using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using Microsoft.UI.Dispatching;

namespace PersonalMediaPlayer.App.Download;

internal static class DownloadQueueHub
{
    private static readonly object Gate = new();
    private static DispatcherQueue? _dispatcher;
    private static CancellationTokenSource? _activeDownload;
    private static DownloadQueueItem? _activeItem;
    private static bool _pumping;

    public static ObservableCollection<DownloadQueueItem> Items { get; } = [];

    public static event EventHandler<DownloadQueueItem>? ItemReady;

    public static void Start(DispatcherQueue dispatcher)
    {
        if (_dispatcher is not null)
        {
            return;
        }

        _dispatcher = dispatcher;
        foreach (var item in DownloadQueueStore.Load())
        {
            Watch(item);
            Items.Add(item);
        }

        Pump();
    }

    public static bool Enqueue(string title, string pageUrl, DownloadQuality quality, DownloadSubtitle? subtitle)
    {
        var added = false;
        OnUi(() =>
        {
            if (IsPending(pageUrl))
            {
                return;
            }

            Accept(new DownloadQueueItem(title, pageUrl, quality, subtitle));
            added = true;
        });
        return added;
    }

    public static void Add(DownloadQueueItem item) => OnUi(() => Accept(item));

    public static bool IsPending(string url)
        => Items.Any(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase)
            && item.Status is "Queued" or "Downloading" or "Paused" or "Ready");

    public static void Pause(DownloadQueueItem item)
    {
        OnUi(() =>
        {
            if (item.Status != "Downloading" || !ReferenceEquals(item, _activeItem))
            {
                return;
            }

            item.MarkPaused();
            _activeDownload?.Cancel();
        });
    }

    public static void Resume(DownloadQueueItem item)
    {
        OnUi(() =>
        {
            if (!item.CanResume)
            {
                return;
            }

            item.MarkQueued(keepProgress: true);
            Pump();
        });
    }

    public static void Cancel(DownloadQueueItem item)
    {
        OnUi(() =>
        {
            if (item.Status is "Queued" or "Paused")
            {
                DeletePartial(item);
                item.MarkCancelled();
                return;
            }

            if (item.Status == "Downloading" && ReferenceEquals(item, _activeItem))
            {
                _activeDownload?.Cancel();
            }
        });
    }

    public static void Retry(DownloadQueueItem item)
    {
        OnUi(() =>
        {
            if (!item.CanRetry)
            {
                return;
            }

            item.MarkQueued();
            Pump();
        });
    }

    public static void Remove(DownloadQueueItem item)
    {
        OnUi(() =>
        {
            Items.Remove(item);
            Save();
        });
    }

    public static void PauseForExit()
    {
        if (_dispatcher is null)
        {
            return;
        }

        OnUi(() =>
        {
            if (_activeItem is { Status: "Downloading" })
            {
                _activeItem.MarkPaused();
                _activeDownload?.Cancel();
            }

            foreach (var item in Items)
            {
                if (item.Status == "Queued")
                {
                    item.MarkPaused();
                }
            }

            Save();
        });
    }

    private static void Accept(DownloadQueueItem item)
    {
        Watch(item);
        Items.Add(item);
        Save();
        Pump();
    }

    private static void Watch(DownloadQueueItem item) => item.PropertyChanged += (_, _) => Save();

    private static void Save() => DownloadQueueStore.Save(Items);

    private static void Pump()
    {
        lock (Gate)
        {
            if (_pumping)
            {
                return;
            }

            _pumping = true;
        }

        _ = PumpAsync();
    }

    private static async Task PumpAsync()
    {
        try
        {
            while (true)
            {
                var run = new PendingRun();
                OnUi(() =>
                {
                    var next = Items.FirstOrDefault(entry => entry.Status == "Queued");
                    if (next is null)
                    {
                        return;
                    }

                    _activeItem = next;
                    _activeDownload?.Dispose();
                    _activeDownload = new CancellationTokenSource();
                    run.Token = _activeDownload.Token;
                    var continuing = next.OutputPath is not null;
                    next.OutputPath ??= YoutubeDownloader.CreateOutputPath(next.Quality.AudioOnly);
                    run.Destination = next.OutputPath;
                    run.Item = next;
                    next.MarkDownloading(continuing);
                });
                if (run.Item is not DownloadQueueItem item)
                {
                    break;
                }

                if (run.Destination is not string output)
                {
                    break;
                }

                var token = run.Token;

                try
                {
                    var path = await YoutubeDownloader.DownloadAsync(
                        item.Url,
                        item.Quality,
                        SubtitleFor(item),
                        item.Title,
                        output,
                        new Progress<double>(value => Post(() =>
                        {
                            if (item.Status == "Downloading")
                            {
                                item.Report(value);
                            }
                        })),
                        token);
                    OnUi(() =>
                    {
                        item.MarkReady(path);
                        ItemReady?.Invoke(null, item);
                    });
                }
                catch (OperationCanceledException)
                {
                    OnUi(() =>
                    {
                        if (item.Status != "Paused")
                        {
                            DeletePartial(item);
                            item.MarkCancelled();
                        }
                    });
                }
                catch (Exception ex)
                {
                    OnUi(() => item.MarkFailed(ex.Message));
                }
                finally
                {
                    OnUi(() =>
                    {
                        if (ReferenceEquals(_activeItem, item))
                        {
                            _activeItem = null;
                        }

                        _activeDownload?.Dispose();
                        _activeDownload = null;
                    });
                }
            }
        }
        finally
        {
            var restart = false;
            try
            {
                if (!OnUi(() =>
                {
                    lock (Gate)
                    {
                        _pumping = false;
                    }

                    restart = Items.Any(entry => entry.Status == "Queued");
                }))
                {
                    lock (Gate)
                    {
                        _pumping = false;
                    }
                }
            }
            catch (Exception)
            {
                lock (Gate)
                {
                    _pumping = false;
                }
            }

            if (restart)
            {
                Pump();
            }
        }
    }

    private static DownloadSubtitle? SubtitleFor(DownloadQueueItem item)
    {
        if (item.SubtitleLanguage is null)
        {
            return null;
        }

        return new DownloadSubtitle(
            item.SubtitleLanguage,
            item.SubtitleLabel ?? item.SubtitleLanguage,
            item.SubtitleAutomatic,
            item.SubtitleTranslated);
    }

    private static void Post(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        dispatcher.TryEnqueue(() => action());
    }

    private static bool OnUi(Action action)
    {
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("The download queue is not started.");
        if (dispatcher.HasThreadAccess)
        {
            action();
            return true;
        }

        Exception? failure = null;
        using var done = new ManualResetEventSlim(false);
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        }))
        {
            return false;
        }

        done.Wait();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return true;
    }

    private sealed class PendingRun
    {
        public DownloadQueueItem? Item { get; set; }

        public CancellationToken Token { get; set; }

        public string? Destination { get; set; }
    }

    private static void DeletePartial(DownloadQueueItem item)
    {
        var folder = Path.GetDirectoryName(item.OutputPath);
        item.OutputPath = null;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
