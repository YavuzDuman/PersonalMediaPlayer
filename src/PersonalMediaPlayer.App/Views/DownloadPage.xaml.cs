using System.Collections.ObjectModel;
using System.Globalization;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.Core.Models;
using PersonalMediaPlayer.App.Helpers;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class DownloadPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ObservableCollection<DownloadQueueItem> _queue = [];
    private readonly ObservableCollection<DownloadHistoryEntry> _history = [];
    private CancellationTokenSource? _lookup;
    private CancellationTokenSource? _activeDownload;
    private DownloadQueueItem? _activeItem;
    private DownloadQueueItem? _previewItem;
    private bool _pumping;
    private DownloadListing? _listing;
    private string? _lookedUpUrl;
    private string? _previewPath;
    private bool _audioOnly;
    private bool _downloading;
    private bool _allowLeave;
    private bool _updatingSlider;
    private bool _dragging;
    private bool _ended;
    private bool _hasDuration;
    private bool _transitioning;
    private long _durationMs;
    private double _lastVolume = 80;
    private LibVLC? _libVlc;
    private VlcMediaPlayer? _player;
    private Media? _media;
    private VideoView? _videoView;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private bool _pendingIsBack;

    public DownloadPage()
    {
        InitializeComponent();
        foreach (var item in DownloadQueueStore.Load())
        {
            Remember(item);
            _queue.Add(item);
        }

        QueueList.ItemsSource = _queue;
        EmptyQueue.Visibility = _queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in DownloadHistory.Load())
        {
            _history.Add(entry);
        }

        HistoryList.ItemsSource = _history;
        EmptyHistory.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _timer.Tick += (_, _) => UpdateClock();
        Playback.SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(Seek_Pressed), true);
        Playback.SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(Seek_Released), true);
        Playback.SeekSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(Seek_Released), true);
        Playback.SeekSlider.ValueChanged += Seek_Changed;
        Playback.PlayButton.Click += Play_Click;
        Playback.BackButton.Click += (_, _) => Skip(-10_000);
        Playback.ForwardButton.Click += (_, _) => Skip(10_000);
        Playback.MuteButton.Click += Mute_Click;
        Playback.VolumeSlider.ValueChanged += Volume_Changed;
        Playback.SpeedCombo.SelectionChanged += (_, _) => _player?.SetRate(SelectedRate());
        Playback.FullScreenButton.Click += (_, _) => _ = ToggleFullScreenAsync();
    }

    private bool HasUnsaved => _queue.Any(item => item.Status is "Queued" or "Downloading" or "Paused" or "Ready");

    internal bool PrepareToLeave(Type? pageType, object? parameter, bool back)
    {
        _pendingPageType = pageType;
        _pendingParameter = parameter;
        _pendingIsBack = back;
        if (_allowLeave || !HasUnsaved)
        {
            return true;
        }

        ShowLeavePrompt();
        return false;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (_allowLeave || !HasUnsaved)
        {
            ReleasePlayer();
            return;
        }

        e.Cancel = true;
        _pendingPageType = e.SourcePageType;
        _pendingParameter = e.Parameter;
        _pendingIsBack = e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back;
        if (LeavePrompt.Visibility != Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(ShowLeavePrompt);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _timer.Stop();
        _lookup?.Cancel();
        if (!_allowLeave)
        {
            PersistForExit();
        }
        if (App.MainAppWindow is MainWindow window && window.IsFullScreen)
        {
            window.SetFullScreen(false);
        }

        ReleasePlayer();
    }

    private async void Lookup_Click(object sender, RoutedEventArgs e) => await LookupAsync();

    private async void LinkBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await LookupAsync();
        }
    }

    private void LinkBox_TextChanged(object sender, TextChangedEventArgs e) => DownloadButton.IsEnabled = CanDownload();

    private bool CanDownload()
    {
        return _listing is not null
            && _lookedUpUrl is not null
            && YoutubeDownloader.SameLinkText(_lookedUpUrl, LinkBox.Text);
    }

    private async Task LookupAsync()
    {
        if (!YoutubeDownloader.TryNormalize(LinkBox.Text, out var uri))
        {
            ShowError("Enter a YouTube, Shorts, or music link.");
            return;
        }

        _lookup?.Cancel();
        _lookup = new CancellationTokenSource();
        var token = _lookup.Token;
        _lookedUpUrl = null;
        _listing = null;
        LookupButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        LookupRing.IsActive = true;
        QualityPanel.Visibility = Visibility.Collapsed;
        StatusBar.IsOpen = false;
        try
        {
            var listing = await YoutubeDownloader.ListAsync(uri.AbsoluteUri, new Progress<string>(text => DownloadStatus.Text = text), token);
            _listing = listing;
            _lookedUpUrl = LinkBox.Text;
            TitleText.Text = listing.Title;
            QualityList.ItemsSource = listing.Qualities;
            QualityList.SelectedIndex = 0;
            QualityPanel.Visibility = Visibility.Visible;
            DownloadStatus.Text = string.Empty;
            DownloadButton.IsEnabled = CanDownload();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            LookupButton.IsEnabled = true;
            LookupRing.IsActive = false;
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (!CanDownload() || _listing is null || QualityList.SelectedItem is not DownloadQuality quality || _lookedUpUrl is null)
        {
            ShowError("Look up this link before adding it.");
            DownloadButton.IsEnabled = false;
            return;
        }

        if (!YoutubeDownloader.TryNormalize(_lookedUpUrl, out var uri))
        {
            ShowError("Look up this link before adding it.");
            return;
        }

        var added = new DownloadQueueItem(_listing.Title, uri.AbsoluteUri, quality);
        Remember(added);
        _queue.Add(added);
        DownloadQueueStore.Save(_queue);
        EmptyQueue.Visibility = Visibility.Collapsed;
        LinkBox.Text = string.Empty;
        _lookedUpUrl = null;
        _listing = null;
        QualityPanel.Visibility = Visibility.Collapsed;
        DownloadButton.IsEnabled = false;
        _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        if (_pumping)
        {
            return;
        }

        _pumping = true;
        try
        {
            while (_queue.FirstOrDefault(item => item.Status == "Queued") is { } item)
            {
                _activeItem = item;
                _activeDownload = new CancellationTokenSource();
                var continuing = item.OutputPath is not null;
                item.OutputPath ??= YoutubeDownloader.CreateOutputPath(item.Quality.AudioOnly);
                item.MarkDownloading(continuing);
                _downloading = true;
                try
                {
                    var path = await YoutubeDownloader.DownloadAsync(
                        item.Url,
                        item.Quality,
                        item.Title,
                        item.OutputPath,
                        new Progress<double>(value => DispatcherQueue.TryEnqueue(() => item.Report(value))),
                        _activeDownload.Token);
                    item.MarkReady(path);
                    if (_previewItem is null)
                    {
                        OpenPreview(item);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (item.Status != "Paused")
                    {
                        DeletePartial(item);
                        item.MarkCancelled();
                    }
                }
                catch (Exception ex)
                {
                    item.MarkFailed(ex.Message);
                }
                finally
                {
                    _downloading = _queue.Any(entry => entry.Status == "Downloading");
                    _activeItem = null;
                    _activeDownload?.Dispose();
                    _activeDownload = null;
                }
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    private void CancelItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadQueueItem item)
        {
            return;
        }

        if (item.Status == "Queued" || item.Status == "Paused")
        {
            DeletePartial(item);
            item.MarkCancelled();
            return;
        }

        if (ReferenceEquals(item, _activeItem))
        {
            _activeDownload?.Cancel();
        }
    }

    private void PauseItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadQueueItem item || !ReferenceEquals(item, _activeItem))
        {
            return;
        }

        item.MarkPaused();
        _activeDownload?.Cancel();
    }

    private void ResumeItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadQueueItem item || !item.CanResume)
        {
            return;
        }

        item.MarkQueued(keepProgress: true);
        _ = PumpAsync();
    }

    private void RetryItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadQueueItem item || !item.CanRetry)
        {
            return;
        }

        item.MarkQueued();
        _ = PumpAsync();
    }

    private void PreviewItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DownloadQueueItem item && item.CanPreview)
        {
            OpenPreview(item);
        }
    }

    private void OpenPreview(DownloadQueueItem item)
    {
        if (item.FilePath is null)
        {
            return;
        }

        _previewItem = item;
        _audioOnly = item.Quality.AudioOnly;
        _listing = new DownloadListing(item.Title, [item.Quality]);
        ShowPreview(item.FilePath, item.Title);
    }

    private void ShowPreview(string path, string title)
    {
        ReleasePlayer();
        _previewPath = path;
        _ended = false;
        _hasDuration = false;
        _durationMs = 0;
        PreviewTitle.Text = title;
        EntryPanel.Visibility = Visibility.Visible;
        PreviewPanel.Visibility = Visibility.Visible;
        AudioMark.Visibility = _audioOnly ? Visibility.Visible : Visibility.Collapsed;
        VideoHost.Visibility = _audioOnly ? Visibility.Collapsed : Visibility.Visible;
        ResetBar();
        if (_audioOnly)
        {
            _libVlc = new LibVLC("--intf", "dummy");
            _player = new VlcMediaPlayer(_libVlc);
            HookPlayer();
            ApplyVolume();
            PlayFile(path);
            _timer.Start();
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, AttachVideo);
    }

    private void AttachVideo()
    {
        if (_videoView is null)
        {
            _videoView = new VideoView();
            _videoView.Initialized += VideoView_Initialized;
        }

        if (VideoHost.Child is null)
        {
            VideoHost.Child = _videoView;
        }
    }

    private void VideoView_Initialized(object? sender, InitializedEventArgs e)
    {
        _libVlc = new LibVLC(false, e.SwapChainOptions);
        _player = new VlcMediaPlayer(_libVlc);
        HookPlayer();
        if (_videoView is not null)
        {
            _videoView.MediaPlayer = _player;
        }

        ApplyVolume();
        if (_previewPath is not null)
        {
            PlayFile(_previewPath);
            _timer.Start();
        }
    }

    private void HookPlayer()
    {
        if (_player is null)
        {
            return;
        }

        _player.LengthChanged += Player_LengthChanged;
        _player.Playing += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayIcon);
        _player.Paused += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayIcon);
        _player.EndReached += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            _ended = true;
            UpdatePlayIcon();
        });
    }

    private void PlayFile(string path)
    {
        if (_libVlc is null || _player is null)
        {
            return;
        }

        _ended = false;
        _media?.Dispose();
        _media = new Media(_libVlc, path, FromType.FromPath);
        _media.Parse(MediaParseOptions.ParseLocal);
        ApplyDuration(_media.Duration);
        _player.Play(_media);
        _player.SetRate(SelectedRate());
        UpdatePlayIcon();
    }

    private void Player_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs args)
        => DispatcherQueue.TryEnqueue(() => ApplyDuration(args.Length));

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
        {
            return;
        }

        if (_player.IsPlaying)
        {
            _player.SetPause(true);
        }
        else if (_ended && _previewPath is not null)
        {
            PlayFile(_previewPath);
        }
        else
        {
            _player.Play();
        }

        UpdatePlayIcon();
    }

    private void Skip(long delta)
    {
        if (_player is null)
        {
            return;
        }

        var length = _hasDuration ? _durationMs : Math.Max(_player.Length, 0);
        var next = Math.Max(0, _player.Time + delta);
        if (length > 0 && next > length)
        {
            next = length;
        }

        _player.Time = next;
        UpdateClock();
    }

    private void Seek_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_hasDuration)
        {
            _dragging = true;
        }
    }

    private void Seek_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (_player is null || !_hasDuration || _durationMs <= 0)
        {
            return;
        }

        _ended = false;
        _player.Time = (long)(Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * _durationMs);
        UpdateClock();
    }

    private void Seek_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider || !_dragging || !_hasDuration || _durationMs <= 0)
        {
            return;
        }

        Playback.PositionText.Text = Format((long)(Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * _durationMs));
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (Playback.VolumeSlider.Value > 0)
        {
            _lastVolume = Playback.VolumeSlider.Value;
            Playback.VolumeSlider.Value = 0;
        }
        else
        {
            Playback.VolumeSlider.Value = _lastVolume <= 0 ? 80 : _lastVolume;
        }

        ApplyVolume();
    }

    private void Volume_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (e.NewValue > 0)
        {
            _lastVolume = e.NewValue;
        }

        ApplyVolume();
    }

    private void ApplyVolume()
    {
        if (_player is not null)
        {
            _player.Mute = Playback.VolumeSlider.Value <= 0;
            _player.Volume = (int)Math.Round(Playback.VolumeSlider.Value);
        }

        Playback.MuteIcon.Glyph = Playback.VolumeSlider.Value <= 0 ? "\uE74F" : "\uE767";
    }

    private float SelectedRate()
    {
        if (Playback.SpeedCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && float.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
            && rate > 0)
        {
            return rate;
        }

        return 1f;
    }

    private void UpdateClock()
    {
        if (_player is null)
        {
            return;
        }

        ApplyDuration(_player.Length);
        if (_dragging)
        {
            return;
        }

        Playback.PositionText.Text = Format(Math.Max(0, _player.Time));
        if (_hasDuration && _durationMs > 0)
        {
            _updatingSlider = true;
            Playback.SeekSlider.Value = Math.Clamp(_player.Time / (double)_durationMs * Playback.SeekSlider.Maximum, 0, Playback.SeekSlider.Maximum);
            _updatingSlider = false;
        }

        UpdatePlayIcon();
    }

    private void ApplyDuration(long duration)
    {
        if (duration <= 0 || _hasDuration)
        {
            return;
        }

        _durationMs = duration;
        _hasDuration = true;
        Playback.SeekSlider.IsEnabled = true;
        Playback.DurationText.Text = Format(duration);
    }

    private void ResetBar()
    {
        _updatingSlider = true;
        Playback.SeekSlider.Value = 0;
        Playback.SeekSlider.IsEnabled = false;
        _updatingSlider = false;
        Playback.PositionText.Text = "00:00";
        Playback.DurationText.Text = "--:--";
        UpdatePlayIcon();
    }

    private void UpdatePlayIcon()
    {
        Playback.PlayIcon.Glyph = _player?.IsPlaying == true ? "\uE769" : "\uE768";
    }

    private static string Format(long milliseconds)
    {
        if (milliseconds < 0)
        {
            return "--:--";
        }

        var time = TimeSpan.FromMilliseconds(milliseconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    private async Task ToggleFullScreenAsync()
    {
        if (_transitioning || App.MainAppWindow is not MainWindow window)
        {
            return;
        }

        _transitioning = true;
        var enter = !window.IsFullScreen;
        if (enter)
        {
            PageGrid.Padding = new Thickness(0);
            PageGrid.RowSpacing = 0;
            HeaderPanel.Visibility = Visibility.Collapsed;
            HistoryPanel.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            PreviewTitleRow.Visibility = Visibility.Collapsed;
        }

        await window.SetFullScreenAsync(enter);
        if (!enter)
        {
            RestoreWindowedLayout();
        }
        else
        {
            Playback.FullScreenIcon.Glyph = "\uE73F";
        }

        _transitioning = false;
    }

    private void RestoreWindowedLayout()
    {
        PageGrid.Padding = new Thickness(24, 8, 24, 24);
        PageGrid.RowSpacing = 12;
        HeaderPanel.Visibility = Visibility.Visible;
        HistoryPanel.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;
        PreviewTitleRow.Visibility = Visibility.Visible;
        Playback.FullScreenIcon.Glyph = "\uE740";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPath is null || _listing is null)
        {
            return;
        }

        if (App.MainAppWindow is MainWindow window && window.IsFullScreen)
        {
            await window.SetFullScreenAsync(false);
            RestoreWindowedLayout();
        }

        var choice = await AskSaveChoiceAsync();
        if (choice is null)
        {
            return;
        }

        if (choice.OnPc)
        {
            var picked = await FilePickerHelper.PickSaveDownloadAsync(App.MainAppWindow, choice.Name, _audioOnly);
            if (picked is null)
            {
                return;
            }

            await FinishSaveAsync(async source =>
            {
                await using var input = File.OpenRead(source);
                await using var output = await picked.OpenStreamForWriteAsync();
                output.SetLength(0);
                await input.CopyToAsync(output);
                return (picked.Path, picked.Path);
            });
            return;
        }

        await FinishSaveAsync(source =>
        {
            var extension = _audioOnly ? ".m4a" : ".mp4";
            using var input = File.OpenRead(source);
            var saved = App.MediaLibrary.ImportMedia(input, choice.Name + extension, choice.Album);
            var message = string.IsNullOrWhiteSpace(choice.Album) ? "the library" : choice.Album!;
            return Task.FromResult((message, saved.FilePath));
        });
    }

    private async Task<SaveChoice?> AskSaveChoiceAsync()
    {
        var nameBox = new TextBox
        {
            Header = "Name",
            Text = YoutubeDownloader.FileName(_listing!.Title, _audioOnly)
        };
        var method = new ComboBox
        {
            Header = "Save to",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items = { "A folder in this app", "A folder on this PC" },
            SelectedIndex = 0
        };
        var album = new ComboBox
        {
            Header = "App folder",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        album.Items.Add("Library");
        foreach (var folder in App.MediaLibrary.GetFolders().Where(folder => !folder.IsSystem))
        {
            album.Items.Add(folder.Name);
        }

        album.Items.Add("New folder…");
        album.SelectedIndex = 0;
        var newFolder = new TextBox { Header = "New folder name", Visibility = Visibility.Collapsed };
        album.SelectionChanged += (_, _) =>
        {
            newFolder.Visibility = album.SelectedItem as string == "New folder…" ? Visibility.Visible : Visibility.Collapsed;
        };
        method.SelectionChanged += (_, _) =>
        {
            var inApp = method.SelectedIndex == 0;
            album.Visibility = inApp ? Visibility.Visible : Visibility.Collapsed;
            newFolder.Visibility = inApp && album.SelectedItem as string == "New folder…" ? Visibility.Visible : Visibility.Collapsed;
        };
        var dialog = new ContentDialog
        {
            Title = "Save download",
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { nameBox, method, album, newFolder }
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var name = YoutubeDownloader.FileName(nameBox.Text, _audioOnly);
        if (method.SelectedIndex == 1)
        {
            return new SaveChoice(name, true, null);
        }

        if (album.SelectedItem as string == "New folder…")
        {
            if (string.IsNullOrWhiteSpace(newFolder.Text))
            {
                ShowError("Enter a folder name.");
                return null;
            }

            var created = App.MediaLibrary.CreateFolder(newFolder.Text);
            return new SaveChoice(name, false, created);
        }

        var selected = album.SelectedItem as string;
        return new SaveChoice(name, false, selected == "Library" ? null : selected);
    }

    private async Task FinishSaveAsync(Func<string, Task<(string Message, string Path)>> write)
    {
        ReleasePlayer();
        var source = _previewPath!;
        var savedItem = _previewItem;
        _previewPath = null;
        try
        {
            var saved = await write(source);
            try
            {
                File.Delete(source);
            }
            catch (IOException)
            {
            }

            savedItem?.MarkSaved();
            if (savedItem is not null)
            {
                _queue.Remove(savedItem);
            }

            var entry = new DownloadHistoryEntry
            {
                Name = Path.GetFileName(saved.Path),
                Url = savedItem?.Url ?? string.Empty,
                Quality = savedItem?.Quality.Label ?? string.Empty,
                SavedAt = DateTimeOffset.Now,
                Location = saved.Path
            };
            _history.Insert(0, entry);
            DownloadHistory.Save(_history);
            EmptyHistory.Visibility = Visibility.Collapsed;
            _previewItem = null;
            ResetToEntry();
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Message = "Saved to " + saved.Message;
            StatusBar.IsOpen = true;
            if (_queue.FirstOrDefault(entry => entry.Status == "Ready") is { } next)
            {
                OpenPreview(next);
            }
        }
        catch (Exception ex)
        {
            _previewPath = source;
            ShowPreview(source, _listing!.Title);
            ShowError(ex.Message);
        }
    }

    private sealed record SaveChoice(string Name, bool OnPc, string? Album);

    private void Discard_Click(object sender, RoutedEventArgs e) => ClearPreview();

    private void ClearPreview()
    {
        if (App.MainAppWindow is MainWindow window && window.IsFullScreen)
        {
            window.SetFullScreen(false);
            RestoreWindowedLayout();
        }

        ReleasePlayer();
        if (_previewPath is not null && File.Exists(_previewPath))
        {
            try
            {
                File.Delete(_previewPath);
            }
            catch (IOException)
            {
            }
        }

        if (_previewItem is not null)
        {
            _queue.Remove(_previewItem);
            _previewItem = null;
        }

        _previewPath = null;
        ResetToEntry();
        EmptyQueue.Visibility = _queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetToEntry()
    {
        _timer.Stop();
        PreviewPanel.Visibility = Visibility.Collapsed;
        EntryPanel.Visibility = Visibility.Visible;
        DownloadStatus.Text = string.Empty;
        DownloadButton.IsEnabled = CanDownload();
        EmptyQueue.Visibility = _queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowLeavePrompt()
    {
        LeavePromptText.Text = _queue.Any(item => item.Status is "Queued" or "Downloading" or "Paused")
            ? "Downloads are still in the queue. Leave and cancel them?"
            : "A downloaded video is not saved yet. Leave without saving?";
        LeavePrompt.Visibility = Visibility.Visible;
    }

    private void LeavePromptStay_Click(object sender, RoutedEventArgs e) => LeavePrompt.Visibility = Visibility.Collapsed;

    private void LeavePromptConfirm_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
        _lookup?.Cancel();
        _activeDownload?.Cancel();
        _downloading = false;
        foreach (var item in _queue.Where(entry => entry.FilePath is not null).ToArray())
        {
            try
            {
                if (File.Exists(item.FilePath))
                {
                    File.Delete(item.FilePath);
                }
            }
            catch (IOException)
            {
            }
        }

        _queue.Clear();
        DownloadQueueStore.Save(_queue);
        _previewItem = null;
        _previewPath = null;
        ReleasePlayer();
        ResetToEntry();
        _allowLeave = true;
        var moved = false;
        if (_pendingIsBack && Frame.CanGoBack)
        {
            Frame.GoBack();
            moved = true;
        }
        else if (_pendingPageType is not null && Frame.Navigate(_pendingPageType, _pendingParameter))
        {
            Frame.BackStack.Clear();
            moved = true;
        }

        if (moved && App.MainAppWindow is MainWindow window)
        {
            window.SyncNavigationSelection();
        }
    }

    private void OpenHistoryFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadHistoryEntry entry)
        {
            return;
        }

        if (!File.Exists(entry.Location))
        {
            ShowError("That file is no longer there.");
            return;
        }

        var item = new MediaItem
        {
            Id = entry.Location,
            Kind = MediaKind.Video,
            DisplayName = entry.Name,
            FilePath = entry.Location,
            ImportedAt = entry.SavedAt,
            FileSizeBytes = new FileInfo(entry.Location).Length
        };
        Frame.Navigate(typeof(VideoPlayerPage), item);
    }

    private void OpenHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadHistoryEntry entry)
        {
            return;
        }

        try
        {
            DownloadHistory.OpenFolder(entry.Location);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void RemoveHistory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadHistoryEntry entry)
        {
            return;
        }

        _history.Remove(entry);
        DownloadHistory.Save(_history);
        EmptyHistory.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void PersistForExit()
    {
        if (_activeItem is { Status: "Downloading" })
        {
            _activeItem.MarkPaused();
            _activeDownload?.Cancel();
        }

        DownloadQueueStore.Save(_queue);
    }

    private void Remember(DownloadQueueItem item)
        => item.PropertyChanged += (_, _) => DownloadQueueStore.Save(_queue);

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

    private void ShowError(string message)
    {
        StatusBar.Severity = InfoBarSeverity.Error;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void ReleasePlayer()
    {
        _timer.Stop();
        if (_player is not null)
        {
            _player.LengthChanged -= Player_LengthChanged;
            try
            {
                _player.Stop();
            }
            catch (Exception)
            {
            }

            if (_videoView is not null)
            {
                _videoView.MediaPlayer = null;
            }

            _player.Dispose();
            _player = null;
        }

        _media?.Dispose();
        _media = null;
        _libVlc?.Dispose();
        _libVlc = null;
        if (VideoHost.Child is not null)
        {
            VideoHost.Child = null;
        }

        _videoView = null;
    }
}
