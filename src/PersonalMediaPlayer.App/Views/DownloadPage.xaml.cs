using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;
using PersonalMediaPlayer.Core.Models;
using PersonalMediaPlayer.App.Helpers;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class DownloadPage : Page, IPlaybackSource
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ObservableCollection<DownloadHistoryEntry> _history = [];
    private CancellationTokenSource? _lookup;
    private DownloadQueueItem? _previewItem;
    private DownloadListing? _listing;
    private DownloadSubtitle? _subtitle;
    private string? _lookedUpUrl;
    private string? _previewPath;
    private bool _audioOnly;
    private bool _left;
    private bool _savingBatch;
    private static bool _historyOpen;
    private readonly HashSet<DownloadQueueItem> _hookedQueue = [];
    private bool _updatingSlider;
    private bool _dragging;
    private bool _ended;
    private bool _pausedForOther;
    private bool _hasDuration;
    private bool _transitioning;
    private bool _fullScreenLayout;
    private long _durationMs;
    private double _lastVolume = 80;
    private LibVLC? _libVlc;
    private VlcMediaPlayer? _player;
    private Media? _media;
    private VideoView? _videoView;
    private CancellationTokenSource? _captionLoad;
    private int _captionGeneration;

    public DownloadPage()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyDownloadLayout();
        QueueList.ItemsSource = DownloadQueueHub.Items;
        UpdateEmptyQueue();
        foreach (var entry in DownloadHistory.Load())
        {
            _history.Add(entry);
        }

        HistoryList.ItemsSource = _history;
        HistoryList.Loaded += (_, _) => AttachHistoryDrag();
        EmptyHistory.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyHistory();
        _timer.Tick += (_, _) => UpdateClock();
        Playback.SubtitlesChanged += (_, shown) => Captions.SetShown(shown);
        Playback.SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(Seek_Pressed), true);
        Playback.SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(Seek_Released), true);
        Playback.SeekSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(Seek_Released), true);
        Playback.SeekSlider.ValueChanged += Seek_Changed;
        Playback.PlayButton.Click += Play_Click;
        Playback.BackButton.Click += (_, _) => Skip(-10_000);
        Playback.ForwardButton.Click += (_, _) => Skip(10_000);
        Playback.MuteButton.Click += Mute_Click;
        var remembered = PlaybackVolume.LoadPreview();
        _lastVolume = remembered.Audible;
        Playback.VolumeSlider.Value = remembered.Level;
        ApplyVolume();
        Playback.VolumeSlider.ValueChanged += Volume_Changed;
        Playback.SpeedCombo.SelectionChanged += (_, _) => _player?.SetRate(SelectedRate());
        Playback.FullScreenButton.Click += (_, _) => _ = ToggleFullScreenAsync();
        PlaybackFocus.Register(this);
    }

    private void AttachHistoryDrag()
    {
        if (SearchScroll.Find(HistoryList) is ScrollViewer scroller)
        {
            CardDragScroll.Attach(scroller);
            return;
        }

        void Later(object? sender, object e)
        {
            if (SearchScroll.Find(HistoryList) is not ScrollViewer found)
            {
                return;
            }

            HistoryList.LayoutUpdated -= Later;
            HistoryList.Unloaded -= Stop;
            CardDragScroll.Attach(found);
        }

        void Stop(object sender, RoutedEventArgs e)
        {
            HistoryList.LayoutUpdated -= Later;
            HistoryList.Unloaded -= Stop;
        }

        HistoryList.LayoutUpdated -= Later;
        HistoryList.Unloaded -= Stop;
        HistoryList.LayoutUpdated += Later;
        HistoryList.Unloaded += Stop;
    }

    private void HistoryToggle_Click(object sender, RoutedEventArgs e)
    {
        _historyOpen = !_historyOpen;
        ApplyHistory();
        AttachHistoryDrag();
    }

    private void ApplyHistory()
    {
        HistoryBody.Visibility = _historyOpen ? Visibility.Visible : Visibility.Collapsed;
        HistoryToggle.Content = _historyOpen ? "Hide" : "Show";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _left = false;
        DownloadQueueHub.Items.CollectionChanged -= Queue_Changed;
        DownloadQueueHub.Items.CollectionChanged += Queue_Changed;
        DownloadQueueHub.ItemReady -= Download_Ready;
        DownloadQueueHub.ItemReady += Download_Ready;
        TrackQueueItems();
        UpdateEmptyQueue();
    }

    private void Queue_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TrackQueueItems();
        UpdateEmptyQueue();
    }

    private void TrackQueueItems()
    {
        foreach (var item in _hookedQueue.ToArray())
        {
            if (!DownloadQueueHub.Items.Contains(item))
            {
                item.PropertyChanged -= QueueItem_Changed;
                _hookedQueue.Remove(item);
            }
        }

        foreach (var item in DownloadQueueHub.Items)
        {
            if (_hookedQueue.Add(item))
            {
                item.PropertyChanged += QueueItem_Changed;
            }
        }

        UpdateSaveSelected();
    }

    private void UntrackQueueItems()
    {
        foreach (var item in _hookedQueue)
        {
            item.PropertyChanged -= QueueItem_Changed;
        }

        _hookedQueue.Clear();
    }

    private void QueueItem_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(DownloadQueueItem.Selected) or nameof(DownloadQueueItem.Status))
        {
            UpdateSaveSelected();
        }
    }

    private void UpdateSaveSelected()
    {
        var ready = false;
        var selected = false;
        foreach (var item in DownloadQueueHub.Items)
        {
            if (item.Status != "Ready")
            {
                continue;
            }

            ready = true;
            if (item.Selected)
            {
                selected = true;
            }
        }

        SaveSelectedButton.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        SaveSelectedButton.IsEnabled = selected && !_savingBatch;
    }

    private void Download_Ready(object? sender, DownloadQueueItem item)
    {
        if (_previewItem is null)
        {
            OpenPreview(item);
        }
    }

    private void UpdateEmptyQueue()
        => EmptyQueue.Visibility = DownloadQueueHub.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        _left = true;
        ReleasePlayer();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _left = true;
        _timer.Stop();
        _lookup?.Cancel();
        DownloadQueueHub.Items.CollectionChanged -= Queue_Changed;
        DownloadQueueHub.ItemReady -= Download_Ready;
        UntrackQueueItems();
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
        _subtitle = null;
        LookupButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        LookupRing.IsActive = true;
        QualityPanel.Visibility = Visibility.Collapsed;
        StatusBar.IsOpen = false;
        try
        {
            if (YoutubeDownloader.IsPlaylist(uri.AbsoluteUri))
            {
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.Message = "Reading the playlist…";
                StatusBar.IsOpen = true;
                var playlist = await YoutubeDownloader.ListPlaylistAsync(uri.AbsoluteUri, new Progress<string>(text => DownloadStatus.Text = text), token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                StatusBar.IsOpen = false;
                var choice = await AskPlaylistAsync(playlist);
                if (choice is null)
                {
                    return;
                }

                QueuePlaylist(choice.Value.Quality, choice.Value.Videos);
                return;
            }

            var listing = await YoutubeDownloader.ListAsync(uri.AbsoluteUri, new Progress<string>(text => DownloadStatus.Text = text), token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _subtitle = await AskSubtitleAsync(listing.Subtitles);
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

        DownloadQueueHub.Add(new DownloadQueueItem(_listing.Title, uri.AbsoluteUri, quality, quality.AudioOnly ? null : _subtitle));
        LinkBox.Text = string.Empty;
        _lookedUpUrl = null;
        _listing = null;
        _subtitle = null;
        QualityPanel.Visibility = Visibility.Collapsed;
        DownloadButton.IsEnabled = false;
    }

    private void CancelItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadQueueItem item)
        {
            return;
        }

        DownloadQueueHub.Cancel(item);
    }

    private void PauseItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadQueueItem item)
        {
            return;
        }

        DownloadQueueHub.Pause(item);
    }

    private void ResumeItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadQueueItem item)
        {
            return;
        }

        DownloadQueueHub.Resume(item);
    }

    private void QueuePlaylist(DownloadQuality quality, IReadOnlyList<PlaylistVideo> videos)
    {
        var added = 0;
        var skipped = 0;
        foreach (var video in videos)
        {
            if (DownloadQueueHub.IsPending(video.Url))
            {
                skipped++;
                continue;
            }

            DownloadQueueHub.Add(new DownloadQueueItem(video.Title, video.Url, quality));
            added++;
        }

        LinkBox.Text = string.Empty;
        StatusBar.Severity = added > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        StatusBar.Message = added == 0
            ? "Those videos are already in the queue."
            : skipped == 0
                ? (added == 1 ? "Added 1 video." : $"Added {added} videos.")
                : $"Added {added} videos. {skipped} {(skipped == 1 ? "was" : "were")} already in the queue.";
        StatusBar.IsOpen = true;
    }

    private async Task<(DownloadQuality Quality, IReadOnlyList<PlaylistVideo> Videos)?> AskPlaylistAsync(PlaylistListing playlist)
    {
        var qualities = YoutubeDownloader.PlaylistQualities();
        var quality = new ComboBox
        {
            Header = "Quality",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = qualities,
            DisplayMemberPath = nameof(DownloadQuality.Label),
            SelectedIndex = 0
        };
        var boxes = new List<(CheckBox Box, PlaylistVideo Video)>();
        var list = new StackPanel { Spacing = 4 };
        foreach (var video in playlist.Videos)
        {
            var label = string.IsNullOrWhiteSpace(video.Length) ? video.Title : video.Title + " · " + video.Length;
            var box = new CheckBox
            {
                IsChecked = true,
                Content = new TextBlock
                {
                    Text = label,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 360
                }
            };
            boxes.Add((box, video));
            list.Children.Add(box);
        }

        var updating = false;
        var all = new CheckBox { Content = "All videos", IsChecked = true, IsThreeState = true };
        all.Checked += (_, _) => SetAll(true);
        all.Unchecked += (_, _) => SetAll(false);
        foreach (var (box, _) in boxes)
        {
            box.Checked += (_, _) => RefreshAll();
            box.Unchecked += (_, _) => RefreshAll();
        }

        var note = playlist.TotalCount > playlist.Videos.Count
            ? $"Showing the first {playlist.Videos.Count} of {playlist.TotalCount}. Each selected video is added on its own. Look up one video when you want captions."
            : "Each selected video is added on its own. Look up one video when you want captions.";
        var dialog = new ContentDialog
        {
            Title = playlist.Title,
            Content = new StackPanel
            {
                Spacing = 12,
                Width = 420,
                Children =
                {
                    new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap },
                    quality,
                    all,
                    new ScrollViewer
                    {
                        MaxHeight = 320,
                        Content = list
                    }
                }
            },
            PrimaryButtonText = "Add to queue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || quality.SelectedItem is not DownloadQuality selected)
        {
            return null;
        }

        var chosen = boxes.Where(item => item.Box.IsChecked == true).Select(item => item.Video).ToArray();
        if (chosen.Length == 0)
        {
            ShowError("Select at least one video.");
            return null;
        }

        return (selected, chosen);

        void SetAll(bool check)
        {
            if (updating)
            {
                return;
            }

            updating = true;
            foreach (var (box, _) in boxes)
            {
                box.IsChecked = check;
            }

            updating = false;
        }

        void RefreshAll()
        {
            if (updating)
            {
                return;
            }

            updating = true;
            var count = boxes.Count(item => item.Box.IsChecked == true);
            all.IsChecked = count == boxes.Count ? true : count == 0 ? false : null;
            updating = false;
        }
    }

    private async Task<DownloadSubtitle?> AskSubtitleAsync(IReadOnlyList<DownloadSubtitle> tracks)
    {
        if (tracks.Count == 0)
        {
            return null;
        }

        var choices = new List<DownloadSubtitle> { new(null, "No subtitles", false) };
        choices.AddRange(tracks);
        var languages = new ComboBox
        {
            Header = "Language",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxDropDownHeight = 320,
            ItemsSource = choices,
            DisplayMemberPath = nameof(DownloadSubtitle.Label),
            SelectedIndex = 0
        };
        var dialog = new ContentDialog
        {
            Title = "Include subtitles?",
            Content = new StackPanel
            {
                Spacing = 12,
                Width = 360,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Choose a subtitle. Uploaded captions come first, then YouTube’s automatic captions, including translations. A translation can take about a minute. Audio-only downloads stay without subtitles.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    languages
                }
            },
            PrimaryButtonText = "Continue",
            CloseButtonText = "No subtitles",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return languages.SelectedItem as DownloadSubtitle;
    }

    private void RetryItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DownloadQueueItem item)
        {
            return;
        }

        DownloadQueueHub.Retry(item);
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
        if (_left || item.FilePath is null)
        {
            return;
        }

        _previewItem = item;
        _audioOnly = item.Quality.AudioOnly;
        _listing = new DownloadListing(item.Title, [item.Quality], []);
        ShowPreview(item.FilePath, item.Title);
    }

    private void ShowPreview(string path, string title)
    {
        if (_left)
        {
            return;
        }

        ReleasePlayer();
        _pausedForOther = false;
        PlaybackFocus.Claim(this);
        _previewPath = path;
        _ended = false;
        _hasDuration = false;
        _durationMs = 0;
        PreviewTitle.Text = title;
        EntryPanel.Visibility = Visibility.Visible;
        PreviewPanel.Visibility = Visibility.Visible;
        ApplyDownloadLayout();
        AudioMark.Visibility = _audioOnly ? Visibility.Visible : Visibility.Collapsed;
        VideoHost.Visibility = _audioOnly ? Visibility.Collapsed : Visibility.Visible;
        ResetBar();
        if (_audioOnly)
        {
            _libVlc = new LibVLC(PlaybackAudio.Options("--intf", "dummy", "--no-sub-autodetect-file"));
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
            _videoView = new VideoView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _videoView.Initialized += VideoView_Initialized;
        }

        if (VideoHost.Child is null)
        {
            VideoHost.Child = _videoView;
        }
    }

    private void VideoView_Initialized(object? sender, InitializedEventArgs e)
    {
        _libVlc = new LibVLC(false, PlaybackAudio.Options(e.SwapChainOptions.Concat(new[] { "--no-sub-autodetect-file" }).ToArray()));
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
        _player.Playing += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_player is null)
            {
                return;
            }

            if (_pausedForOther)
            {
                try
                {
                    _player.SetPause(true);
                }
                catch (Exception)
                {
                    // Playback already moved to another video.
                }

                UpdatePlayIcon();
                return;
            }

            PlaybackFocus.Claim(this);
            Playback.UseSubtitles(_player);
            UpdatePlayIcon();
        });
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
        _media.AddOption(":no-sub-autodetect-file");
        _media.AddOption(":sub-track=0");
        _captionLoad?.Cancel();
        _captionLoad = new CancellationTokenSource();
        var generation = ++_captionGeneration;
        var token = _captionLoad.Token;
        Captions.Prepare(path);
        if (!_pausedForOther)
        {
            TakePlayback();
            _player.Play(_media);
            _player.SetRate(SelectedRate());
            UpdatePlayIcon();
        }

        _ = LoadPreviewCaptionsAsync(path, generation, token);
    }

    private async Task LoadPreviewCaptionsAsync(string path, int generation, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubtitleCue> cues;
        try
        {
            cues = await Task.Run(() => SubtitleCues.LoadFor(path, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            return;
        }

        if (generation != _captionGeneration || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            ShowPreviewCues(path, generation, cues);
            return;
        }

        DispatcherQueue.TryEnqueue(() => ShowPreviewCues(path, generation, cues));
    }

    private void ShowPreviewCues(string path, int generation, IReadOnlyList<SubtitleCue> cues)
    {
        if (generation != _captionGeneration || _left || !string.Equals(_previewPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Captions.ApplyLoadedCues(cues);
        if (Captions.HasCues)
        {
            Playback.OfferCaptions();
        }

        if (_player is not null)
        {
            Captions.SetTime(_player.Time);
        }
    }

    private void Player_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs args)
        => DispatcherQueue.TryEnqueue(() =>
        {
            ApplyDuration(args.Length);
            if (_player is not null)
            {
                Playback.UseSubtitles(_player);
            }
        });

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
            _pausedForOther = false;
            PlayFile(_previewPath);
        }
        else
        {
            TakePlayback();
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

        PlaybackVolume.SavePreview(e.NewValue);
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
        Captions.SetTime(_player.Time);
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
        Playback.CloseSettings();
        var enter = !window.IsFullScreen;
        if (enter)
        {
            _fullScreenLayout = true;
            PageGrid.Padding = new Thickness(0);
            PageGrid.RowSpacing = 0;
            HeaderPanel.Visibility = Visibility.Collapsed;
            HistoryPanel.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            PreviewTitleRow.Visibility = Visibility.Collapsed;
            ApplyDownloadLayout();
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
        Playback.CloseSettings();
        _fullScreenLayout = false;
        HeaderPanel.Visibility = Visibility.Visible;
        HistoryPanel.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;
        PreviewTitleRow.Visibility = Visibility.Visible;
        Playback.FullScreenIcon.Glyph = "\uE740";
        ApplyDownloadLayout();
    }

    private void ApplyDownloadLayout()
    {
        if (PageGrid is null || EntryColumn is null)
        {
            return;
        }

        if (_fullScreenLayout)
        {
            PlaceDownload(wide: true);
            return;
        }

        var width = ActualWidth;
        PageGrid.Padding = width > 0 && width < 720
            ? new Thickness(16, 8, 16, 16)
            : new Thickness(24, 8, 24, 24);
        PageGrid.RowSpacing = 12;
        PlaceDownload(wide: width <= 0 || width >= 980);
    }

    private void PlaceDownload(bool wide)
    {
        var previewOpen = PreviewPanel.Visibility == Visibility.Visible;
        if (wide)
        {
            EntryColumn.Width = previewOpen ? new GridLength(360) : new GridLength(1, GridUnitType.Star);
            PreviewColumn.Width = previewOpen ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            EntryRow.Height = new GridLength(1, GridUnitType.Star);
            PreviewRow.Height = new GridLength(0);
            WorkGrid.ColumnSpacing = previewOpen ? 16 : 0;
            WorkGrid.RowSpacing = 0;
            Grid.SetColumn(EntryPanel, 0);
            Grid.SetColumnSpan(EntryPanel, 1);
            Grid.SetRow(EntryPanel, 0);
            Grid.SetColumn(PreviewPanel, 1);
            Grid.SetColumnSpan(PreviewPanel, 1);
            Grid.SetRow(PreviewPanel, 0);
            PreviewPanel.ClearValue(FrameworkElement.MinHeightProperty);
            return;
        }

        EntryColumn.Width = new GridLength(1, GridUnitType.Star);
        PreviewColumn.Width = new GridLength(0);
        WorkGrid.ColumnSpacing = 0;
        Grid.SetColumn(EntryPanel, 0);
        Grid.SetColumnSpan(EntryPanel, 1);
        Grid.SetRow(EntryPanel, 0);
        Grid.SetColumn(PreviewPanel, 0);
        Grid.SetColumnSpan(PreviewPanel, 1);
        Grid.SetRow(PreviewPanel, 1);
        if (previewOpen)
        {
            EntryRow.Height = new GridLength(1, GridUnitType.Star);
            PreviewRow.Height = new GridLength(1, GridUnitType.Star);
            WorkGrid.RowSpacing = 16;
            PreviewPanel.MinHeight = 280;
        }
        else
        {
            EntryRow.Height = new GridLength(1, GridUnitType.Star);
            PreviewRow.Height = new GridLength(0);
            WorkGrid.RowSpacing = 0;
            PreviewPanel.ClearValue(FrameworkElement.MinHeightProperty);
        }
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
            RememberSavedDownload(savedItem, source, saved.Path);
            _previewItem = null;
            ResetToEntry();
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Message = "Saved to " + saved.Message;
            StatusBar.IsOpen = true;
            if (!_left && DownloadQueueHub.Items.FirstOrDefault(entry => entry.Status == "Ready") is { } next)
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

    private void RememberSavedDownload(DownloadQueueItem? item, string source, string savedPath)
    {
        SavedWords.Move(source, savedPath);
        Playlists.MoveFile(source, savedPath);
        try
        {
            File.Delete(source);
        }
        catch (IOException)
        {
        }

        item?.MarkSaved();
        if (item is not null)
        {
            DownloadQueueHub.Remove(item);
        }

        _history.Insert(0, new DownloadHistoryEntry
        {
            Name = Path.GetFileName(savedPath),
            Url = item?.Url ?? string.Empty,
            Quality = item?.Quality.Label ?? string.Empty,
            SavedAt = DateTimeOffset.Now,
            Location = savedPath
        });
        DownloadHistory.Save(_history);
        EmptyHistory.Visibility = Visibility.Collapsed;
    }

    private async void SaveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_savingBatch)
        {
            return;
        }

        var chosen = DownloadQueueHub.Items.Where(item => item.Selected && item.Status == "Ready").ToList();
        if (chosen.Count == 0)
        {
            return;
        }

        _savingBatch = true;
        UpdateSaveSelected();
        try
        {
            if (App.MainAppWindow is MainWindow window && window.IsFullScreen)
            {
                await window.SetFullScreenAsync(false);
                RestoreWindowedLayout();
            }

            var choice = await AskSelectedSaveAsync(chosen.Count);
            if (choice is null)
            {
                return;
            }

            if (choice.OnPc)
            {
                var folder = await FilePickerHelper.PickFolderAsync(App.MainAppWindow);
                if (folder is null || string.IsNullOrWhiteSpace(folder.Path))
                {
                    return;
                }

                await SaveSelectedToFolderAsync(chosen, folder.Path);
                return;
            }

            await SaveSelectedToLibraryAsync(chosen, choice.Album);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _savingBatch = false;
            UpdateSaveSelected();
        }
    }

    private async Task SaveSelectedToFolderAsync(IReadOnlyList<DownloadQueueItem> chosen, string folder)
    {
        var ready = new List<DownloadQueueItem>();
        var results = new List<DownloadBatchResult>();
        foreach (var item in chosen)
        {
            if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
            {
                results.Add(FailedResult(item, "That download is no longer there."));
                continue;
            }

            ready.Add(item);
        }

        IReadOnlyList<DownloadSaveTarget> plans = ready.Count == 0
            ? []
            : DownloadBatchSave.Plan(ready.Select(item => new DownloadBatchFile(item.Title, item.FilePath!, item.Quality.AudioOnly)).ToList(), folder);
        var replace = true;
        if (plans.Any(plan => plan.AlreadyThere))
        {
            var conflicts = plans.Where(plan => plan.AlreadyThere).Select(plan => plan.FileName).ToList();
            var choice = await AskReplaceAsync(conflicts);
            if (choice == DownloadReplaceChoice.Cancel)
            {
                return;
            }

            replace = choice == DownloadReplaceChoice.Replace;
        }

        var previewInBatch = ReleasePreviewIfSelected(chosen);
        ShowSaving();
        if (plans.Count > 0)
        {
            var copied = await Task.Run(() => DownloadBatchSave.CopyAll(plans, replace));
            foreach (var result in copied)
            {
                results.Add(CommitCopied(ready, result));
            }
        }

        FinishBatch(previewInBatch, results, "that folder");
    }

    private async Task SaveSelectedToLibraryAsync(IReadOnlyList<DownloadQueueItem> chosen, string? album)
    {
        var previewInBatch = ReleasePreviewIfSelected(chosen);
        ShowSaving();
        var results = new List<DownloadBatchResult>();
        foreach (var item in chosen)
        {
            var source = item.FilePath;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                results.Add(FailedResult(item, "That download is no longer there."));
                continue;
            }

            try
            {
                var name = DownloadBatchSave.SavedName(item.Title, source, item.Quality.AudioOnly);
                var savedPath = await Task.Run(() =>
                {
                    using var input = File.OpenRead(source);
                    return App.MediaLibrary.ImportMedia(input, name, album).FilePath;
                });
                RememberSavedDownload(item, source, savedPath);
                results.Add(new DownloadBatchResult(source, item.Title, Path.GetFileName(savedPath), DownloadBatchOutcome.Saved, savedPath, null));
            }
            catch (Exception ex)
            {
                results.Add(item.Status == "Saved"
                    ? new DownloadBatchResult(source, item.Title, Path.GetFileName(source), DownloadBatchOutcome.Saved, null, null)
                    : FailedResult(item, DownloadBatchSave.Shorten(ex.Message)));
            }
        }

        var place = string.IsNullOrWhiteSpace(album) ? "the library" : "the " + album + " album";
        FinishBatch(previewInBatch, results, place);
    }

    private DownloadBatchResult CommitCopied(IReadOnlyList<DownloadQueueItem> ready, DownloadBatchResult result)
    {
        if (result.Outcome != DownloadBatchOutcome.Saved || result.SavedPath is null)
        {
            return result;
        }

        var item = ready.FirstOrDefault(entry => string.Equals(entry.FilePath, result.SourcePath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return result with { Outcome = DownloadBatchOutcome.Failed, SavedPath = null, Detail = "That download is no longer in the queue." };
        }

        try
        {
            RememberSavedDownload(item, result.SourcePath, result.SavedPath);
            return result;
        }
        catch (Exception ex)
        {
            return item.Status == "Saved"
                ? result
                : result with { Outcome = DownloadBatchOutcome.Failed, SavedPath = null, Detail = DownloadBatchSave.Shorten(ex.Message) };
        }
    }

    private static DownloadBatchResult FailedResult(DownloadQueueItem item, string detail)
        => new(item.FilePath ?? string.Empty, item.Title, item.Title, DownloadBatchOutcome.Failed, null, detail);

    private bool ReleasePreviewIfSelected(IReadOnlyList<DownloadQueueItem> chosen)
    {
        if (_previewItem is null || !chosen.Contains(_previewItem))
        {
            return false;
        }

        ReleasePlayer();
        _previewPath = null;
        return true;
    }

    private void ShowSaving()
    {
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Message = "Saving the selected downloads…";
        StatusBar.IsOpen = true;
    }

    private void FinishBatch(bool previewInBatch, IReadOnlyList<DownloadBatchResult> results, string place)
    {
        if (!_left)
        {
            RestorePreviewAfterBatch(previewInBatch);
        }

        if (results.Count == 0)
        {
            return;
        }

        StatusBar.Severity = BatchSeverity(results);
        StatusBar.Message = DownloadBatchSave.Describe(results, place);
        StatusBar.IsOpen = true;
    }

    private void RestorePreviewAfterBatch(bool previewInBatch)
    {
        if (!previewInBatch)
        {
            return;
        }

        if (_previewItem is { Status: "Ready", FilePath: { } path } preview && File.Exists(path))
        {
            _audioOnly = preview.Quality.AudioOnly;
            _listing = new DownloadListing(preview.Title, [preview.Quality], []);
            ShowPreview(path, preview.Title);
            return;
        }

        _previewItem = null;
        _previewPath = null;
        ResetToEntry();
        if (!_left && DownloadQueueHub.Items.FirstOrDefault(entry => entry.Status == "Ready" && entry.FilePath is { } readyPath && File.Exists(readyPath)) is { } next)
        {
            OpenPreview(next);
        }
    }

    private static InfoBarSeverity BatchSeverity(IReadOnlyList<DownloadBatchResult> results)
    {
        var saved = results.Any(item => item.Outcome == DownloadBatchOutcome.Saved);
        var notSaved = results.Any(item => item.Outcome != DownloadBatchOutcome.Saved);
        if (saved && notSaved)
        {
            return InfoBarSeverity.Warning;
        }

        if (!saved && results.Any(item => item.Outcome == DownloadBatchOutcome.Failed))
        {
            return InfoBarSeverity.Error;
        }

        if (saved)
        {
            return InfoBarSeverity.Success;
        }

        return InfoBarSeverity.Informational;
    }

    private async Task<DownloadReplaceChoice> AskReplaceAsync(IReadOnlyList<string> names)
    {
        var dialog = new ContentDialog
        {
            Title = "Replace existing files?",
            Content = new ScrollViewer
            {
                MaxHeight = 280,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "These files are already in that folder. Replacing them overwrites those files.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = string.Join(Environment.NewLine, names),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            },
            PrimaryButtonText = "Replace",
            SecondaryButtonText = "Keep existing files",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => DownloadReplaceChoice.Replace,
            ContentDialogResult.Secondary => DownloadReplaceChoice.KeepExisting,
            _ => DownloadReplaceChoice.Cancel
        };
    }

    private async Task<SelectedSaveChoice?> AskSelectedSaveAsync(int count)
    {
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
            Title = "Save selected downloads",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = count == 1 ? "1 finished download" : count + " finished downloads"
                    },
                    new TextBlock
                    {
                        Text = "Each file keeps its download name. A folder on this PC asks before replacing a file that is already there. A folder in this app keeps an existing library file and saves the download as a new file.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    method,
                    album,
                    newFolder
                }
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

        if (method.SelectedIndex == 1)
        {
            return new SelectedSaveChoice(true, null);
        }

        if (album.SelectedItem as string == "New folder…")
        {
            if (string.IsNullOrWhiteSpace(newFolder.Text))
            {
                ShowError("Enter a folder name.");
                return null;
            }

            var created = App.MediaLibrary.CreateFolder(newFolder.Text);
            return new SelectedSaveChoice(false, created);
        }

        var selected = album.SelectedItem as string;
        return new SelectedSaveChoice(false, selected == "Library" ? null : selected);
    }

    private sealed record SaveChoice(string Name, bool OnPc, string? Album);

    private sealed record SelectedSaveChoice(bool OnPc, string? Album);

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
            DownloadQueueHub.Remove(_previewItem);
            _previewItem = null;
        }

        _previewPath = null;
        ResetToEntry();
    }

    private void ResetToEntry()
    {
        _timer.Stop();
        PreviewPanel.Visibility = Visibility.Collapsed;
        EntryPanel.Visibility = Visibility.Visible;
        ApplyDownloadLayout();
        DownloadStatus.Text = string.Empty;
        DownloadButton.IsEnabled = CanDownload();
        UpdateEmptyQueue();
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
        NavigationHelper.OpenPlayer(item);
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

    private void ShowError(string message)
    {
        StatusBar.Severity = InfoBarSeverity.Error;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void TakePlayback()
    {
        _pausedForOther = false;
        PlaybackFocus.Claim(this);
    }

    void IPlaybackSource.PauseForOther()
    {
        _pausedForOther = true;
        if (_player is not { IsPlaying: true })
        {
            UpdatePlayIcon();
            return;
        }

        try
        {
            _player.SetPause(true);
        }
        catch (Exception)
        {
            // The preview can already be stopped when another video starts.
        }

        UpdatePlayIcon();
    }

    private void ReleasePlayer()
    {
        _captionGeneration++;
        _captionLoad?.Cancel();
        _captionLoad = null;
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
