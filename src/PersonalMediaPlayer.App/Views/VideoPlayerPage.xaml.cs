using System.Diagnostics;
using System.Globalization;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;
using PersonalMediaPlayer.Core.Models;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace PersonalMediaPlayer.App.Views;

internal sealed record VideoOpenRequest(MediaItem Item, long StartMs, SavedWord? Focus = null);

internal sealed record PlaylistOpenRequest(string PlaylistId, int Index);

public sealed partial class VideoPlayerPage : Page, IPlaybackSource
{
    private const long MaxRealisticMs = 24L * 60 * 60 * 1000;

    private const string TranslationWaitMessage = "Loading this translation. YouTube holds it for about a minute.";

    private const string TranslationFailedMessage = "This translation did not load.";

    private readonly DispatcherTimer _timer;
    private LibVLC? _libVlc;
    private VlcMediaPlayer? _player;
    private Media? _streamMedia;
    private string? _pendingPath;
    private Uri? _pendingStream;
    private bool _pendingPageResolve;
    private string? _filePath;
    private Uri? _streamUrl;
    private Uri? _streamReferrer;
    private long? _streamStartMs;
    private Uri? _streamPage;
    private Uri? _streamAudio;
    private IReadOnlyList<DownloadSubtitle>? _streamSubtitles;
    private DownloadSubtitle? _streamSubtitle;
    private DownloadQuality? _streamQuality;
    private string? _streamThumbnail;
    private string? _hlsBody;
    private Uri? _hlsSource;
    private HlsMaster? _hlsMaster;
    private string? _hlsAudioKey;
    private string? _restoreAudioLanguage;
    private int _hlsHeight;
    private string? _hlsPath;
    private string? _hlsPending;
    private int[]? _localAudioIds;
    private long _streamLastMs;
    private bool _streamEndedCleanly;
    private bool _streamResolving;
    private int _streamErrorRetries;
    private int _playbackEpoch;
    private CancellationTokenSource? _subtitleWork;
    private CancellationTokenSource? _captionLoad;
    private int _captionGeneration;
    private bool _deferSubtitleFetch;
    private int? _deferredSubtitle;
    private bool _subtitleReleasePending;
    private int _enrichGeneration;
    private CancellationToken _enrichToken;
    private bool _captionsEnriched;
    private bool _captionChosen;
    private string? _captionChoice;
    private bool _applyingCaptionOffer;
    private bool _canSeek;
    private bool _seekKnown;
    private bool _streamFailed;
    private bool _streamOpening;
    private int _streamGeneration;
    private CancellationTokenSource? _streamWork;
    private bool _editing;

    private bool HasTrimChange =>
        _editing && _durationMs > 0 && (_trimStartMs > 50 || _durationMs - _trimEndMs > 50);
    private bool _savingTrim;
    private bool _grabbingFrame;
    private long _trimStartMs;
    private long _trimEndMs;
    private string[]? _swapChainOptions;
    private long _resumeMs;
    private bool _resumePending;
    private long? _openAtMs;
    private bool _explicitStart;
    private SavedWord? _savedFocus;
    private string? _playlistId;
    private int _playlistIndex = -1;
    private bool _fromQueue;
    private IReadOnlyList<VideoChapter> _chapters = [];
    private bool _chaptersFromLookup;
    private int _chapterIndex = -1;
    private int _chapterEpoch;
    private long? _captionHoldMs;
    private bool _ended;
    private long _lastRememberedMs;
    private bool _dragging;
    private bool _updatingSlider;
    private bool _hasValidDuration;
    private long _durationMs;
    private long? _repeatAMs;
    private long? _repeatBMs;
    private double _lastVolume = 80;
    private bool _transitioning;
    private bool _mini;
    private bool _wordsBeforeMini;
    private bool _searchBeforeMini;
    private bool _playlistBeforeMini;
    private bool _bookmarksBeforeMini;
    private bool _volumeSync;
    private bool _pausedForOther;
    private bool _restorePaused;
    private long? _restoreTargetMs;
    private bool _heldMissing;
    private bool _miniDragging;
    private bool _updatingMiniSeek;
    private bool _allowLeave;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private bool _pendingIsBack;
    private static readonly Thickness WindowedPadding = new(24, 8, 24, 24);

    public VideoPlayerPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => UpdateClockAndBar();
        Playback.SubtitlesChanged += (_, shown) => Captions.SetShown(shown);

        Playback.SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPressed), handledEventsToo: true);
        Playback.SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekReleased), handledEventsToo: true);
        Playback.SeekSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnSeekReleased), handledEventsToo: true);
        Playback.SeekSlider.ValueChanged += SeekSlider_ValueChanged;
        var remembered = PlaybackVolume.Load();
        _lastVolume = remembered.Audible;
        Playback.VolumeSlider.Value = remembered.Level;
        MiniVolume.Value = remembered.Level;
        UpdateMuteIcon();
        Playback.VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        MiniVolume.ValueChanged += MiniVolume_ValueChanged;
        MiniSeek.AddHandler(PointerPressedEvent, new PointerEventHandler(MiniSeek_Pressed), handledEventsToo: true);
        MiniSeek.AddHandler(PointerReleasedEvent, new PointerEventHandler(MiniSeek_Released), handledEventsToo: true);
        MiniSeek.AddHandler(PointerCanceledEvent, new PointerEventHandler(MiniSeek_Released), handledEventsToo: true);
        MiniSeek.ValueChanged += MiniSeek_ValueChanged;
        PlaybackFocus.Register(this);
        Playback.MuteButton.Click += MuteButton_Click;
        Playback.BackButton.Click += RewindButton_Click;
        Playback.ForwardButton.Click += ForwardButton_Click;
        Playback.PlayButton.Click += PlayPauseButton_Click;
        Playback.SpeedCombo.SelectionChanged += SpeedCombo_SelectionChanged;
        Playback.FullScreenButton.Click += FullscreenButton_Click;
        Playback.SnapshotButton.Visibility = Visibility.Visible;
        Playback.SnapshotButton.Click += Snapshot_Click;
        Playback.BookmarkButton.Visibility = Visibility.Visible;
        Playback.BookmarkButton.Click += Bookmark_Click;
        Playback.UseSectionRepeat(true);
        WordsPanel.WordChosen += WordsPanel_WordChosen;
        PlaylistPanel.VideoChosen += (_, index) => OpenPlaylistVideo(index);
        PlaylistPanel.ReplayChosen += (_, _) => RestartPlaylistVideo();
        PlaylistPanel.ModeChanged += (_, _) => RefreshPlaylistLabel();
        PlaylistPanel.OrderChanged += (_, index) => ApplyPlaylistOrder(index);
        PlaylistPanel.NextChosen += (_, _) => AdvanceForward();
        QueuePanel.NextChosen += (_, _) => AdvanceForward();
        ChapterPanel.ChapterChosen += (_, args) => PlayChapter(args.StartMs);
        SubtitleSearchPanel.CueChosen += (_, time) => PlayChapter(time);
        PlayQueue.Changed += OnQueueChanged;
        Unloaded += (_, _) => PlayQueue.Changed -= OnQueueChanged;
        Captions.WordSaved += (_, _) => WordsPanel.Refresh();
        Playback.CaptionChosen += (_, index) => ChoosePageSubtitle(index);
        Playback.QualityChosen += (_, index) => ChooseStreamQuality(index);
        Playback.AudioChosen += (_, index) => ChooseAudio(index);
        Playback.SectionARequested += (_, _) => SetRepeatPoint(a: true);
        Playback.SectionBRequested += (_, _) => SetRepeatPoint(a: false);
        Playback.SectionClearRequested += (_, _) => ClearSectionRepeat();
        Playback.TrimRangeChanged += (_, _) => ApplyTrimFromBar();
        Playback.TrimSeekRequested += (_, fraction) => SeekToFraction(fraction);
        VideoView.Loaded += (_, _) => EnsurePlayback();
        VideoHost.Tapped += (_, _) =>
        {
            if (_mini)
            {
                MiniExpand_Click(this, new RoutedEventArgs());
            }
        };
        KeyDown += VideoPlayerPage_KeyDown;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => Open(e.Parameter);

    internal bool HasSession =>
        _player is not null
        || _pendingPath is not null
        || _pendingStream is not null
        || _pendingPageResolve
        || _streamResolving
        || _streamUrl is not null
        || _streamPage is not null
        || !string.IsNullOrWhiteSpace(_filePath);

    internal void RestoreHeld(WatchingVideo video)
    {
        _restorePaused = true;
        _restoreTargetMs = Math.Max(0, video.PositionMs);
        _fromQueue = false;
        _pausedForOther = false;
        switch (video.Kind)
        {
            case WatchingKind.File:
                if (!CanOpenFile(video.Location))
                {
                    ShowMissingHeld(video);
                    return;
                }

                _openAtMs = Math.Max(0, video.PositionMs);
                _explicitStart = true;
                try
                {
                    OpenFileItem(MediaFor(video.Location));
                }
                catch (Exception)
                {
                    ShowMissingHeld(video);
                }

                return;
            case WatchingKind.Page:
                OpenHeldAddress(video, page: true);
                return;
            default:
                OpenHeldAddress(video, page: false);
                return;
        }
    }

    internal void Open(object? parameter)
    {
        if (TryKeep(parameter))
        {
            return;
        }

        ReleaseRestoredHold();
        _heldMissing = false;
        _pausedForOther = false;
        if (parameter is StreamOpenRequest stream)
        {
            _fromQueue = false;
            StopForReplacement();
            ShowStream(stream);
            return;
        }

        if (parameter is PlaylistOpenRequest playlistRequest)
        {
            OpenPlaylistRequest(playlistRequest);
            return;
        }

        var item = parameter switch
        {
            VideoOpenRequest request => App.MediaLibrary.GetById(request.Item.Id) ?? request.Item,
            MediaItem media => App.MediaLibrary.GetById(media.Id) ?? media,
            string id => App.MediaLibrary.GetById(id),
            _ => null
        };
        if (item is null)
        {
            return;
        }

        _fromQueue = false;
        StopForReplacement();
        _openAtMs = null;
        _savedFocus = null;
        LeavePlaylist();
        if (parameter is VideoOpenRequest open)
        {
            _openAtMs = Math.Max(0, open.StartMs);
            _savedFocus = open.Focus;
        }

        OpenFileItem(item);
    }

    private void OpenPlaylistRequest(PlaylistOpenRequest playlistRequest)
    {
        var list = Playlists.Find(playlistRequest.PlaylistId);
        var inRange = list is not null && playlistRequest.Index >= 0 && playlistRequest.Index < list.Videos.Count;
        if (list is null || !inRange)
        {
            if (!HasSession)
            {
                TitleText.Text = "Playlist";
                AddedText.Text = "That playlist is no longer available.";
            }

            return;
        }

        var entry = list.Videos[playlistRequest.Index];
        _fromQueue = false;
        StopForReplacement();
        _openAtMs = null;
        _savedFocus = null;
        _playlistId = list.Id;
        _playlistIndex = playlistRequest.Index;
        if (entry.Resolve)
        {
            OpenSavedPage(entry, keepPlaylist: true);
            return;
        }

        var path = entry.Location;
        if (!File.Exists(path))
        {
            StopWatchingStream();
            TitleText.Text = Path.GetFileName(path);
            AddedText.Text = PlaylistLabel();
            _filePath = path;
            WordsPanel.CurrentVideoPath = path;
            RestoreButton.Visibility = Visibility.Collapsed;
            ResetDuration();
            WatchingSession.RememberFile(TitleText.Text, path, 0);
            ShowPlaylist();
            StartWatching();
            return;
        }

        OpenFileItem(MediaFor(path));
    }

    private void OpenHeldAddress(WatchingVideo video, bool page)
    {
        if (!StreamLink.TryNormalize(video.Location, out var address))
        {
            ReleaseRestoredHold();
            WatchingSession.Forget();
            return;
        }

        var title = string.IsNullOrWhiteSpace(video.Title) ? StreamLink.DisplayName(address) : video.Title;
        Uri? thumbnail = null;
        if (video.Thumbnail is not null && Uri.TryCreate(video.Thumbnail, UriKind.Absolute, out var picture))
        {
            thumbnail = picture;
        }

        var start = video.PositionMs > 0 ? video.PositionMs : 0;
        ShowStream(page
            ? new StreamOpenRequest(address, title, Page: address, StartMs: start, Thumbnail: thumbnail)
            : new StreamOpenRequest(address, title, StartMs: start, Thumbnail: thumbnail));
    }

    private void ShowMissingHeld(WatchingVideo video)
    {
        ReleaseRestoredHold();
        _heldMissing = true;
        StopWatchingStream();
        var name = string.IsNullOrWhiteSpace(video.Title) ? Path.GetFileName(video.Location) : video.Title;
        TitleText.Text = string.IsNullOrWhiteSpace(name) ? "Video" : name;
        AddedText.Text = "Missing";
        _filePath = video.Location;
        WordsPanel.CurrentVideoPath = video.Location;
        RestoreButton.Visibility = Visibility.Collapsed;
        ResetDuration();
        _pendingPath = null;
        MiniPosition.Text = "Missing";
        MiniSeek.IsEnabled = false;
        StartWatching();
    }

    private static bool CanOpenFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ReleaseRestoredHold()
    {
        _restorePaused = false;
        _restoreTargetMs = null;
    }

    private void ApplyRestoredPause()
    {
        if (!_restorePaused || _player is null || _resumePending)
        {
            return;
        }

        if (_player.State is VLCState.NothingSpecial or VLCState.Opening or VLCState.Buffering)
        {
            return;
        }

        // Wait until a seekable video reaches the saved moment. Pausing at the
        // start would let the pause event replace that moment.
        if (_streamStartMs is > 0 && (!_seekKnown || _canSeek))
        {
            return;
        }

        var target = _restoreTargetMs ?? 0;
        var time = Math.Max(0, _player.Time);
        var landed = target < 1_500 || Math.Abs(time - target) <= 1_500;
        var cannotSeek = _streamUrl is not null && _seekKnown && !_canSeek;
        if (!landed && !cannotSeek)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.SetPause(true);
            }
        }
        catch (Exception)
        {
            return;
        }

        if (!_player.IsPlaying && landed)
        {
            if (_hasValidDuration && time + 1_500 < _durationMs)
            {
                _ended = false;
            }

            ReleaseRestoredHold();
        }
    }

    private void OpenFileItem(MediaItem item)
    {
        _heldMissing = false;
        StopWatchingStream();
        TitleText.Text = item.DisplayName;
        AddedText.Text = _fromQueue
            ? QueueCaption()
            : _playlistId is null
                ? $"Added {item.ImportedAt.ToLocalTime():g}"
                : PlaylistLabel();
        RestoreButton.Visibility = App.MediaLibrary.HasOriginal(item.FilePath)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _filePath = item.FilePath;
        if (VideoHost.Child is null)
        {
            VideoHost.Child = VideoView;
        }

        Playback.SetHoverSource(item.FilePath);
        ShowBookmarks();
        ResetDuration();
        _pendingPath = item.FilePath;
        WordsPanel.CurrentVideoPath = item.FilePath;
        if (_savedFocus is null)
        {
            Captions.ClearSavedWord();
        }
        else
        {
            Captions.ShowSavedWord(_savedFocus.English, _savedFocus.Sentence, _savedFocus.TimeMs);
            WordsPanel.Show(_savedFocus);
        }

        if (_player is not null)
        {
            PlayFile(item.FilePath);
        }

        if (_playlistId is not null)
        {
            ShowPlaylist();
        }

        StartWatching();
    }

    private void StopForReplacement()
    {
        RememberPosition(force: true);
        _playbackEpoch++;
        _streamGeneration++;
        _streamWork?.Cancel();
        _subtitleWork?.Cancel();
        CancelCaptionLoad();
        _deferSubtitleFetch = false;
        _deferredSubtitle = null;
        _subtitleReleasePending = false;
        _captionsEnriched = false;
        _captionChosen = false;
        _captionChoice = null;
        _pendingPath = null;
        _pendingStream = null;
        _pendingPageResolve = false;
        _streamResolving = false;
        _streamOpening = false;
        ClearChapters();
        if (_player is null)
        {
            return;
        }

        try
        {
            _player.Stop();
        }
        catch (Exception)
        {
            // The player can already be stopped when another video replaces it.
        }

        _ended = true;
    }

    private void StartWatching()
    {
        _timer.Start();
        UpdateClockAndBar();
        UpdatePlayIcon();
        ShowQueue();
        ShowChapters();
        if (HostWindow is not null)
        {
            HostWindow.AppWindow.Changed -= AppWindow_Changed;
            HostWindow.AppWindow.Changed += AppWindow_Changed;
        }
    }

    private bool TryKeep(object? parameter)
    {
        if (_player is null && _pendingPath is null && _pendingStream is null && !_pendingPageResolve && !_streamResolving)
        {
            return false;
        }

        var jump = WantsJump(parameter, out var jumpMs);
        var visit = PlaybackVisitChoice.Choose(CurrentMediaKey(), RequestedMediaKey(parameter), jump, jumpMs);
        if (visit.Kind == PlaybackVisitKind.Open)
        {
            return false;
        }

        if (parameter is PlaylistOpenRequest playlist)
        {
            _playlistId = playlist.PlaylistId;
            _playlistIndex = playlist.Index;
            ShowPlaylist();
            if (_fromQueue)
            {
                AddedText.Text = QueueCaption();
            }
            else if (!string.IsNullOrWhiteSpace(_filePath) && _streamPage is null && _streamUrl is null)
            {
                AddedText.Text = PlaylistLabel();
            }
        }

        if (visit.Kind == PlaybackVisitKind.Seek)
        {
            if (parameter is StreamOpenRequest { Focus: { } word })
            {
                OpenSavedStreamWord(word);
            }
            else if (parameter is VideoOpenRequest open)
            {
                _savedFocus = open.Focus;
                if (open.Focus is { } focus)
                {
                    Captions.ShowSavedWord(focus.English, focus.Sentence, focus.TimeMs, redraw: true);
                    WordsPanel.Show(focus);
                }

                SeekToSavedTime(visit.SeekMs);
            }
            else
            {
                SeekToSavedTime(visit.SeekMs);
            }
        }

        StartWatching();
        return true;
    }

    private static bool WantsJump(object? parameter, out long time)
    {
        switch (parameter)
        {
            case VideoOpenRequest open:
                time = Math.Max(0, open.StartMs);
                return true;
            case StreamOpenRequest stream when stream.Focus?.TimeMs is long focus:
                time = Math.Max(0, focus);
                return true;
            case StreamOpenRequest stream when stream.StartMs is long start && start > 0:
                time = start;
                return true;
            default:
                time = 0;
                return false;
        }
    }

    internal bool MatchesPage(Uri page)
        => string.Equals(CurrentMediaKey(), page.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

    private string? CurrentMediaKey()
    {
        if (!string.IsNullOrWhiteSpace(_filePath) && _streamPage is null && _streamUrl is null)
        {
            return _filePath.Trim();
        }

        return StreamWordKey();
    }

    private string? RequestedMediaKey(object? parameter)
    {
        switch (parameter)
        {
            case VideoOpenRequest open:
                return string.IsNullOrWhiteSpace(open.Item.FilePath) ? null : open.Item.FilePath.Trim();
            case MediaItem media:
                return string.IsNullOrWhiteSpace(media.FilePath) ? null : media.FilePath.Trim();
            case string id:
                var item = App.MediaLibrary.GetById(id);
                return string.IsNullOrWhiteSpace(item?.FilePath) ? null : item.FilePath.Trim();
            case StreamOpenRequest stream:
                return (stream.Page ?? stream.Url).AbsoluteUri;
            case PlaylistOpenRequest playlist:
                var list = Playlists.Find(playlist.PlaylistId);
                if (list is null || playlist.Index < 0 || playlist.Index >= list.Videos.Count)
                {
                    return null;
                }

                var entry = list.Videos[playlist.Index];
                if (entry.Resolve && StreamLink.TryNormalize(entry.Location, out var page))
                {
                    return page.AbsoluteUri;
                }

                return string.IsNullOrWhiteSpace(entry.Location) ? null : entry.Location.Trim();
            default:
                return null;
        }
    }

    internal void SetChrome(bool mini)
    {
        if (mini == _mini)
        {
            return;
        }

        if (mini)
        {
            _wordsBeforeMini = WordsPanel.Visibility == Visibility.Visible;
            _searchBeforeMini = SubtitleSearchPanel.Visibility == Visibility.Visible;
            _playlistBeforeMini = PlaylistPanel.Visibility == Visibility.Visible;
            _bookmarksBeforeMini = BookmarkHost.Visibility == Visibility.Visible;
            HeaderPanel.Visibility = Visibility.Collapsed;
            Playback.Visibility = Visibility.Collapsed;
            BookmarkHost.Visibility = Visibility.Collapsed;
            WordsPanel.Visibility = Visibility.Collapsed;
            SubtitleSearchPanel.Visibility = Visibility.Collapsed;
            PlaylistPanel.Visibility = Visibility.Collapsed;
            QueuePanel.Visibility = Visibility.Collapsed;
            ChapterPanel.Visibility = Visibility.Collapsed;
            Captions.Visibility = Visibility.Collapsed;
            MiniBar.Visibility = Visibility.Visible;
            RootGrid.Padding = new Thickness(0);
            RootGrid.RowSpacing = 0;
            VideoHost.CornerRadius = new CornerRadius(12, 12, 0, 0);
            _mini = true;
            SyncMiniVolume();
            UpdatePlayIcon();
            return;
        }

        _mini = false;
        MiniBar.Visibility = Visibility.Collapsed;
        HeaderPanel.Visibility = Visibility.Visible;
        Playback.Visibility = Visibility.Visible;
        Captions.Visibility = Visibility.Visible;
        RootGrid.Padding = WindowedPadding;
        RootGrid.RowSpacing = 12;
        VideoHost.CornerRadius = new CornerRadius(16);
        if (_wordsBeforeMini)
        {
            WordsPanel.Visibility = Visibility.Visible;
        }

        if (_searchBeforeMini)
        {
            SubtitleSearchPanel.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(SearchButton, "Hide subtitle search");
        }

        if (_bookmarksBeforeMini)
        {
            BookmarkHost.Visibility = Visibility.Visible;
        }

        if (_playlistBeforeMini && _playlistId is not null)
        {
            PlaylistPanel.Visibility = Visibility.Visible;
            ShowPlaylist();
        }

        ShowQueue();
        ShowChapters();
    }

    internal void ApplyBackdrop(bool mini)
    {
        if (mini)
        {
            var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            Background = clear;
            RootGrid.Background = clear;
            HeaderPanel.Background = clear;
            return;
        }

        var dark = ActualTheme == ElementTheme.Dark;
        var backdrop = new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
            : Windows.UI.Color.FromArgb(255, 243, 243, 243));
        Background = backdrop;
        RootGrid.Background = backdrop;
        HeaderPanel.Background = backdrop;
    }

    internal void LeaveFullScreen() => ExitFullScreen();

    internal void RememberForClose() => RememberPosition(force: true);

    internal void Shutdown()
    {
        _timer.Stop();
        ReleaseRestoredHold();
        _heldMissing = false;
        WatchingSession.Forget();
        if (HostWindow?.IsFullScreen == true)
        {
            ExitFullScreen();
        }

        RememberPosition(force: true);
        _streamGeneration++;
        _streamWork?.Cancel();
        _subtitleWork?.Cancel();
        CancelCaptionLoad();
        _subtitleReleasePending = false;
        LeavePlaylist();
        _filePath = null;
        _pendingPath = null;
        _pendingStream = null;
        _pendingPageResolve = false;
        _streamResolving = false;
        _streamUrl = null;
        _streamPage = null;
        _savedFocus = null;
        _openAtMs = null;
        _fromQueue = false;
        _chapterEpoch++;
        _chapters = [];
        _chaptersFromLookup = false;
        _chapterIndex = -1;
        _wordsBeforeMini = false;
        _searchBeforeMini = false;
        _playlistBeforeMini = false;
        _bookmarksBeforeMini = false;
        WordsPanel.Visibility = Visibility.Collapsed;
        SubtitleSearchPanel.Visibility = Visibility.Collapsed;
        ToolTipService.SetToolTip(SearchButton, "Search subtitles");
        BookmarkHost.Visibility = Visibility.Collapsed;
        Captions.ClearSavedWord();
        DetachPlayback();
        _mini = false;
        MiniBar.Visibility = Visibility.Collapsed;
        HeaderPanel.Visibility = Visibility.Visible;
        Playback.Visibility = Visibility.Visible;
        Captions.Visibility = Visibility.Visible;
        RootGrid.Padding = WindowedPadding;
        RootGrid.RowSpacing = 12;
        VideoHost.CornerRadius = new CornerRadius(16);
        ShowQueue();
        ShowChapters();
    }

    private void MiniExpand_Click(object sender, RoutedEventArgs e)
    {
        HostWindow?.ExpandPlayer();
    }

    private void MiniClose_Click(object sender, RoutedEventArgs e)
    {
        HostWindow?.ClosePlayer();
    }

    private void SyncMiniVolume()
    {
        if (Math.Abs(MiniVolume.Value - Playback.VolumeSlider.Value) < 0.1)
        {
            return;
        }

        _volumeSync = true;
        MiniVolume.Value = Playback.VolumeSlider.Value;
        _volumeSync = false;
    }

    private void MiniSeek_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (!MiniSeek.IsEnabled)
        {
            return;
        }

        _miniDragging = true;
    }

    private void MiniSeek_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_miniDragging)
        {
            return;
        }

        _miniDragging = false;
        if (_player is null || !_hasValidDuration || _durationMs <= 0 || (_streamUrl is not null && !_canSeek))
        {
            return;
        }

        var time = (long)(MiniSeek.Value / MiniSeek.Maximum * _durationMs);
        if (_editing)
        {
            time = Math.Clamp(time, _trimStartMs, _trimEndMs);
        }

        ReleaseRestoredHold();
        _player.Time = time;
        HoldCaptions(time);
        UpdateClockAndBar();
    }

    private void MiniSeek_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingMiniSeek || !_miniDragging || !_hasValidDuration || _durationMs <= 0)
        {
            return;
        }

        MiniPosition.Text = FormatMs((long)(e.NewValue / MiniSeek.Maximum * _durationMs));
    }

    private void MiniVolume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_volumeSync)
        {
            return;
        }

        _volumeSync = true;
        Playback.VolumeSlider.Value = e.NewValue;
        _volumeSync = false;
    }

    internal bool PrepareToLeave(Type? pageType, object? parameter, bool back)
    {
        _pendingPageType = pageType;
        _pendingParameter = parameter;
        _pendingIsBack = back;
        if (!HasTrimChange && !_savingTrim)
        {
            return true;
        }

        ShowLeavePrompt();
        return false;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (_allowLeave || (!HasTrimChange && !_savingTrim))
        {
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

    private void ShowLeavePrompt()
    {
        LeavePromptText.Text = _savingTrim
            ? "A trim is still saving. Stay until it finishes."
            : "This trim is not saved. Leave without keeping it?";
        LeavePromptConfirm.Visibility = _savingTrim ? Visibility.Collapsed : Visibility.Visible;
        LeavePrompt.Visibility = Visibility.Visible;
    }

    private void LeavePromptStay_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
    }

    private void LeavePromptConfirm_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
        LeaveTrimMode();
        _allowLeave = true;
        HostWindow?.CompletePlayerLeave(_pendingIsBack, _pendingPageType, _pendingParameter);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        WordsPanel.Remember();
        _streamGeneration++;
        _streamWork?.Cancel();
        _subtitleWork?.Cancel();
        if (HostWindow is not null)
        {
            HostWindow.AppWindow.Changed -= AppWindow_Changed;
        }

        _timer.Stop();
        RememberPosition(force: true);
        _openAtMs = null;
        _explicitStart = false;
        _pendingPath = null;
        Playback.SetHoverSource(null);
        ExitFullScreen();
        ResetDuration();
        DetachPlayback();
    }

    private void DetachPlayback()
    {
        if (_player is not null)
        {
            _player.LengthChanged -= Player_LengthChanged;
            _player.SeekableChanged -= Player_SeekableChanged;
            _player.EncounteredError -= Player_EncounteredError;
            try
            {
                _player.Stop();
            }
            catch (Exception)
            {
                // The player can already be stopped when the page is leaving.
            }

            VideoView.MediaPlayer = null;
            _player.Dispose();
            _player = null;
        }

        ReleaseStreamMedia();
        DeletePlaylistFile(_hlsPath);
        _hlsPath = null;
        DeletePlaylistFile(_hlsPending);
        _hlsPending = null;
        _libVlc?.Dispose();
        _libVlc = null;
        _swapChainOptions = null;
        if (VideoHost.Child is not null)
        {
            VideoHost.Child = null;
        }
    }

    private void VideoView_Initialized(object sender, InitializedEventArgs e)
    {
        _swapChainOptions = e.SwapChainOptions;
        EnsurePlayback();
    }

    private void EnsurePlayback()
    {
        if (_player is not null || _swapChainOptions is null)
        {
            return;
        }

        _libVlc = new LibVLC(enableDebugLogs: false, PlaybackAudio.Options(_swapChainOptions.Concat(new[] { "--no-sub-autodetect-file" }).ToArray()));
        _player = new VlcMediaPlayer(_libVlc);
        _player.LengthChanged += Player_LengthChanged;
        _player.SeekableChanged += Player_SeekableChanged;
        _player.EncounteredError += Player_EncounteredError;
        HookPlayer();
        VideoView.MediaPlayer = _player;
        ApplyVolumeToPlayer();
        ApplyRateToPlayer();
        if (_pausedForOther)
        {
            return;
        }

        if (_pendingPageResolve)
        {
            _pendingPageResolve = false;
            _ = ResolvePageAsync(_streamStartMs);
        }
        else if (_pendingStream is Uri pendingStream)
        {
            _ = OpenStreamAsync(pendingStream);
        }
        else if (!string.IsNullOrWhiteSpace(_pendingPath))
        {
            PlayFile(_pendingPath);
        }
    }

    private void PlayFile(string path)
    {
        if (_libVlc is null || _player is null)
        {
            return;
        }

        StopWatchingStream();
        ClearStreamChoices();
        _captionHoldMs = null;
        ResetDuration();
        if (_openAtMs is long openAt)
        {
            _resumeMs = Math.Max(0, openAt);
            _resumePending = true;
            _explicitStart = true;
        }
        else
        {
            _resumeMs = PlaybackProgress.Load(path);
            _resumePending = _resumeMs >= 5_000;
            _explicitStart = false;
        }

        _lastRememberedMs = _resumeMs;
        WatchingSession.RememberFile(TitleText.Text, path, _resumeMs);
        _captionLoad?.Cancel();
        _captionLoad = new CancellationTokenSource();
        var generation = ++_captionGeneration;
        var token = _captionLoad.Token;
        using var media = new Media(_libVlc, path, FromType.FromPath);
        _ended = false;
        media.AddOption(":no-sub-autodetect-file");
        media.AddOption(":sub-track=0");
        Playback.ClearHoverCaptions();
        Captions.Prepare(path);
        TakePlayback();
        _player.Play(media);
        ApplyRateToPlayer();
        SubtitleSearchPanel.BeginVideo();
        _ = LoadFileCaptionsAsync(path, generation, token);
    }

    private void CancelCaptionLoad()
    {
        _captionGeneration++;
        _captionLoad?.Cancel();
        _captionLoad = null;
    }

    private async Task LoadFileCaptionsAsync(string path, int generation, CancellationToken cancellationToken)
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
            ShowFileCues(path, generation, cues);
            return;
        }

        DispatcherQueue.TryEnqueue(() => ShowFileCues(path, generation, cues));
    }

    private void ShowFileCues(string path, int generation, IReadOnlyList<SubtitleCue> cues)
    {
        if (generation != _captionGeneration || !string.Equals(_filePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Captions.ApplyLoadedCues(cues);
        SubtitleSearchPanel.ShowCues(cues);
        if (_savedFocus is { } focus)
        {
            Captions.ShowSavedWord(focus.English, focus.Sentence, focus.TimeMs, redraw: false);
        }

        if (Captions.HasCues)
        {
            Playback.OfferCaptions();
        }

        if (_player is not null)
        {
            Captions.SetTime(_captionHoldMs ?? _player.Time);
        }
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
            // The player can already be stopped when another video starts.
        }

        UpdatePlayIcon();
    }

    private void Player_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        var epoch = _playbackEpoch;
        var chapters = _chapterEpoch;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (epoch != _playbackEpoch || chapters != _chapterEpoch)
            {
                return;
            }

            ApplyDuration(e.Length, "vlc-length");
            if (_player is not null)
            {
                Playback.UseSubtitles(_player);
            }

            TryResume();
            ApplyRestoredPause();
            ReadPlayerChapters();
        });
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_heldMissing)
        {
            AddedText.Text = "Missing";
            return;
        }

        if (_player is null)
        {
            return;
        }

        if (_restorePaused && !_player.IsPlaying)
        {
            ReleaseRestoredHold();
        }

        if (_streamOpening)
        {
            return;
        }

        if (_player.IsPlaying)
        {
            _player.SetPause(true);
        }
        else if (_ended && _streamUrl is not null)
        {
            TakePlayback();
            _ = ReplayStreamAsync();
        }
        else if (_ended && !string.IsNullOrWhiteSpace(_filePath))
        {
            _pausedForOther = false;
            PlayFile(_filePath);
        }
        else
        {
            if (_editing && (_player.Time < _trimStartMs || _player.Time >= _trimEndMs))
            {
                _player.Time = _trimStartMs;
            }

            var notStarted = !_hasValidDuration && _player.State is VLCState.NothingSpecial or VLCState.Stopped or VLCState.Error;
            if (notStarted && _pendingPageResolve)
            {
                _pausedForOther = false;
                _pendingPageResolve = false;
                _ = ResolvePageAsync(_streamStartMs);
            }
            else if (notStarted && _pendingStream is Uri pendingStream)
            {
                _pausedForOther = false;
                _ = OpenStreamAsync(pendingStream);
            }
            else if (notStarted && !string.IsNullOrWhiteSpace(_pendingPath))
            {
                _pausedForOther = false;
                PlayFile(_pendingPath);
            }
            else if (notStarted && _streamMedia is not null)
            {
                TakePlayback();
                _player.Play(_streamMedia);
            }
            else
            {
                TakePlayback();
                _player.Play();
            }
        }

        UpdatePlayIcon();
    }

    private void RewindButton_Click(object sender, RoutedEventArgs e) => Skip(-10_000);

    private void ForwardButton_Click(object sender, RoutedEventArgs e) => Skip(10_000);

    private void Skip(long deltaMs)
    {
        _captionHoldMs = null;
        if (_player is null || (_streamUrl is not null && (!_seekKnown || !_canSeek)))
        {
            return;
        }

        var length = _hasValidDuration && _durationMs > 0 ? _durationMs : Math.Max(_player.Length, 0);
        var min = _editing ? _trimStartMs : 0;
        var max = _editing ? _trimEndMs : length;
        var current = Math.Max(_player.Time, 0);
        var next = current + deltaMs;
        if (next < min)
        {
            next = min;
        }

        if (max > 0 && next > max)
        {
            next = max;
        }

        ReleaseRestoredHold();
        _player.Time = next;
        UpdateClockAndBar();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
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

        ApplyVolumeToPlayer();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (e.NewValue > 0)
        {
            _lastVolume = e.NewValue;
        }

        PlaybackVolume.Save(e.NewValue);
        ApplyVolumeToPlayer();
        if (!_volumeSync && Math.Abs(MiniVolume.Value - e.NewValue) > 0.1)
        {
            _volumeSync = true;
            MiniVolume.Value = e.NewValue;
            _volumeSync = false;
        }
    }

    private void ApplyVolumeToPlayer()
    {
        if (_player is null)
        {
            UpdateMuteIcon();
            return;
        }

        _player.Mute = Playback.VolumeSlider.Value <= 0;
        _player.Volume = (int)Math.Round(Playback.VolumeSlider.Value);
        UpdateMuteIcon();
    }

    private void UpdateMuteIcon()
    {
        Playback.MuteIcon.Glyph = Playback.VolumeSlider.Value <= 0 ? "\uE74F" : "\uE767";
    }

    private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ApplyRateToPlayer();

    private void ApplyRateToPlayer()
    {
        _player?.SetRate(GetSelectedRate());
    }

    private float GetSelectedRate()
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

    private void FullscreenButton_Click(object sender, RoutedEventArgs e) => _ = ToggleFullScreenAsync();

    private void VideoPlayerPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && HostWindow?.IsFullScreen == true)
        {
            _ = ExitFullScreenAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.F && !IsTextInput(e.OriginalSource as DependencyObject))
        {
            _ = ToggleFullScreenAsync();
            e.Handled = true;
        }
    }

    private static bool IsTextInput(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox or AutoSuggestBox or RichEditBox or PasswordBox or NumberBox or ComboBox)
            {
                return true;
            }
        }

        return false;
    }

    private Task ToggleFullScreenAsync()
    {
        if (_mini)
        {
            return Task.CompletedTask;
        }

        return HostWindow?.IsFullScreen == true ? ExitFullScreenAsync() : EnterFullScreenAsync();
    }

    private async Task EnterFullScreenAsync()
    {
        var window = HostWindow;
        if (window is null || _transitioning)
        {
            return;
        }

        _transitioning = true;
        ApplyFullScreenLayout();
        await window.SetFullScreenAsync(true);
        Playback.FullScreenIcon.Glyph = "\uE73F";
        _transitioning = false;
    }

    private async Task ExitFullScreenAsync()
    {
        if (_transitioning)
        {
            return;
        }

        _transitioning = true;
        if (HostWindow is not null)
        {
            await HostWindow.SetFullScreenAsync(false);
        }

        RestoreWindowedLayout();
        _transitioning = false;
    }

    private void ExitFullScreen()
    {
        HostWindow?.SetFullScreen(false);
        RestoreWindowedLayout();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_transitioning || !args.DidPresenterChange || sender.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            return;
        }

        RestoreWindowedLayout();
    }

    private void ApplyFullScreenLayout()
    {
        Playback.CloseSettings();
        HeaderPanel.Opacity = 1;
        HeaderPanel.Visibility = Visibility.Collapsed;
        RootGrid.Padding = new Thickness(0);
        RootGrid.RowSpacing = 0;
    }

    private void RestoreWindowedLayout()
    {
        Playback.CloseSettings();
        if (_mini)
        {
            return;
        }

        RootGrid.Padding = WindowedPadding;
        RootGrid.RowSpacing = 12;
        HeaderPanel.Opacity = 1;
        HeaderPanel.Visibility = Visibility.Visible;
        Playback.FullScreenIcon.Glyph = "\uE740";
    }

    private static MainWindow? HostWindow => App.MainAppWindow as MainWindow;

    private void OnSeekPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_hasValidDuration || (_streamUrl is not null && !_canSeek))
        {
            return;
        }

        _dragging = true;
        _captionHoldMs = null;
    }

    private void OnSeekReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (_player is null || !_hasValidDuration || _durationMs <= 0)
        {
            return;
        }

        var time = (long)(Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * _durationMs);
        if (_editing)
        {
            time = Math.Clamp(time, _trimStartMs, _trimEndMs);
        }

        ReleaseRestoredHold();
        _player.Time = time;
        UpdateClockAndBar();
    }

    private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider || !_dragging || !_hasValidDuration)
        {
            return;
        }

        Playback.PositionText.Text = FormatMs((long)(Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * _durationMs));
    }

    private void UpdateClockAndBar()
    {
        if (_heldMissing)
        {
            MiniPosition.Text = "Missing";
            MiniSeek.IsEnabled = false;
            UpdatePlayIcon();
            return;
        }

        if (_player is null)
        {
            return;
        }

        ApplyDuration(_player.Length, "vlc-timer");
        if (_streamPage is not null && _player.Time > 0)
        {
            _streamLastMs = _player.Time;
        }

        TryApplyStreamStart();
        TryResume();
        ApplyRestoredPause();
        if (_editing && _player.IsPlaying && _trimEndMs > _trimStartMs && _player.Time >= _trimEndMs - 80)
        {
            _player.Time = _trimStartMs;
        }
        else
        {
            KeepInsideSection();
        }

        if (!_dragging)
        {
            Playback.PositionText.Text = FormatMs(_player.Time);
        }

        MiniPosition.Text = FormatMs(_player.Time);
        var canSeek = _hasValidDuration && _durationMs > 0 && (_streamUrl is null || _canSeek);
        MiniSeek.IsEnabled = canSeek;
        if (_hasValidDuration && _durationMs > 0)
        {
            MiniDuration.Text = FormatMs(_durationMs);
        }

        if (canSeek && !_miniDragging)
        {
            _updatingMiniSeek = true;
            MiniSeek.Value = Math.Clamp(_player.Time / (double)_durationMs * MiniSeek.Maximum, 0, MiniSeek.Maximum);
            _updatingMiniSeek = false;
        }

        HighlightChapter();
        if (_dragging || !_hasValidDuration)
        {
            return;
        }

        Playback.DurationText.Text = FormatMs(_durationMs);
        _updatingSlider = true;
        Playback.SeekSlider.Value = Math.Clamp(_player.Time / (double)_durationMs * Playback.SeekSlider.Maximum, 0, Playback.SeekSlider.Maximum);
        _updatingSlider = false;
        UpdatePlayIcon();
        RememberPosition(force: false);
        ShowCaptions();
    }

    private void ShowCaptions()
    {
        if (_player is null)
        {
            return;
        }

        if (_captionHoldMs is long hold && Math.Abs(_player.Time - hold) > 1200)
        {
            Captions.SetTime(hold);
            return;
        }

        _captionHoldMs = null;
        Captions.SetTime(_player.Time);
    }

    private void HoldCaptions(long time)
    {
        var hold = Math.Max(0, time);
        _captionHoldMs = hold;
        Captions.SetTime(hold);
    }

    private void HookPlayer()
    {
        if (_player is null)
        {
            return;
        }

        _player.Playing += (_, _) =>
        {
            var epoch = _playbackEpoch;
            var chapters = _chapterEpoch;
            DispatcherQueue.TryEnqueue(() =>
            {
            if (_player is null || epoch != _playbackEpoch || chapters != _chapterEpoch)
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
                ReadPlayerChapters();
                return;
            }

            PlaybackFocus.Claim(this);
            Playback.UseSubtitles(_player);
            RefreshLocalAudio();
            UpdatePlayIcon();
            TryResume();
            ApplyRestoredPause();
            ReleaseSubtitlesAfterStart();
            if (_streamUrl is not null && _player is not null)
            {
                _streamOpening = false;
                _seekKnown = true;
                _canSeek = _player.IsSeekable;
                ApplyStreamControls();
            }

            ReadPlayerChapters();
            });
        };
        _player.ChapterChanged += (_, _) =>
        {
            var epoch = _playbackEpoch;
            var chapters = _chapterEpoch;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_player is null || epoch != _playbackEpoch || chapters != _chapterEpoch)
                {
                    return;
                }

                ReadPlayerChapters();
                HighlightChapter();
            });
        };
        _player.Paused += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            RememberPosition(force: true);
            UpdatePlayIcon();
        });
        _player.Stopped += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayIcon);
        _player.ESAdded += (_, _) => DispatcherQueue.TryEnqueue(RefreshLocalAudio);
        _player.EndReached += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_restorePaused)
            {
                _ended = true;
                UpdatePlayIcon();
                return;
            }

            if (RestartSection())
            {
                return;
            }

            if (!_fromQueue)
            {
                MarkPlaylistVideoWatched();
            }

            if (TryPlayQueued())
            {
                return;
            }

            if (PlayFollowingVideo())
            {
                return;
            }

            _ended = true;
            if (_streamPage is not null)
            {
                _streamEndedCleanly = true;
            }

            if (ResumeKey() is string endedKey)
            {
                PlaybackProgress.Save(endedKey, 0, 1);
            }

            UpdatePlayIcon();
        });
    }

    private void TryResume()
    {
        if (!_resumePending || _player is null || !_hasValidDuration || _editing)
        {
            return;
        }

        _resumePending = false;
        var explicitStart = _explicitStart;
        _explicitStart = false;
        if (explicitStart && _player.State != VLCState.Playing)
        {
            _resumePending = true;
            _explicitStart = true;
            return;
        }

        if (!explicitStart && _player.State is not (VLCState.Playing or VLCState.Paused))
        {
            _resumePending = true;
            return;
        }

        if (!explicitStart && (string.IsNullOrWhiteSpace(_filePath) || _durationMs <= _resumeMs + 10_000))
        {
            if (!string.IsNullOrWhiteSpace(_filePath))
            {
                PlaybackProgress.Save(_filePath, 0, 1);
            }

            return;
        }

        var time = explicitStart ? Math.Clamp(_resumeMs, 0, Math.Max(0, _durationMs - 400)) : _resumeMs;
        if (explicitStart)
        {
            _openAtMs = null;
        }

        _player.Time = time;
        if (explicitStart)
        {
            HoldCaptions(time);
        }
        else
        {
            Captions.SetTime(time);
        }

        AddedText.Text = _fromQueue
            ? QueueCaption()
            : _playlistId is not null && (!explicitStart || time == 0)
                ? PlaylistLabel()
                : explicitStart ? $"Opened at {FormatMs(time)}." : $"Resumed at {FormatMs(_resumeMs)}.";
    }

    private string? ResumeKey()
        => _streamPage is Uri page ? page.AbsoluteUri : string.IsNullOrWhiteSpace(_filePath) ? null : _filePath;

    private void RememberPosition(bool force)
    {
        var key = ResumeKey();
        if (_player is null || key is null || !_hasValidDuration || _editing)
        {
            return;
        }

        var time = Math.Max(_player.Time, 0);
        if (!force && (time < 5_000 || Math.Abs(time - _lastRememberedMs) < 5_000))
        {
            return;
        }

        // The file already has the saved moment. Skip the start of the file until the seek lands.
        if (_restorePaused
            && _restoreTargetMs is long held
            && held >= 1_500
            && Math.Abs(time - held) > 1_500)
        {
            return;
        }

        WatchingSession.NotePosition(time);
        PlaybackProgress.Save(key, time, _durationMs, TitleText.Text, _streamPage is null ? null : _streamThumbnail);
        _lastRememberedMs = time;
    }

    private void ResetDuration()
    {
        _hasValidDuration = false;
        _durationMs = 0;
        _repeatAMs = null;
        _repeatBMs = null;
        Playback.SetSectionMarks(null, null);
        _dragging = false;
        Playback.SeekSlider.IsEnabled = false;
        Playback.SeekSlider.Value = 0;
        EditTrimButton.IsEnabled = false;
        Playback.DurationText.Text = "--:--";
        Playback.PositionText.Text = "00:00";
        Playback.SetHoverDuration(0);
    }

    private void ApplyDuration(long durationMs, string source)
    {
        Debug.WriteLine($"{DateTime.Now:O} source={source} durationMs={durationMs} usable={IsUsableDuration(durationMs)} alreadyValid={_hasValidDuration}");

        if (_hasValidDuration || !IsUsableDuration(durationMs))
        {
            return;
        }

        _hasValidDuration = true;
        _durationMs = durationMs;
        Playback.SetHoverDuration(_streamUrl is null ? durationMs : 0);
        Playback.DurationText.Text = FormatMs(durationMs);
        if (_streamUrl is null)
        {
            Playback.SeekSlider.IsEnabled = true;
            EditTrimButton.IsEnabled = true;
        }
        else
        {
            EditTrimButton.IsEnabled = false;
            ApplyStreamControls();
        }
        if (_editing)
        {
            _trimEndMs = durationMs;
            UpdateTrimSummary();
        }
    }

    private static bool IsUsableDuration(long durationMs)
        => durationMs > 0 && durationMs <= MaxRealisticMs && durationMs < int.MaxValue;

    private void UpdatePlayIcon()
    {
        var glyph = _player?.IsPlaying == true ? "\uE769" : "\uE768";
        Playback.PlayIcon.Glyph = glyph;
        MiniPlayIcon.Glyph = glyph;
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Restore original?",
            Content = "This replaces the current video with the untouched copy.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var path = _filePath;
        try
        {
            if (_editing)
            {
                LeaveTrimMode();
            }

            _player?.SetPause(true);
            ReleasePlayer();
            App.MediaLibrary.RestoreOriginal(path);
            ResetDuration();
            _pendingPath = path;
            CreatePlayer();
            PlayFile(path);
            AddedText.Text = "Original restored.";
        }
        catch (Exception ex)
        {
            if (_player is null)
            {
                CreatePlayer();
                PlayFile(path);
            }

            AddedText.Text = ex.Message;
        }
    }

    private async void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        var key = ResumeKey();
        if (_player is null || key is null)
        {
            return;
        }

        var time = Math.Max(0, _player.Time);
        var note = await AskBookmarkNoteAsync(string.Empty);
        if (note is null)
        {
            return;
        }

        if (!PlaybackBookmarks.Add(key, time, note))
        {
            AddedText.Text = $"Already bookmarked near {FormatMs(time)}.";
            return;
        }

        AddedText.Text = string.IsNullOrWhiteSpace(note)
            ? $"Bookmarked {FormatMs(time)}."
            : $"Bookmarked {FormatMs(time)}: {note}";
        ShowBookmarks();
    }

    private async Task<string?> AskBookmarkNoteAsync(string current)
    {
        var box = new TextBox
        {
            Text = current,
            PlaceholderText = "Why this moment? Optional",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 120
        };
        var dialog = new ContentDialog
        {
            Title = "Bookmark note",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    private void ShowBookmarks()
    {
        var keepOpen = BookmarkPanel.Visibility == Visibility.Visible;
        BookmarkList.Children.Clear();
        var key = ResumeKey();
        if (key is null)
        {
            BookmarkHost.Visibility = Visibility.Collapsed;
            BookmarkPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var marks = PlaybackBookmarks.Load(key);
        if (marks.Count == 0)
        {
            BookmarkHost.Visibility = Visibility.Collapsed;
            BookmarkPanel.Visibility = Visibility.Collapsed;
            return;
        }

        BookmarkHost.Visibility = Visibility.Visible;
        BookmarkPanel.Visibility = keepOpen ? Visibility.Visible : Visibility.Collapsed;
        foreach (var mark in marks)
        {
            BookmarkList.Children.Add(CreateBookmarkRow(mark.TimeMs, mark.Note));
        }
    }

    private void BookmarkToggle_Click(object sender, RoutedEventArgs e)
    {
        BookmarkPanel.Visibility = BookmarkPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private UIElement CreateBookmarkRow(long time, string note)
    {
        var jump = new Button
        {
            Content = FormatMs(time),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 72
        };
        jump.Click += (_, _) => JumpTo(time);
        var edit = new Button { Content = "Edit", Padding = new Thickness(8, 4, 8, 4) };
        edit.Click += async (_, _) =>
        {
            var updated = await AskBookmarkNoteAsync(note);
            if (updated is null || ResumeKey() is not string noteKey)
            {
                return;
            }

            PlaybackBookmarks.UpdateNote(noteKey, time, updated);
            ShowBookmarks();
        };
        var remove = new Button { Content = "Remove", Padding = new Thickness(8, 4, 8, 4) };
        remove.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Remove bookmark?",
                Content = string.IsNullOrWhiteSpace(note)
                    ? $"Remove the bookmark at {FormatMs(time)}?"
                    : $"Remove the bookmark at {FormatMs(time)} and its note?",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || ResumeKey() is not string removeKey)
            {
                return;
            }

            PlaybackBookmarks.Remove(removeKey, time);
            ShowBookmarks();
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Children = { edit, remove }
        };
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(actions, 1);
        row.Children.Add(jump);
        row.Children.Add(actions);
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(row);
        if (!string.IsNullOrWhiteSpace(note))
        {
            const int previewLength = 80;
            var shortened = note.Length > previewLength;
            var preview = shortened ? note[..previewLength].TrimEnd() + "…" : note;
            var expanded = false;
            var text = new TextBlock
            {
                Text = preview,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextWrapping = TextWrapping.Wrap
            };
            var noteHost = new Border { Child = text, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 255, 255, 255)) };
            if (shortened)
            {
                noteHost.Tapped += (_, _) =>
                {
                    expanded = !expanded;
                    text.Text = expanded ? note : preview;
                };
            }

            body.Children.Add(noteHost);
        }

        return new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255)),
            Child = body
        };
    }

    private void JumpTo(long timeMs)
    {
        if (_player is null)
        {
            return;
        }

        if (_editing)
        {
            timeMs = Math.Clamp(timeMs, _trimStartMs, Math.Max(_trimStartMs, _trimEndMs));
        }

        ReleaseRestoredHold();
        _player.Time = timeMs;
        UpdateClockAndBar();
    }

    private async void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null || string.IsNullOrWhiteSpace(_filePath) || _grabbingFrame)
        {
            return;
        }

        _grabbingFrame = true;
        Playback.SnapshotButton.IsEnabled = false;
        var path = _filePath;
        var time = TimeSpan.FromMilliseconds(Math.Max(0, _player.Time));
        var name = $"{Path.GetFileNameWithoutExtension(path)} {FormatMs((long)time.TotalMilliseconds)}";
        try
        {
            RememberPosition(force: true);
            var png = await VideoFrameGrab.GrabPngAsync(path, time);
            using var buffer = new MemoryStream(png);
            using var image = System.Drawing.Image.FromStream(buffer, useEmbeddedColorManagement: false, validateImageData: false);
            var shot = new PendingScreenshot(png, image.Width, image.Height, name);
            if (!PrepareToLeave(typeof(CapturePage), shot, back: false))
            {
                return;
            }

            if (HostWindow?.LeavePlayerFor(typeof(CapturePage), shot, "capture") != true)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            AddedText.Text = ex.Message;
        }
        finally
        {
            _grabbingFrame = false;
            Playback.SnapshotButton.IsEnabled = true;
        }
    }

    private void EditVideo_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        var item = App.MediaLibrary.GetById(_filePath);
        if (item is null)
        {
            return;
        }

        if (!PrepareToLeave(typeof(VideoEditorPage), item, back: false))
        {
            return;
        }

        HostWindow?.LeavePlayerFor(typeof(VideoEditorPage), item, null);
    }

    private bool HasSectionLoop =>
        !_editing && _repeatAMs is long start && _repeatBMs is long end && Math.Abs(end - start) >= 400;

    private void SetRepeatPoint(bool a)
    {
        if (_player is null || !_hasValidDuration)
        {
            return;
        }

        var time = Math.Clamp(_player.Time, 0, _durationMs);
        if (a)
        {
            _repeatAMs = time;
        }
        else
        {
            _repeatBMs = time;
        }

        ShowSectionMarks();
    }

    private void ClearSectionRepeat()
    {
        _repeatAMs = null;
        _repeatBMs = null;
        Playback.SetSectionMarks(null, null);
        Playback.SetSectionPrompt("Repeat a section", "Mark where the repeat starts", "Mark where the repeat ends");
    }

    private void ShowSectionMarks()
    {
        if (!_hasValidDuration || _durationMs <= 0)
        {
            return;
        }

        Playback.SetSectionMarks(
            _repeatAMs is long start ? start / (double)_durationMs : null,
            _repeatBMs is long end ? end / (double)_durationMs : null);
        Playback.SetSectionPrompt(SectionHint(), SectionTip(true), SectionTip(false));
    }

    private string SectionHint()
    {
        if (_repeatAMs is long start && _repeatBMs is long end)
        {
            var from = FormatMs(Math.Min(start, end));
            var to = FormatMs(Math.Max(start, end));
            return Math.Abs(end - start) >= 400
                ? $"Repeating {from}–{to}"
                : "Set the points a little further apart.";
        }

        if (_repeatAMs is long onlyStart)
        {
            return $"Start is {FormatMs(onlyStart)}. Set the end.";
        }

        if (_repeatBMs is long onlyEnd)
        {
            return $"End is {FormatMs(onlyEnd)}. Set the start.";
        }

        return "Repeat a section";
    }

    private string SectionTip(bool start)
    {
        var time = start ? _repeatAMs : _repeatBMs;
        if (time is not long ms)
        {
            return start ? "Mark where the repeat starts" : "Mark where the repeat ends";
        }

        var name = start ? "Start" : "End";
        return $"{name} at {FormatMs(ms)}. Click to move it.";
    }

    private void KeepInsideSection()
    {
        if (_player is null || _dragging || !_player.IsPlaying || !HasSectionLoop)
        {
            return;
        }

        var start = Math.Min(_repeatAMs!.Value, _repeatBMs!.Value);
        var end = Math.Max(_repeatAMs.Value, _repeatBMs.Value);
        if (_player.Time < start || _player.Time >= end - 80)
        {
            _player.Time = start;
        }
    }

    private bool RestartSection()
    {
        if (_player is null || !HasSectionLoop)
        {
            return false;
        }

        _ended = false;
        _player.Time = Math.Min(_repeatAMs!.Value, _repeatBMs!.Value);
        TakePlayback();
        _player.Play();
        UpdatePlayIcon();
        return true;
    }

    private void EditTrim_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasValidDuration || _durationMs <= 0 || string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        _editing = true;
        _trimStartMs = 0;
        _trimEndMs = _durationMs;
        WatchActions.Visibility = Visibility.Collapsed;
        TrimActions.Visibility = Visibility.Visible;
        Playback.BeginTrim();
        UpdateTrimSummary();
        AddedText.Text = "Drag the white ends. Playback stays inside the selection.";
    }

    private void CancelTrim_Click(object sender, RoutedEventArgs e) => LeaveTrimMode();

    private async void SaveTrim_Click(object sender, RoutedEventArgs e)
    {
        if (_savingTrim || !_editing || string.IsNullOrWhiteSpace(_filePath) || _trimEndMs - _trimStartMs < 400)
        {
            TrimSummary.Text = "Keep at least half a second.";
            return;
        }

        _savingTrim = true;
        TrimProgress.IsActive = true;
        TrimSummary.Text = "Saving the trimmed video…";
        var path = _filePath;
        var temp = path + ".trimming.mp4";
        try
        {
            _player?.SetPause(true);
            ReleasePlayer();
            await VideoTrimmer.TrimAsync(path, temp, TimeSpan.FromMilliseconds(_trimStartMs), TimeSpan.FromMilliseconds(_trimEndMs));
            App.MediaLibrary.PreserveOriginal(path);
            File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            LeaveTrimMode();
            ResetDuration();
            _pendingPath = path;
            CreatePlayer();
            PlayFile(path);
            AddedText.Text = "Trimmed. The original is kept with your other originals.";
            RestoreButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // The failed export can stay until the next trim.
                }
            }

            if (_player is null)
            {
                CreatePlayer();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    PlayFile(path);
                }
            }

            TrimSummary.Text = ex.Message;
        }
        finally
        {
            _savingTrim = false;
            TrimProgress.IsActive = false;
        }
    }

    private void LeaveTrimMode()
    {
        _editing = false;
        Playback.EndTrim();
        TrimActions.Visibility = Visibility.Collapsed;
        WatchActions.Visibility = Visibility.Visible;
    }

    private void ApplyTrimFromBar()
    {
        if (!_editing || _durationMs <= 0)
        {
            return;
        }

        _trimStartMs = (long)(Playback.TrimStart * _durationMs);
        _trimEndMs = (long)(Playback.TrimEnd * _durationMs);
        if (_player is not null && (_player.Time < _trimStartMs || _player.Time > _trimEndMs))
        {
            _player.Time = _trimStartMs;
        }

        UpdateTrimSummary();
        UpdateClockAndBar();
    }

    private void SeekToFraction(double fraction)
    {
        if (_player is null || _durationMs <= 0)
        {
            return;
        }

        var time = (long)(fraction * _durationMs);
        _player.Time = _editing ? Math.Clamp(time, _trimStartMs, _trimEndMs) : time;
        UpdateClockAndBar();
    }

    private void UpdateTrimSummary()
    {
        var keep = Math.Max(0, _trimEndMs - _trimStartMs);
        TrimSummary.Text = $"Keep {FormatMs(keep)}  ·  {FormatMs(_trimStartMs)} to {FormatMs(_trimEndMs)}";
    }

    private void ReleasePlayer()
    {
        if (_player is not null)
        {
            _player.LengthChanged -= Player_LengthChanged;
            _player.SeekableChanged -= Player_SeekableChanged;
            _player.EncounteredError -= Player_EncounteredError;
            _player.Stop();
            VideoView.MediaPlayer = null;
            _player.Dispose();
            _player = null;
        }

        ReleaseStreamMedia();
        DeletePlaylistFile(_hlsPath);
        _hlsPath = null;
        DeletePlaylistFile(_hlsPending);
        _hlsPending = null;
        _libVlc?.Dispose();
        _libVlc = null;
    }

    private void ReleaseStreamMedia()
    {
        _streamMedia?.Dispose();
        _streamMedia = null;
    }

    private void CreatePlayer()
    {
        if (_swapChainOptions is null)
        {
            return;
        }

        _libVlc = new LibVLC(enableDebugLogs: false, PlaybackAudio.Options(_swapChainOptions.Concat(new[] { "--no-sub-autodetect-file" }).ToArray()));
        _player = new VlcMediaPlayer(_libVlc);
        _player.LengthChanged += Player_LengthChanged;
        _player.SeekableChanged += Player_SeekableChanged;
        _player.EncounteredError += Player_EncounteredError;
        HookPlayer();
        VideoView.MediaPlayer = _player;
        ApplyVolumeToPlayer();
        ApplyRateToPlayer();
    }

    private void ShowStream(StreamOpenRequest stream, bool keepPlaylist = false)
    {
        _heldMissing = false;
        if (!keepPlaylist)
        {
            LeavePlaylist();
        }

        var playlistId = keepPlaylist ? _playlistId : null;
        var playlistIndex = keepPlaylist ? _playlistIndex : -1;
        var sameChapterPage = _streamPage is not null
            && stream.Page is not null
            && SameAddress(_streamPage, stream.Page);
        RememberStreamChapters(stream.Chapters, sameChapterPage);
        _subtitleWork?.Cancel();
        CancelCaptionLoad();
        _deferSubtitleFetch = stream.Page is not null;
        _deferredSubtitle = null;
        _subtitleReleasePending = false;
        _captionsEnriched = false;
        _captionChosen = false;
        _captionChoice = null;
        ClearStreamChoices();
        _openAtMs = null;
        _savedFocus = stream.Focus;
        _playlistId = playlistId;
        _playlistIndex = playlistIndex;
        _filePath = null;
        _streamUrl = stream.Url;
        _streamPage = stream.Page;
        _streamAudio = stream.Audio;
        _streamSubtitles = stream.Subtitles;
        _streamSubtitle = null;
        _streamQuality = stream.Quality;
        _streamThumbnail = StreamThumbnail.Choose(stream.Page?.AbsoluteUri, stream.Thumbnail);
        _streamReferrer = stream.Referrer;
        _streamLastMs = 0;
        _streamEndedCleanly = false;
        _streamResolving = false;
        _streamErrorRetries = 0;
        long? start = _restorePaused
            ? Math.Max(0, stream.StartMs ?? 0)
            : _savedFocus is not null
                ? _savedFocus.TimeMs ?? stream.StartMs
                : stream.StartMs is > 0 ? stream.StartMs : null;
        if (start is null && _streamPage is Uri page)
        {
            var saved = PlaybackProgress.Load(page.AbsoluteUri);
            if (saved >= 5_000)
            {
                start = saved;
            }
        }

        _streamStartMs = start;
        var heldAddress = _streamPage ?? _streamUrl;
        if (_streamPage is not null)
        {
            WatchingSession.RememberPage(stream.DisplayName, _streamPage.AbsoluteUri, _streamThumbnail, start ?? 0);
        }
        else if (heldAddress is not null)
        {
            WatchingSession.RememberStream(stream.DisplayName, heldAddress.AbsoluteUri, _streamThumbnail, start ?? 0);
        }
        _streamFailed = false;
        _canSeek = false;
        _seekKnown = false;
        _explicitStart = false;
        _resumePending = false;
        TitleText.Text = stream.DisplayName;
        AddedText.Text = _fromQueue ? QueueCaption() : "Streaming";
        EditButton.Visibility = Visibility.Collapsed;
        WordsButton.Visibility = Visibility.Visible;
        RestoreButton.Visibility = Visibility.Collapsed;
        EditTrimButton.Visibility = Visibility.Collapsed;
        PlaylistButton.Visibility = _playlistId is null ? Visibility.Collapsed : Visibility.Visible;
        AddPlaylistButton.Visibility = stream.Page is null ? Visibility.Collapsed : Visibility.Visible;
        SaveCopyButton.Visibility = _streamPage is null ? Visibility.Collapsed : Visibility.Visible;
        SaveCopyButton.IsEnabled = _streamPage is not null && _streamQuality is not null;
        SaveCopyLabel.Text = "Save a copy";
        Playback.SnapshotButton.Visibility = Visibility.Collapsed;
        Playback.BookmarkButton.Visibility = _streamPage is null ? Visibility.Collapsed : Visibility.Visible;
        Playback.BackButton.IsEnabled = false;
        Playback.ForwardButton.IsEnabled = false;
        Playback.UseSectionRepeat(false);
        Playback.SetHoverSource(null);
        Playback.ClearHoverCaptions();
        Captions.Load(null);
        Captions.SetStreamSource(StreamWordKey(), _streamPage is not null, stream.DisplayName);
        WordsPanel.CurrentVideoPath = StreamWordKey();
        SubtitleSearchPanel.BeginVideo();
        if (_savedFocus is { } focus)
        {
            Captions.ShowSavedWord(focus.English, focus.Sentence, focus.TimeMs);
            WordsPanel.Show(focus);
        }
        else
        {
            Captions.ClearSavedWord();
            if (WordsPanel.IsOpen)
            {
                WordsPanel.Refresh();
            }
        }
        _restoreAudioLanguage = string.IsNullOrWhiteSpace(_savedFocus?.AudioLanguage) ? null : _savedFocus.AudioLanguage.Trim();
        if (_restoreAudioLanguage is not null)
        {
            _hlsAudioKey = _restoreAudioLanguage;
        }

        ShowResolvedCaptions();
        if (_streamSubtitles is not { Count: > 0 } && _streamPage is null)
        {
            SubtitleSearchPanel.ShowCues([]);
        }

        if (VideoHost.Child is null)
        {
            VideoHost.Child = VideoView;
        }

        ResetDuration();
        _pendingPath = null;
        var unresolvedPage = stream.Page is Uri savedPage && SameAddress(stream.Url, savedPage);
        _pendingPageResolve = unresolvedPage && _player is null;
        _pendingStream = unresolvedPage ? null : stream.Url;
        if (_player is not null)
        {
            if (unresolvedPage)
            {
                _ = ResolvePageAsync(_streamStartMs);
            }
            else
            {
                _ = OpenStreamAsync(stream.Url);
            }
        }

        if (_streamPage is not null)
        {
            ShowBookmarks();
        }

        if (_playlistId is not null)
        {
            ShowPlaylist();
        }
        else
        {
            PlaylistButton.Visibility = Visibility.Collapsed;
            PlaylistPanel.Hide();
            _playlistBeforeMini = false;
        }

        StartWatching();
    }

    private static bool SameAddress(Uri left, Uri right)
        => string.Equals(left.AbsoluteUri, right.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

    private void OpenSavedPage(PlaylistEntry entry, bool keepPlaylist)
    {
        if (!StreamLink.TryNormalize(entry.Location, out var page))
        {
            TitleText.Text = string.IsNullOrWhiteSpace(entry.Title) ? "Playlist" : entry.Title;
            AddedText.Text = "That link cannot be opened.";
            ShowPlaylist();
            return;
        }

        var title = string.IsNullOrWhiteSpace(entry.Title) ? StreamLink.DisplayName(page) : entry.Title;
        ShowStream(new StreamOpenRequest(page, title, Page: page), keepPlaylist);
    }

    private async Task ReplayStreamAsync()
    {
        if (_streamUrl is null || _streamOpening)
        {
            return;
        }

        if (_streamPage is not null)
        {
            long? resume = _streamEndedCleanly || _streamLastMs <= 0 ? null : _streamLastMs;
            _streamEndedCleanly = false;
            if (_streamErrorRetries != 0)
            {
                _streamErrorRetries = StreamRetry.Clear();
            }

            await ResolvePageAsync(resume);
            return;
        }

        _streamStartMs = null;
        var token = BeginStreamWork();
        var generation = _streamGeneration;
        AddedText.Text = "Checking the link…";
        _streamFailed = false;
        try
        {
            var result = await StreamLink.CheckAsync(_streamUrl, token);
            if (generation != _streamGeneration || token.IsCancellationRequested)
            {
                return;
            }

            if (result.Status != StreamCheckStatus.Media)
            {
                FailStream(result.Message);
                return;
            }

            _streamUrl = result.Url;
            await OpenStreamCoreAsync(result.Url, generation, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task OpenStreamAsync(Uri url)
    {
        var token = BeginStreamWork();
        return OpenStreamCoreAsync(url, _streamGeneration, token);
    }

    private async Task OpenStreamCoreAsync(Uri url, int generation, CancellationToken cancellationToken)
    {
        var libVlc = _libVlc;
        if (libVlc is null || _player is null)
        {
            _pendingStream = url;
            return;
        }

        _pendingStream = null;
        _streamOpening = true;
        _streamFailed = false;
        _canSeek = false;
        _seekKnown = false;
        _ended = false;
        AddedText.Text = "Opening…";
        ResetDuration();
        Playback.BackButton.IsEnabled = false;
        Playback.ForwardButton.IsEnabled = false;
        Playback.UseSectionRepeat(false);
        Media? media = null;
        var handedToPlayer = false;
        try
        {
            media = await CreateStreamMediaAsync(libVlc, url, generation, cancellationToken);
            if (media is null || generation != _streamGeneration || cancellationToken.IsCancellationRequested || _player is null)
            {
                return;
            }

            var previousPlaylist = _hlsPath;
            var nextPlaylist = _hlsPending;
            _hlsPending = null;
            ReleaseStreamMedia();
            _hlsPath = nextPlaylist;
            if (!string.IsNullOrEmpty(previousPlaylist)
                && !string.Equals(previousPlaylist, _hlsPath, StringComparison.OrdinalIgnoreCase))
            {
                DeletePlaylistFile(previousPlaylist);
            }

            _streamMedia = media;
            handedToPlayer = true;
            media = null;
            _playbackEpoch++;
            if (_streamErrorRetries != 0)
            {
                _streamErrorRetries = StreamRetry.Clear();
            }

            if (_deferSubtitleFetch || !_captionsEnriched)
            {
                _subtitleReleasePending = true;
                _enrichGeneration = generation;
                _enrichToken = cancellationToken;
            }

            if (_pausedForOther)
            {
                return;
            }

            TakePlayback();
            if (!_player.Play(_streamMedia))
            {
                FailStream(StreamLink.OpenFailedMessage);
                return;
            }

            ApplyRateToPlayer();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (generation == _streamGeneration)
            {
                FailStream(StreamLink.OpenFailedMessage);
                if (handedToPlayer)
                {
                    ReleaseStreamMedia();
                }
            }
        }
        finally
        {
            if (!handedToPlayer)
            {
                DeletePlaylistFile(_hlsPending);
                _hlsPending = null;
            }

            try
            {
                media?.Dispose();
            }
            catch (Exception)
            {
                // The player may already have released LibVLC while this open was cancelled.
            }

            if (generation == _streamGeneration)
            {
                _streamOpening = false;
            }
        }
    }

    private CancellationToken BeginStreamWork()
    {
        _streamWork?.Cancel();
        _streamWork = new CancellationTokenSource();
        _streamGeneration++;
        return _streamWork.Token;
    }

    private void Player_SeekableChanged(object? sender, MediaPlayerSeekableChangedEventArgs e)
    {
        var seekable = e.Seekable != 0;
        DispatcherQueue.TryEnqueue(() =>
        {
            _seekKnown = true;
            _canSeek = seekable;
            ApplyStreamControls();
        });
    }

    private void Player_EncounteredError(object? sender, EventArgs e)
    {
        var epoch = _playbackEpoch;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (epoch != _playbackEpoch || _streamUrl is null || _streamFailed || _streamResolving || _streamOpening)
            {
                return;
            }

            if (_streamPage is null || !StreamRetry.TrySpend(ref _streamErrorRetries))
            {
                FailStream(StreamLink.OpenFailedMessage);
                return;
            }
            long? position = _streamStartMs;
            if (position is null or <= 0 && _player is { Time: > 0 })
            {
                position = _player.Time;
            }
            else if (position is null or <= 0 && _streamLastMs > 0)
            {
                position = _streamLastMs;
            }

            _ = ResolvePageAsync(position is > 0 ? position : null);
        });
    }

    private void ApplyStreamControls()
    {
        if (_streamUrl is null)
        {
            return;
        }

        var allow = _seekKnown && _canSeek && _hasValidDuration;
        Playback.SeekSlider.IsEnabled = allow;
        Playback.BackButton.IsEnabled = allow;
        Playback.ForwardButton.IsEnabled = allow;
        if (_seekKnown)
        {
            Playback.UseSectionRepeat(allow);
        }

        if (allow && !_streamFailed)
        {
            TryApplyStreamStart();
            ApplyRestoredPause();
        }

        if (_streamFailed || !_seekKnown || AddedText.Text == TranslationWaitMessage)
        {
            return;
        }

        AddedText.Text = _fromQueue ? QueueCaption() : StreamStatusText();
    }

    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private async Task<Media?> CreateStreamMediaAsync(LibVLC libVlc, Uri url, int generation, CancellationToken cancellationToken)
    {
        if (IsPlaylist(url))
        {
            var referrer = _streamReferrer;
            if (referrer is not null && referrer.Scheme != Uri.UriSchemeHttp && referrer.Scheme != Uri.UriSchemeHttps)
            {
                referrer = null;
            }

            var prepared = await HlsMaster.TryPrepareAsync(
                url,
                _hlsSource == url ? _hlsBody : null,
                _hlsAudioKey,
                _hlsHeight,
                referrer,
                _streamPage is not null ? BrowserUserAgent : null,
                cancellationToken,
                _streamPage is not null,
                _streamPage is not null && string.IsNullOrWhiteSpace(_hlsAudioKey)
                    ? StreamLanguageSettings.Load().AudioLanguage
                    : null);
            if (generation != _streamGeneration || cancellationToken.IsCancellationRequested)
            {
                DeletePlaylistFile(prepared?.PlaylistPath);
                return null;
            }

            if (prepared is not null)
            {
                _hlsSource = url;
                _hlsBody = prepared.Body;
                _hlsMaster = prepared.Master;
                _hlsAudioKey = prepared.AudioKey;
                _restoreAudioLanguage = prepared.AudioKey;
                _hlsHeight = prepared.Height;
                RememberStreamChoice();
                ShowHlsChoices(prepared.Master);
                if (!string.IsNullOrEmpty(prepared.PlaylistPath))
                {
                    _hlsPending = prepared.PlaylistPath;
                    return CreatePlayback(libVlc, prepared.PlaylistPath, FromType.FromPath, slave: false);
                }
            }
        }
        else if (_hlsMaster is not null)
        {
            ClearStreamChoices();
        }

        return CreatePlayback(libVlc, url.AbsoluteUri, FromType.FromLocation, slave: true);
    }

    private Media CreatePlayback(LibVLC libVlc, string location, FromType type, bool slave)
    {
        var media = new Media(libVlc, location, type);
        if (_streamPage is not null)
        {
            media.AddOption(":http-user-agent=" + BrowserUserAgent);
        }

        var referrer = _streamReferrer;
        if (referrer is not null
            && (referrer.Scheme == Uri.UriSchemeHttp || referrer.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(referrer.Host))
        {
            media.AddOption(":http-referrer=" + referrer.AbsoluteUri);
        }

        if (slave
            && _streamPage is not null
            && _streamAudio is Uri audio
            && (audio.Scheme == Uri.UriSchemeHttp || audio.Scheme == Uri.UriSchemeHttps))
        {
            media.AddSlave(MediaSlaveType.Audio, 1, audio.AbsoluteUri);
        }

        return media;
    }

    private static bool IsPlaylist(Uri url)
    {
        var path = url.AbsolutePath;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowHlsChoices(HlsMaster master)
    {
        _localAudioIds = null;
        if (master.Qualities.Count >= 2)
        {
            var labels = new List<string> { "Auto" };
            foreach (var option in master.Qualities)
            {
                labels.Add(option.Label);
            }

            var selected = 0;
            if (_hlsHeight > 0)
            {
                for (var i = 0; i < master.Qualities.Count; i++)
                {
                    if (master.Qualities[i].Height == _hlsHeight)
                    {
                        selected = i + 1;
                        break;
                    }
                }
            }

            Playback.OfferQualityChoices(labels, selected);
        }
        else
        {
            Playback.ClearQualityChoices();
        }

        if (master.Audios.Count >= 2)
        {
            var labels = new List<string>(master.Audios.Count);
            var selected = 0;
            for (var i = 0; i < master.Audios.Count; i++)
            {
                labels.Add(master.Audios[i].Label);
                if (master.Audios[i].Key.Equals(_hlsAudioKey, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                }
            }

            Playback.OfferAudioChoices(labels, selected);
        }
        else
        {
            Playback.ClearAudioChoices();
        }
    }

    private void ChooseStreamQuality(int index)
    {
        if (_hlsMaster is null || _streamUrl is null)
        {
            return;
        }

        var height = 0;
        if (index > 0)
        {
            var option = index - 1;
            if (option >= _hlsMaster.Qualities.Count)
            {
                return;
            }

            height = _hlsMaster.Qualities[option].Height;
        }

        if (height == _hlsHeight)
        {
            return;
        }

        _hlsHeight = height;
        ReopenStreamAtPlayedTime();
    }

    private void ChooseAudio(int index)
    {
        if (_hlsMaster is not null)
        {
            if (_streamUrl is null || index < 0 || index >= _hlsMaster.Audios.Count)
            {
                return;
            }

            var key = _hlsMaster.Audios[index].Key;
            if (string.Equals(key, _hlsAudioKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _hlsAudioKey = key;
            _restoreAudioLanguage = key;
            RememberStreamChoice();
            ReopenStreamAtPlayedTime();
            return;
        }

        if (_localAudioIds is not null && _player is not null && index >= 0 && index < _localAudioIds.Length)
        {
            _player.SetAudioTrack(_localAudioIds[index]);
            _restoreAudioLanguage = LocalAudioName(_localAudioIds[index]);
            RememberStreamChoice();
        }
    }

    private void ReopenStreamAtPlayedTime()
    {
        if (_streamUrl is null)
        {
            return;
        }

        var position = _player is { Time: > 0 } ? _player.Time : _streamLastMs;
        if (position > 0)
        {
            _streamStartMs = position;
        }

        _ = OpenStreamAsync(_streamUrl);
    }

    private void RefreshLocalAudio()
    {
        if (_hlsMaster is not null || _player is null)
        {
            return;
        }

        TrackDescription[] descriptions;
        try
        {
            descriptions = _player.AudioTrackDescription;
            if (descriptions is null)
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        var tracks = descriptions.Where(track => track.Id >= 0).ToList();
        if (tracks.Count < 2)
        {
            if (_localAudioIds is not null)
            {
                _localAudioIds = null;
                Playback.ClearAudioChoices();
            }

            return;
        }

        var ids = tracks.Select(track => track.Id).ToArray();
        var selected = Math.Max(0, Array.IndexOf(ids, _player.AudioTrack));
        if (!string.IsNullOrWhiteSpace(_restoreAudioLanguage))
        {
            for (var i = 0; i < tracks.Count; i++)
            {
                if (string.Equals(tracks[i].Name, _restoreAudioLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                    if (_player.AudioTrack != tracks[i].Id)
                    {
                        _player.SetAudioTrack(tracks[i].Id);
                    }

                    break;
                }
            }
        }

        if (_localAudioIds is not null && SameTrackIds(_localAudioIds, ids) && Array.IndexOf(ids, _player.AudioTrack) == selected)
        {
            return;
        }

        _localAudioIds = ids;
        Playback.OfferAudioChoices(
            tracks.Select(track => string.IsNullOrWhiteSpace(track.Name) ? "Audio" : track.Name).ToList(),
            selected);
        RememberStreamChoice();
    }

    private static bool SameTrackIds(int[] left, int[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private void ClearStreamChoices()
    {
        _hlsBody = null;
        _hlsSource = null;
        _hlsMaster = null;
        _hlsAudioKey = null;
        _hlsHeight = 0;
        _localAudioIds = null;
        DeletePlaylistFile(_hlsPath);
        _hlsPath = null;
        DeletePlaylistFile(_hlsPending);
        _hlsPending = null;
        Playback.ClearQualityChoices();
        Playback.ClearAudioChoices();
    }

    private static void DeletePlaylistFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task ResolvePageAsync(long? startMs)
    {
        if (_streamPage is not Uri page || _streamResolving)
        {
            return;
        }

        var token = BeginStreamWork();
        var generation = _streamGeneration;
        _streamResolving = true;
        _streamOpening = true;
        _streamFailed = false;
        _ended = false;
        AddedText.Text = "Opening…";
        try
        {
            var progress = new Progress<string>(text =>
            {
                if (generation != _streamGeneration || string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation == _streamGeneration)
                    {
                        AddedText.Text = text;
                    }
                });
            });
            var choice = await Task.Run(
                () => YoutubeDownloader.ResolvePlaybackAsync(page.AbsoluteUri, progress, token),
                token);
            if (generation != _streamGeneration || token.IsCancellationRequested)
            {
                return;
            }

            var captionsWereOffered = _streamSubtitles is { Count: > 0 };
            _streamUrl = choice.Media;
            _streamAudio = choice.Audio;
            _streamReferrer = page;
            _streamThumbnail = StreamThumbnail.Choose(page.AbsoluteUri, choice.Thumbnail);
            RememberPlaylistThumbnail();
            UseLookupChapters(choice.Chapters);
            _streamSubtitles = choice.Subtitles;
            if (choice.Quality is not null)
            {
                var qualityWasMissing = _streamQuality is null;
                _streamQuality = choice.Quality;
                if (qualityWasMissing)
                {
                    SaveCopyButton.IsEnabled = true;
                }
            }

            // ShowStream offers captions only when the request already has them.
            // A playlist open learns the list from this lookup.
            if (!captionsWereOffered)
            {
                ShowResolvedCaptions();
            }

            _streamStartMs = startMs is > 0 ? startMs : null;
            await OpenStreamCoreAsync(choice.Media, generation, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (generation == _streamGeneration)
            {
                FailStream(ex is InvalidOperationException ? ex.Message : StreamLink.OpenFailedMessage);
            }
        }
        finally
        {
            if (generation == _streamGeneration)
            {
                _streamResolving = false;
            }
        }
    }

    private void ChoosePageSubtitle(int index)
    {
        if (_streamPage is null)
        {
            return;
        }

        if (!_applyingCaptionOffer)
        {
            _captionChosen = true;
            _captionChoice = index >= 0 && _streamSubtitles is not null && index < _streamSubtitles.Count
                ? _streamSubtitles[index].Language
                : null;
        }

        if (_deferSubtitleFetch)
        {
            _deferredSubtitle = index;
            if (index < 0)
            {
                _streamSubtitle = null;
                Captions.LoadCues([]);
                SubtitleSearchPanel.ShowOff();
            }
            else
            {
                SubtitleSearchPanel.ShowLoading();
            }

            return;
        }

        BeginSubtitleLoad(index);
    }

    private void NoteCaptionsMissing(Uri page, int generation)
    {
        if (generation != _streamGeneration || _streamPage is null || !SameAddress(_streamPage, page))
        {
            return;
        }

        if (_streamSubtitles is { Count: > 0 })
        {
            return;
        }

        SubtitleSearchPanel.ShowCues([]);
    }

    private void BeginSubtitleLoad(int index)
    {
        _subtitleWork?.Cancel();
        if (_streamPage is null || _streamSubtitles is null || index < 0 || index >= _streamSubtitles.Count)
        {
            _streamSubtitle = null;
            Captions.LoadCues([]);
            if (index < 0 && _streamSubtitles is { Count: > 0 })
            {
                SubtitleSearchPanel.ShowOff();
            }
            else
            {
                SubtitleSearchPanel.ShowCues([]);
            }

            return;
        }

        SubtitleSearchPanel.ShowLoading();

        _subtitleWork = new CancellationTokenSource();
        _ = LoadPageSubtitleAsync(index, _subtitleWork.Token);
    }

    private void ReleaseSubtitlesAfterStart()
    {
        if (!_subtitleReleasePending)
        {
            return;
        }

        _subtitleReleasePending = false;
        var generation = _enrichGeneration;
        var token = _enrichToken;
        StartDeferredSubtitle();
        ScheduleCaptionEnrich(generation, token);
    }

    private void StartDeferredSubtitle()
    {
        _deferSubtitleFetch = false;
        if (_deferredSubtitle is not int index)
        {
            return;
        }

        _deferredSubtitle = null;
        BeginSubtitleLoad(index);
    }

    private void ScheduleCaptionEnrich(int generation, CancellationToken cancellationToken)
    {
        if (_captionsEnriched)
        {
            return;
        }

        _captionsEnriched = true;
        if (_streamPage is not Uri page)
        {
            return;
        }

        _ = EnrichCaptionsAsync(page, generation, cancellationToken);
    }

    private async Task EnrichCaptionsAsync(Uri page, int generation, CancellationToken cancellationToken)
    {
        try
        {
            var captions = await YoutubeDownloader.ResolveCaptionsAsync(page.AbsoluteUri, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (captions.Count == 0)
            {
                if (DispatcherQueue.HasThreadAccess)
                {
                    NoteCaptionsMissing(page, generation);
                }
                else
                {
                    DispatcherQueue.TryEnqueue(() => NoteCaptionsMissing(page, generation));
                }

                return;
            }

            if (DispatcherQueue.HasThreadAccess)
            {
                ApplyEnrichedCaptions(page, generation, captions);
                return;
            }

            DispatcherQueue.TryEnqueue(() => ApplyEnrichedCaptions(page, generation, captions));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // The picture is already playing. The caption list stays on the tracks from the first lookup.
        }
    }

    private void ApplyEnrichedCaptions(Uri page, int generation, IReadOnlyList<DownloadSubtitle> captions)
    {
        if (generation != _streamGeneration || _streamPage is null || !SameAddress(_streamPage, page))
        {
            return;
        }

        if (SameCaptions(_streamSubtitles, captions))
        {
            return;
        }

        _streamSubtitles = captions;
        ShowResolvedCaptions(keepDownload: true);
    }

    private static bool SameCaptions(IReadOnlyList<DownloadSubtitle>? current, IReadOnlyList<DownloadSubtitle> next)
    {
        if (current is null || current.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].Language, next[i].Language, StringComparison.OrdinalIgnoreCase)
                || current[i].Automatic != next[i].Automatic
                || current[i].Translated != next[i].Translated)
            {
                return false;
            }
        }

        return true;
    }

    private async Task LoadPageSubtitleAsync(int index, CancellationToken cancellationToken)
    {
        if (_streamPage is not Uri page || _streamSubtitles is null || index < 0 || index >= _streamSubtitles.Count)
        {
            _streamSubtitle = null;
            Captions.LoadCues([]);
            return;
        }

        var subtitle = _streamSubtitles[index];
        _streamSubtitle = subtitle;
        RememberStreamChoice();
        try
        {
            if (subtitle.Translated)
            {
                SetAddedText(TranslationWaitMessage);
            }

            var path = await YoutubeDownloader.FetchSubtitleAsync(page.AbsoluteUri, subtitle, cancellationToken);
            if (cancellationToken.IsCancellationRequested || _streamPage?.AbsoluteUri != page.AbsoluteUri)
            {
                return;
            }

            var cues = path is null ? [] : SubtitleCues.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            if (cancellationToken.IsCancellationRequested || _streamPage?.AbsoluteUri != page.AbsoluteUri)
            {
                return;
            }

            ShowPageCues(cues);
            if (subtitle.Translated)
            {
                SetAddedText(cues.Count == 0 ? TranslationFailedMessage : StreamStatusText());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ShowPageCues([]);
            }
        }
    }

    private string StreamStatusText()
        => _canSeek ? "Streaming" : "Streaming. This video cannot seek.";

    private void SetAddedText(string text)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => SetAddedText(text));
            return;
        }

        if (_streamPage is not null)
        {
            AddedText.Text = text;
        }
    }

    private void ShowPageCues(IReadOnlyList<SubtitleCue> cues)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowPageCues(cues));
            return;
        }

        Captions.LoadCues(cues);
        SubtitleSearchPanel.ShowCues(cues);
        if (_savedFocus is { } focus)
        {
            Captions.ShowSavedWord(focus.English, focus.Sentence, focus.TimeMs, redraw: false);
        }

        if (_player is not null)
        {
            Captions.SetTime(_player.Time);
        }
    }

    private void SaveCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_streamPage is not Uri page || _streamQuality is null || !SaveCopyButton.IsEnabled)
        {
            return;
        }

        try
        {
            var added = DownloadQueueHub.Enqueue(TitleText.Text, page.AbsoluteUri, _streamQuality, _streamSubtitle);
            SaveCopyButton.IsEnabled = false;
            SaveCopyLabel.Text = added ? "Added to Download" : "Already in Download";
        }
        catch (Exception ex)
        {
            AddedText.Text = ex.Message;
        }
    }

    private void TryApplyStreamStart()
    {
        if (_streamStartMs is not long startMs || startMs <= 0 || _player is null || _streamFailed)
        {
            return;
        }

        if (!_seekKnown || !_canSeek || !_hasValidDuration || _durationMs <= 0)
        {
            return;
        }

        if (_player.State is not (VLCState.Playing or VLCState.Paused or VLCState.Buffering))
        {
            return;
        }

        _streamStartMs = null;
        var target = Math.Clamp(startMs, 0, Math.Max(0, _durationMs - 400));
        if (target <= 0)
        {
            return;
        }

        _player.Time = target;
    }

    private void FailStream(string message)
    {
        _streamOpening = false;
        _streamFailed = true;
        _streamStartMs = null;
        _ended = true;
        AddedText.Text = message;
        UpdatePlayIcon();
    }

    private async void AddPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_streamPage is not Uri page)
        {
            return;
        }

        var lists = Playlists.All();
        var picker = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = nameof(Playlist.Name),
            MaxHeight = 220,
            Visibility = lists.Count == 0 ? Visibility.Collapsed : Visibility.Visible
        };
        foreach (var list in lists)
        {
            picker.Items.Add(list);
        }

        var name = new TextBox
        {
            Header = lists.Count == 0 ? "Playlist name" : "New playlist",
            PlaceholderText = lists.Count == 0 ? "Name" : "Leave this blank to use the playlist you select"
        };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var dialog = new ContentDialog
        {
            Title = "Add to playlist",
            Content = new StackPanel
            {
                Spacing = 12,
                Width = 420,
                Children = { picker, name, hint }
            },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var message = TryAddCurrentPage(page, TitleForPlaylist(), name.Text, picker.SelectedItem as Playlist, lists);
            if (message is null)
            {
                return;
            }

            args.Cancel = true;
            hint.Text = message;
            hint.Visibility = Visibility.Visible;
        };
        await dialog.ShowAsync();
    }

    private static string? TryAddCurrentPage(Uri page, string title, string typedName, Playlist? selected, IReadOnlyList<Playlist> lists)
    {
        var typed = typedName.Trim();
        Playlist? target = null;
        if (typed.Length > 0)
        {
            if (typed.Length > 80)
            {
                return "Use a name of 80 characters or fewer.";
            }

            target = lists.FirstOrDefault(item => string.Equals(item.Name, typed, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                try
                {
                    target = Playlists.Create(typed);
                }
                catch (IOException)
                {
                    return "Could not save the playlist.";
                }
                catch (UnauthorizedAccessException)
                {
                    return "Could not save the playlist.";
                }

                if (target is null)
                {
                    return "You already have a playlist with that name.";
                }
            }
        }
        else if (selected is not null)
        {
            target = selected;
        }
        else
        {
            return lists.Count == 0 ? "Enter a name for the playlist." : "Choose a playlist, or enter a new name.";
        }

        try
        {
            var added = Playlists.AddPage(target.Id, page.AbsoluteUri, title);
            return added switch
            {
                1 => null,
                0 => "This page is already in that playlist.",
                _ => "That link cannot be saved."
            };
        }
        catch (IOException)
        {
            return "Could not save the playlist.";
        }
        catch (UnauthorizedAccessException)
        {
            return "Could not save the playlist.";
        }
    }

    private string TitleForPlaylist()
    {
        var title = TitleText.Text.Trim();
        return title.Length == 0 ? _streamPage?.AbsoluteUri ?? string.Empty : title;
    }

    private void ChaptersButton_Click(object sender, RoutedEventArgs e)
    {
        VideoChapters.ListOpen = !VideoChapters.ListOpen;
        ShowChapters();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var open = SubtitleSearchPanel.Visibility != Visibility.Visible;
        SubtitleSearchPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(SearchButton, open ? "Hide subtitle search" : "Search subtitles");
    }

    private void WordsButton_Click(object sender, RoutedEventArgs e) => WordsPanel.Toggle();

    private void WordsPanel_WordChosen(object? sender, SavedWord word) => OpenSavedWord(word);

    private void OpenSavedWord(SavedWord word)
    {
        if (_editing || _savingTrim)
        {
            WordsPanel.SetStatus("Finish trimming before opening another moment.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(word.PageUrl))
        {
            OpenSavedStreamWord(word);
            return;
        }

        if (string.IsNullOrWhiteSpace(word.VideoPath) || word.TimeMs is not long time)
        {
            WordsPanel.SetStatus("This word was saved before a video moment was remembered.");
            return;
        }

        if (!File.Exists(word.VideoPath))
        {
            WordsPanel.SetStatus("That video is no longer on this PC.");
            return;
        }

        WordsPanel.SetStatus(null);
        _savedFocus = word;
        var sameVideo = string.Equals(_filePath, word.VideoPath, StringComparison.OrdinalIgnoreCase)
            && _streamPage is null
            && _streamUrl is null
            && _player is not null;
        Captions.ShowSavedWord(word.English, word.Sentence, time, redraw: sameVideo);
        if (sameVideo)
        {
            SeekToSavedTime(time);
            return;
        }

        _fromQueue = false;
        StopForReplacement();
        StopWatchingStream();
        var item = MediaFor(word.VideoPath);
        _openAtMs = Math.Max(0, time);
        TitleText.Text = item.DisplayName;
        AddedText.Text = $"Added {item.ImportedAt.ToLocalTime():g}";
        RestoreButton.Visibility = App.MediaLibrary.HasOriginal(item.FilePath)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _filePath = item.FilePath;
        WordsPanel.CurrentVideoPath = item.FilePath;
        Playback.SetHoverSource(item.FilePath);
        ShowBookmarks();
        _pendingPath = item.FilePath;
        if (_player is not null)
        {
            PlayFile(item.FilePath);
        }

        if (WordsPanel.IsOpen)
        {
            WordsPanel.Refresh();
        }

        NotePlaylist(item.FilePath);
    }

    private void OpenSavedStreamWord(SavedWord word)
    {
        if (word.TimeMs is not long time || !StreamLink.TryNormalize(word.PageUrl, out var page))
        {
            WordsPanel.SetStatus("This word was saved before a video moment was remembered.");
            return;
        }

        var samePage = StreamWordKey() is string key
            && string.Equals(key, page.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        WordsPanel.SetStatus(null);
        _savedFocus = word;
        Captions.ShowSavedWord(word.English, word.Sentence, time, redraw: samePage && _player is not null);
        if (!samePage)
        {
            HostWindow?.OpenSavedStream(word);
            return;
        }

        if (_player is null)
        {
            _streamStartMs = time > 0 ? time : null;
            return;
        }

        var reopened = RestoreSavedAudio(word, time);
        RestoreSavedCaption(word);
        if (reopened)
        {
            return;
        }

        if (_seekKnown && _canSeek && _hasValidDuration)
        {
            var target = Math.Clamp(time, 0, Math.Max(0, _durationMs - 400));
            if (target > 0)
            {
                _player!.Time = target;
            }

            return;
        }

        if (_seekKnown && !_canSeek)
        {
            WordsPanel.SetStatus("This stream cannot seek.");
            return;
        }

        _streamStartMs = time > 0 ? time : null;
    }

    private string? StreamWordKey()
        => _streamPage?.AbsoluteUri ?? _streamUrl?.AbsoluteUri;

    private void ShowResolvedCaptions(bool keepDownload = false)
    {
        if (_streamPage is null || _streamSubtitles is not { Count: > 0 })
        {
            return;
        }

        string? requested;
        string? preferred;
        if (_captionChosen)
        {
            requested = _captionChoice;
            preferred = null;
        }
        else
        {
            requested = string.IsNullOrWhiteSpace(_streamSubtitle?.Language)
                ? _savedFocus?.CaptionLanguage
                : _streamSubtitle.Language;
            preferred = string.IsNullOrWhiteSpace(requested)
                ? StreamLanguageSettings.Load().CaptionLanguage
                : null;
        }

        var index = StreamLanguageSettings.ChooseCaption(_streamSubtitles, requested, preferred);
        var selected = index >= 0 && index < _streamSubtitles.Count ? _streamSubtitles[index].Language : null;
        var unchanged = keepDownload && string.Equals(selected, _streamSubtitle?.Language, StringComparison.OrdinalIgnoreCase);
        if (keepDownload && selected is null && _streamSubtitle is null)
        {
            unchanged = true;
        }

        _applyingCaptionOffer = true;
        try
        {
            Playback.OfferCaptionChoices(_streamSubtitles.Select(item => item.Label).ToList(), index, announce: !unchanged);
        }
        finally
        {
            _applyingCaptionOffer = false;
        }
    }

    private int CaptionIndex(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || _streamSubtitles is null)
        {
            return 0;
        }

        for (var i = 0; i < _streamSubtitles.Count; i++)
        {
            if (string.Equals(_streamSubtitles[i].Language, language, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private bool RestoreSavedAudio(SavedWord word, long time)
    {
        if (string.IsNullOrWhiteSpace(word.AudioLanguage) || _streamUrl is null)
        {
            return false;
        }

        _restoreAudioLanguage = word.AudioLanguage.Trim();
        if (_hlsMaster is null)
        {
            RefreshLocalAudio();
            return false;
        }

        if (!_hlsMaster.Audios.Any(item => item.Key.Equals(word.AudioLanguage, StringComparison.OrdinalIgnoreCase))
            || string.Equals(word.AudioLanguage, _hlsAudioKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _hlsAudioKey = _restoreAudioLanguage;
        _streamStartMs = time > 0 ? time : null;
        _ = OpenStreamAsync(_streamUrl);
        return true;
    }

    private void RestoreSavedCaption(SavedWord word)
    {
        if (string.IsNullOrWhiteSpace(word.CaptionLanguage) || _streamSubtitles is not { Count: > 0 })
        {
            return;
        }

        var index = CaptionIndex(word.CaptionLanguage);
        if (!string.Equals(_streamSubtitles[index].Language, word.CaptionLanguage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_streamSubtitle?.Language, word.CaptionLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Playback.OfferCaptionChoices(_streamSubtitles.Select(item => item.Label).ToList(), index);
    }

    private void RememberStreamChoice()
    {
        var audio = string.IsNullOrWhiteSpace(_hlsAudioKey) ? null : _hlsAudioKey;
        if (audio is null && _player is not null)
        {
            audio = LocalAudioName(_player.AudioTrack);
        }

        Captions.RememberChoice(audio, _streamSubtitle?.Language);
    }

    private string? LocalAudioName(int id)
    {
        if (_player is null)
        {
            return null;
        }

        try
        {
            foreach (var track in _player.AudioTrackDescription)
            {
                if (track.Id == id && !string.IsNullOrWhiteSpace(track.Name))
                {
                    return track.Name.Trim();
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private void StopWatchingStream()
    {
        if (_streamUrl is null && _streamPage is null)
        {
            return;
        }

        _streamGeneration++;
        _streamWork?.Cancel();
        _subtitleWork?.Cancel();
        try
        {
            _player?.Stop();
        }
        catch (Exception)
        {
            // The stream player can already be stopped.
        }

        ReleaseStreamMedia();
        _streamUrl = null;
        _streamPage = null;
        _streamAudio = null;
        _streamSubtitles = null;
        _streamSubtitle = null;
        _streamQuality = null;
        _streamThumbnail = null;
        _streamReferrer = null;
        _streamStartMs = null;
        _streamFailed = false;
        _streamResolving = false;
        _streamOpening = false;
        _pendingStream = null;
        _pendingPageResolve = false;
        _restoreAudioLanguage = null;
        ClearStreamChoices();
        AddPlaylistButton.Visibility = Visibility.Collapsed;
        EditButton.Visibility = Visibility.Visible;
        WordsButton.Visibility = Visibility.Visible;
        EditTrimButton.Visibility = Visibility.Visible;
        SaveCopyButton.Visibility = Visibility.Collapsed;
        if (_playlistId is null)
        {
            PlaylistButton.Visibility = Visibility.Collapsed;
            PlaylistPanel.Hide();
            _playlistBeforeMini = false;
        }
        else
        {
            PlaylistButton.Visibility = Visibility.Visible;
        }

        Playback.SnapshotButton.Visibility = Visibility.Visible;
        Playback.BookmarkButton.Visibility = Visibility.Visible;
        Playback.UseSectionRepeat(true);
    }

    private void PlaylistButton_Click(object sender, RoutedEventArgs e) => PlaylistPanel.Toggle();

    private void AdvanceForward()
    {
        if (_editing || _savingTrim)
        {
            PlaylistPanel.SetStatus("Finish trimming before opening another video.");
            return;
        }

        if (TryPlayQueued())
        {
            return;
        }

        PlaylistPanel.PlayForward();
    }

    private bool TryPlayQueued()
    {
        if (_editing || _savingTrim)
        {
            return false;
        }

        ReleaseRestoredHold();
        _heldMissing = false;

        while (PlayQueue.Advance(PlayQueue.Count, hasPlaylist: false) == PlayAdvanceKind.Queue)
        {
            var item = PlayQueue.TakeNext();
            if (item is null)
            {
                return false;
            }

            if (TryStartQueued(item))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryStartQueued(PlayQueueItem item)
    {
        if (item.Kind == PlayQueueKind.Page)
        {
            if (!StreamLink.TryNormalize(item.Location, out var page))
            {
                return false;
            }

            BeginQueued();
            Uri? thumbnail = null;
            if (item.Thumbnail is not null && Uri.TryCreate(item.Thumbnail, UriKind.Absolute, out var picture))
            {
                thumbnail = picture;
            }

            var title = string.IsNullOrWhiteSpace(item.Title) ? StreamLink.DisplayName(page) : item.Title;
            ShowStream(new StreamOpenRequest(page, title, Page: page, Thumbnail: thumbnail), keepPlaylist: true);
            AddedText.Text = QueueCaption();
            if (WordsPanel.IsOpen)
            {
                WordsPanel.Refresh();
            }

            return true;
        }

        if (!File.Exists(item.Location))
        {
            return false;
        }

        BeginQueued();
        OpenFileItem(MediaFor(item.Location));
        AddedText.Text = QueueCaption();
        if (WordsPanel.IsOpen)
        {
            WordsPanel.Refresh();
        }

        return true;
    }

    private void BeginQueued()
    {
        _fromQueue = true;
        StopForReplacement();
        _openAtMs = null;
        _savedFocus = null;
        ClearSectionRepeat();
        Captions.ClearSavedWord();
    }

    private string QueueCaption()
    {
        var waiting = PlayQueue.Count;
        var waitingText = waiting switch
        {
            0 => null,
            1 => "1 video still queued",
            _ => $"{waiting} videos still queued"
        };
        if (_playlistId is null)
        {
            return waitingText is null ? "From the queue" : "From the queue · " + waitingText;
        }

        var name = Playlists.Find(_playlistId)?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "the playlist";
        }

        var then = "then " + name;
        return waitingText is null ? "From the queue · " + then : "From the queue · " + waitingText + " · " + then;
    }

    private void RememberStreamChapters(IReadOnlyList<VideoChapter>? incoming, bool samePage)
    {
        if ((incoming is null || incoming.Count == 0) && samePage && _chaptersFromLookup)
        {
            return;
        }

        _chapterEpoch++;
        _chapters = incoming is { Count: > 0 } ? incoming : [];
        _chaptersFromLookup = _chapters.Count > 0;
        _chapterIndex = -1;
    }

    private void UseLookupChapters(IReadOnlyList<VideoChapter>? chapters)
    {
        if (chapters is not { Count: > 0 })
        {
            return;
        }

        _chaptersFromLookup = true;
        if (VideoChapters.Same(_chapters, chapters))
        {
            ShowChapters();
            return;
        }

        _chapterEpoch++;
        _chapters = chapters;
        _chapterIndex = -1;
        ShowChapters();
    }

    private void ClearChapters()
    {
        _chapterEpoch++;
        _chaptersFromLookup = false;
        _chapters = [];
        _chapterIndex = -1;
        ShowChapters();
    }

    private void ReadPlayerChapters()
    {
        if (_player is null || _chaptersFromLookup)
        {
            return;
        }

        ChapterDescription[] described;
        try
        {
            described = _player.FullChapterDescriptions(-1);
        }
        catch (Exception)
        {
            return;
        }

        if (described is null || described.Length == 0)
        {
            return;
        }

        var next = VideoChapters.FromOffsets(described.Select(item => (item.TimeOffset, item.Name)));
        if (VideoChapters.Same(_chapters, next))
        {
            return;
        }

        _chapters = next;
        _chapterIndex = -1;
        ShowChapters();
    }

    private void ShowChapters()
    {
        ChapterPanel.Bind(_chapters);
        var choice = VideoChapters.Choose(VideoChapters.ListOpen, _mini, HasSession, _chapters.Count);
        ChapterPanel.Visibility = choice.ShowList ? Visibility.Visible : Visibility.Collapsed;
        ChaptersButton.Visibility = choice.ShowButton ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(ChaptersButton, choice.ShowList ? "Hide chapters" : "Show chapters");
        HighlightChapter(force: choice.ShowList);
    }

    private void HighlightChapter(bool force = false)
    {
        var index = _player is null || _chapters.Count == 0
            ? -1
            : VideoChapters.Current(_chapters, Math.Max(0, _player.Time));
        if (!force && index == _chapterIndex)
        {
            return;
        }

        _chapterIndex = index;
        ChapterPanel.Highlight(index, force);
    }

    private void PlayChapter(long startMs)
    {
        if (_player is null)
        {
            return;
        }

        var canSeek = _hasValidDuration && _durationMs > 0 && (_streamUrl is null || _canSeek);
        if (!canSeek)
        {
            AddedText.Text = _streamUrl is null ? "This video cannot seek." : "This stream cannot seek.";
            return;
        }

        JumpTo(Math.Max(0, startMs));
    }

    private void OnQueueChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ShowQueue);

    private void ShowQueue()
    {
        var items = PlayQueue.Snapshot();
        QueuePanel.Bind(items);
        var undo = PlayQueue.IsPending(out _);
        QueuePanel.Visibility = !_mini && HasSession && (items.Count > 0 || undo)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_fromQueue && AddedText.Text.StartsWith("From the queue", StringComparison.Ordinal))
        {
            AddedText.Text = QueueCaption();
        }
    }

    private void OpenPlaylistVideo(int index)
    {
        if (_playlistId is null)
        {
            return;
        }

        if (_editing || _savingTrim)
        {
            PlaylistPanel.SetStatus("Finish trimming before opening another video.");
            return;
        }

        var list = Playlists.Find(_playlistId);
        if (list is null || index < 0 || index >= list.Videos.Count)
        {
            PlaylistPanel.SetStatus("This playlist is no longer available.");
            return;
        }

        var entry = list.Videos[index];
        if (entry.Resolve)
        {
            OpenPlaylistPage(entry, index);
            return;
        }

        var path = entry.Location;
        if (!File.Exists(path))
        {
            PlaylistPanel.SetStatus("That video is no longer on this PC.");
            return;
        }

        var sameFile = index == _playlistIndex
            && string.Equals(_filePath, path, StringComparison.OrdinalIgnoreCase)
            && _streamPage is null
            && _streamUrl is null;
        if (sameFile && _player is not null && !_ended)
        {
            if (_fromQueue)
            {
                _fromQueue = false;
                AddedText.Text = PlaylistLabel();
            }

            PlaylistPanel.SetStatus(null);
            return;
        }

        if (!sameFile)
        {
            RememberPosition(force: true);
        }

        if (_streamPage is not null || _streamUrl is not null || !string.Equals(_filePath, path, StringComparison.OrdinalIgnoreCase))
        {
            ClearChapters();
        }

        _fromQueue = false;
        ClearSectionRepeat();
        _savedFocus = null;
        _openAtMs = sameFile ? 0 : null;
        Captions.ClearSavedWord();
        var item = MediaFor(path);
        _playlistIndex = index;
        TitleText.Text = item.DisplayName;
        AddedText.Text = PlaylistLabel();
        RestoreButton.Visibility = App.MediaLibrary.HasOriginal(item.FilePath)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _filePath = item.FilePath;
        WordsPanel.CurrentVideoPath = item.FilePath;
        Playback.SetHoverSource(item.FilePath);
        ShowBookmarks();
        _pendingPath = item.FilePath;
        PlaylistPanel.SetStatus(null);
        ShowPlaylist();
        if (_player is not null)
        {
            PlayFile(item.FilePath);
        }

        if (WordsPanel.IsOpen)
        {
            WordsPanel.Refresh();
        }

        _timer.Start();
    }

    private void OpenPlaylistPage(PlaylistEntry entry, int index)
    {
        if (!StreamLink.TryNormalize(entry.Location, out var page))
        {
            PlaylistPanel.SetStatus("That link cannot be opened.");
            return;
        }

        var samePage = index == _playlistIndex
            && _streamPage is not null
            && SameAddress(_streamPage, page);
        if (samePage && _player is not null && !_ended && !_streamFailed)
        {
            if (_fromQueue)
            {
                _fromQueue = false;
                if (_seekKnown)
                {
                    AddedText.Text = StreamStatusText();
                }
            }

            PlaylistPanel.SetStatus(null);
            return;
        }

        if (!samePage)
        {
            RememberPosition(force: true);
        }

        _fromQueue = false;
        ClearSectionRepeat();
        _savedFocus = null;
        Captions.ClearSavedWord();
        _playlistIndex = index;
        var title = string.IsNullOrWhiteSpace(entry.Title) ? StreamLink.DisplayName(page) : entry.Title;
        ShowStream(new StreamOpenRequest(page, title, Page: page), keepPlaylist: true);
        PlaylistPanel.SetStatus(null);
    }

    private bool PlayFollowingVideo()
    {
        if (_editing || _savingTrim || _playlistId is null)
        {
            return false;
        }

        var list = Playlists.Find(_playlistId);
        if (list is null)
        {
            return false;
        }

        var step = PlaylistRuns.For(list.Id).Move(
            PlaylistRun.Keys(list),
            index => PlaylistRun.Include(list, index, _playlistIndex, PlaylistPanel.UnwatchedOnly),
            _playlistIndex,
            list.PlayNext,
            fromEnd: true,
            forward: true,
            PlaylistRuns.Random);
        switch (step.Kind)
        {
            case PlaylistStepKind.Replay:
                RestartPlaylistVideo();
                return true;
            case PlaylistStepKind.Open:
                OpenPlaylistVideo(step.Index);
                return true;
            default:
                return false;
        }
    }

    private void RestartPlaylistVideo()
    {
        if (_playlistId is null)
        {
            return;
        }

        var list = Playlists.Find(_playlistId);
        if (list is null || _playlistIndex < 0 || _playlistIndex >= list.Videos.Count)
        {
            return;
        }

        var entry = list.Videos[_playlistIndex];
        if (entry.Resolve)
        {
            if (!StreamLink.TryNormalize(entry.Location, out var page))
            {
                PlaylistPanel.SetStatus("That link cannot be opened.");
                return;
            }

            PlaybackProgress.Save(page.AbsoluteUri, 0, 1);
            _ended = true;
            _streamEndedCleanly = true;
            OpenPlaylistPage(entry, _playlistIndex);
            return;
        }

        if (!File.Exists(entry.Location))
        {
            PlaylistPanel.SetStatus("That video is no longer on this PC.");
            return;
        }

        PlaybackProgress.Save(entry.Location, 0, 1);
        _ended = true;
        _openAtMs = 0;
        OpenPlaylistVideo(_playlistIndex);
    }

    private void MarkPlaylistVideoWatched()
    {
        if (_playlistId is null || _playlistIndex < 0 || _editing || _savingTrim || _streamFailed)
        {
            return;
        }

        try
        {
            if (!Playlists.SetWatched(_playlistId, _playlistIndex, true))
            {
                return;
            }
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (PlaylistPanel.Visibility == Visibility.Visible)
        {
            ShowPlaylist();
        }
    }

    private void ApplyPlaylistOrder(int index)
    {
        if (_playlistId is null || index < 0)
        {
            return;
        }

        _playlistIndex = index;
        RememberPlaylistPlace();
        if (_streamPage is null && !string.IsNullOrWhiteSpace(_filePath))
        {
            AddedText.Text = _fromQueue ? QueueCaption() : PlaylistLabel();
        }
    }

    private void RememberPlaylistThumbnail()
    {
        if (_playlistId is null || _streamPage is null || _streamThumbnail is null)
        {
            return;
        }

        try
        {
            if (!Playlists.RememberThumbnail(_playlistId, _streamPage.AbsoluteUri, _streamThumbnail))
            {
                return;
            }

            var known = StreamThumbnail.ForPage(_streamPage.AbsoluteUri);
            if (!string.Equals(_streamThumbnail, known, StringComparison.Ordinal))
            {
                ShowPlaylist();
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ShowPlaylist()
    {
        if (_playlistId is null)
        {
            return;
        }

        RememberPlaylistPlace();
        PlaylistButton.Visibility = Visibility.Visible;
        if (_mini)
        {
            PlaylistPanel.Sync(_playlistId, _playlistIndex);
            return;
        }

        PlaylistPanel.Show(_playlistId, _playlistIndex);
    }

    private void RefreshPlaylistLabel()
    {
        if (_fromQueue)
        {
            if (_streamPage is null && _streamUrl is null)
            {
                AddedText.Text = QueueCaption();
            }

            return;
        }

        if (_playlistId is null || _streamPage is not null || _streamUrl is not null)
        {
            return;
        }

        AddedText.Text = PlaylistLabel();
    }

    private void RememberPlaylistPlace()
    {
        if (_playlistId is null)
        {
            return;
        }

        var list = Playlists.Find(_playlistId);
        if (list is null)
        {
            return;
        }

        PlaylistRuns.For(list.Id).CatchUp(
            PlaylistRun.Keys(list),
            index => PlaylistRun.Include(list, index, _playlistIndex, PlaylistPanel.UnwatchedOnly),
            _playlistIndex,
            PlaylistRuns.Random);
    }

    private void LeavePlaylist()
    {
        _playlistId = null;
        _playlistIndex = -1;
        _playlistBeforeMini = false;
        PlaylistButton.Visibility = Visibility.Collapsed;
        PlaylistPanel.Hide();
    }

    private void NotePlaylist(string path)
    {
        if (_playlistId is null)
        {
            return;
        }

        var list = Playlists.Find(_playlistId);
        var index = list?.Videos.FindIndex(item => !item.Resolve && string.Equals(item.Location, path, StringComparison.OrdinalIgnoreCase)) ?? -1;
        if (list is null || index < 0)
        {
            LeavePlaylist();
            return;
        }

        _playlistIndex = index;
        ShowPlaylist();
    }

    private string PlaylistLabel()
    {
        var list = Playlists.Find(_playlistId);
        if (list is null || list.Videos.Count == 0)
        {
            return list?.Name ?? "Playlist";
        }

        var place = _playlistIndex >= 0 && _playlistIndex < list.Videos.Count ? _playlistIndex + 1 : 1;
        var label = $"{list.Name} · {place} of {list.Videos.Count}";
        var note = PlaylistRuns.For(list.Id).ActiveNote;
        return note is null ? label : label + " · " + note;
    }

    private void SeekToSavedTime(long time)
    {
        if (_player is null)
        {
            _openAtMs = Math.Max(0, time);
            return;
        }

        var target = Math.Max(0, time);
        if (_hasValidDuration)
        {
            target = Math.Clamp(target, 0, Math.Max(0, _durationMs - 400));
        }

        if (HasSectionLoop)
        {
            var start = Math.Min(_repeatAMs!.Value, _repeatBMs!.Value);
            var end = Math.Max(_repeatAMs.Value, _repeatBMs.Value);
            if (target < start || target >= end)
            {
                ClearSectionRepeat();
            }
        }

        var ended = _ended || _player.State is VLCState.Ended or VLCState.Stopped;
        _ended = false;
        ReleaseRestoredHold();
        if (_resumePending)
        {
            _resumeMs = target;
            _openAtMs = target;
            _explicitStart = true;
        }

        _player.Time = target;
        if (ended)
        {
            TakePlayback();
            _player.Play();
        }

        AddedText.Text = $"Opened at {FormatMs(target)}.";
        HoldCaptions(target);
        UpdatePlayIcon();
    }

    private static MediaItem MediaFor(string path)
    {
        return App.MediaLibrary.GetItems().FirstOrDefault(media =>
                string.Equals(media.FilePath, path, StringComparison.OrdinalIgnoreCase))
            ?? new MediaItem
            {
                Id = path,
                Kind = MediaKind.Video,
                DisplayName = Path.GetFileName(path),
                FilePath = path,
                ImportedAt = new DateTimeOffset(File.GetCreationTimeUtc(path)),
                FileSizeBytes = new FileInfo(path).Length
            };
    }

    private static string FormatMs(long durationMs)
    {
        if (durationMs < 0 || durationMs > MaxRealisticMs)
        {
            return "00:00";
        }

        var totalSeconds = durationMs / 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }
}
