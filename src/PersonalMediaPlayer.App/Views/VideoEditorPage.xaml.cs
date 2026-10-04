using System.Globalization;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Controls;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.Core.Models;
using PlaybackStore = PersonalMediaPlayer.App.Playback;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class VideoEditorPage : Page, PlaybackStore.IPlaybackSource
{
    private readonly DispatcherTimer _timer;
    private LibVLC? _libVlc;
    private VlcMediaPlayer? _player;
    private VideoView? _videoView;
    private string[]? _swapChainOptions;
    private MediaItem? _item;
    private string? _path;
    private string? _pendingPath;
    private long _durationMs;
    private bool _updatingSlider;
    private bool _pausedForOther;
    private bool _dragging;
    private bool _saving;
    private bool _allowLeave;
    private bool _closeWindow;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private bool _pendingIsBack;
    private bool _selectingCut;
    private string _timelineMode = "seek";
    private string _tool = "speed";
    private bool _trimReady;
    private long _trimStartMs;
    private long _trimEndMs;
    private long? _markStartMs;
    private long? _markEndMs;
    private readonly List<(long StartMs, long EndMs)> _cuts = [];
    private bool _cropPreview;
    private int _quarterTurns;
    private bool _flipHorizontal;
    private bool _flipVertical;
    private int _videoWidth;
    private int _videoHeight;
    private bool _hasAudio;
    private int _audioRate = 48000;
    private bool _extracting;
    private Media? _openMedia;
    private readonly DispatcherTimer _historyTimer;
    private readonly List<EditorSnapshot> _history = [];
    private int _historyIndex;
    private bool _historyReady;
    private bool _restoring;
    private double _listenVolume = 80;

    public VideoEditorPage()
    {
        InitializeComponent();
        Playback.SpeedCombo.Visibility = Visibility.Collapsed;
        Playback.FullScreenButton.Visibility = Visibility.Collapsed;
        Playback.UseTimelineZoom(true);
        var remembered = PlaybackStore.PlaybackVolume.Load();
        _listenVolume = remembered.Audible;
        Playback.VolumeSlider.Value = remembered.Level;
        ApplyVolume();
        Playback.MuteButton.Click += PlaybackMute_Click;
        Playback.VolumeSlider.ValueChanged += PlaybackVolume_Changed;
        Playback.PlayButton.Click += Play_Click;
        Playback.BackButton.Click += (_, _) => Skip(-10_000);
        Playback.ForwardButton.Click += (_, _) => Skip(10_000);
        Playback.SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(Seek_Pressed), true);
        Playback.SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(Seek_Released), true);
        Playback.SeekSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(Seek_Released), true);
        Playback.SeekSlider.ValueChanged += Seek_Changed;
        Playback.CutSelected += Cut_Selected;
        Playback.TimelineClicked += Timeline_Clicked;
        Playback.TrimRangeChanged += (_, _) => ApplyTrimFromBar();
        Playback.TrimSeekRequested += (_, fraction) => SeekToFraction(fraction);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => UpdateClock();
        _historyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _historyTimer.Tick += (_, _) =>
        {
            _historyTimer.Stop();
            CommitHistory();
        };
        _history.Add(Capture());
        _historyReady = true;
        SelectTool("speed");
        PlaybackStore.PlaybackFocus.Register(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _item = e.Parameter switch
        {
            MediaItem media => App.MediaLibrary.GetById(media.Id) ?? media,
            string id => App.MediaLibrary.GetById(id),
            _ => null
        };
        if (_item is null)
        {
            return;
        }

        _path = _item.FilePath;
        FileNameText.Text = _item.DisplayName;
        Playback.SetHoverSource(_path);
        _pendingPath = _path;
        _timer.Start();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AttachVideo);
        if (_player is not null)
        {
            PlayFile(_path);
        }
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

        if (PictureHost.Child is null)
        {
            PictureHost.Child = _videoView;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _timer.Stop();
        Playback.SetHoverSource(null);
        ReleasePlayer();
    }

    internal bool PrepareToLeave(Type? pageType, object? parameter, bool back)
    {
        _pendingPageType = pageType;
        _pendingParameter = parameter;
        _pendingIsBack = back;
        if (_allowLeave || !HasEdits)
        {
            return true;
        }

        if (_saving)
        {
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Message = "The video is still saving.";
            StatusBar.IsOpen = true;
            return false;
        }

        LeavePrompt.Visibility = Visibility.Visible;
        return false;
    }

    internal bool TryHandleHostClose()
    {
        if (_allowLeave || !HasEdits)
        {
            return false;
        }

        if (_saving)
        {
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Message = "The video is still saving.";
            StatusBar.IsOpen = true;
            return true;
        }

        _closeWindow = true;
        DispatcherQueue.TryEnqueue(() => LeavePrompt.Visibility = Visibility.Visible);
        return true;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (_allowLeave || !HasEdits)
        {
            ReleasePlayer();
            return;
        }

        if (_saving)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _pendingPageType = e.SourcePageType;
        _pendingParameter = e.Parameter;
        _pendingIsBack = e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back;
        if (LeavePrompt.Visibility != Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(() => LeavePrompt.Visibility = Visibility.Visible);
        }
    }

    private bool HasSpeedChange => SpeedSlider is not null && Math.Abs(SpeedSlider.Value - 1d) > 0.02;

    private bool HasVolumeChange => VolumeSlider is not null && Math.Abs(VolumeSlider.Value - 100d) > 0.5;

    private bool HasCuts => _cuts.Count > 0;

    private bool HasTrim => _durationMs > 0 && (_trimStartMs > 50 || _durationMs - _trimEndMs > 50);

    private bool HasCrop => CropSurface is not null && CropSurface.HasCrop;

    private bool HasOrientation => _quarterTurns != 0 || _flipHorizontal || _flipVertical;

    private bool HasVideoFade => VideoFadeInBox?.IsChecked == true || VideoFadeOutBox?.IsChecked == true;

    private bool HasAudioFade => AudioFadeInBox?.IsChecked == true || AudioFadeOutBox?.IsChecked == true;

    private bool HasEdits => HasSpeedChange || HasVolumeChange || HasCuts || HasTrim || HasCrop || HasOrientation || HasVideoFade || HasAudioFade;

    private Editing.VideoOrientation CurrentOrientation() => new(_quarterTurns, _flipHorizontal, _flipVertical);

    private void VideoView_Initialized(object? sender, InitializedEventArgs e)
    {
        _swapChainOptions = e.SwapChainOptions;
        _libVlc = new LibVLC(enableDebugLogs: false, PlaybackStore.PlaybackAudio.Options(e.SwapChainOptions));
        _player = new VlcMediaPlayer(_libVlc);
        _player.LengthChanged += (_, args) => DispatcherQueue.TryEnqueue(() => SetDuration(args.Length));
        _player.Playing += (_, _) => DispatcherQueue.TryEnqueue(OnEditorPlaying);
        _player.EndReached += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!string.IsNullOrWhiteSpace(_path))
            {
                PlayFile(_path);
            }
        });
        if (_videoView is not null)
        {
            _videoView.MediaPlayer = _player;
        }
        ApplyVolume();
        ApplyRate();
        if (!string.IsNullOrWhiteSpace(_pendingPath))
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

        _openMedia?.Dispose();
        var media = new Media(_libVlc, path, FromType.FromPath);
        _openMedia = media;
        media.ParsedChanged += (_, args) =>
        {
            if (args.ParsedStatus == MediaParsedStatus.Done)
            {
                DispatcherQueue.TryEnqueue(() => ApplyVideoSize(media));
            }
        };
        media.Parse(MediaParseOptions.ParseLocal);
        ApplyVideoSize(media);
        SetDuration(media.Duration);
        if (_pausedForOther)
        {
            return;
        }

        TakePlayback();
        _player.Play(media);
        ApplyRate();
        UpdatePlayIcon();
    }

    private void OnEditorPlaying()
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
        }

        UpdatePlayIcon();
    }

    private void TakePlayback()
    {
        _pausedForOther = false;
        PlaybackStore.PlaybackFocus.Claim(this);
    }

    void PlaybackStore.IPlaybackSource.PauseForOther()
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
        if (_player is not null)
        {
            try
            {
                _player.Stop();
            }
            catch (Exception)
            {
                // The player can already be stopped when the page is leaving.
            }

            if (_videoView is not null)
            {
                _videoView.MediaPlayer = null;
            }

            _player.Dispose();
            _player = null;
        }

        _openMedia?.Dispose();
        _openMedia = null;
        _libVlc?.Dispose();
        _libVlc = null;
        if (PictureHost.Child is not null)
        {
            PictureHost.Child = null;
        }
    }

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
        else if (!string.IsNullOrWhiteSpace(_path) && _player.State == VLCState.Ended)
        {
            _pausedForOther = false;
            PlayFile(_path);
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

        var next = Math.Clamp(_player.Time + delta, 0, Math.Max(0, _durationMs));
        _player.Time = next;
        UpdateClock();
    }

    private void ApplyVolume() => ApplyPlaybackFades();

    private void Seek_Pressed(object sender, PointerRoutedEventArgs e) => _dragging = true;

    private void Seek_Released(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        if (_player is not null && _durationMs > 0)
        {
            _player.Time = PlayableTime((long)(Playback.SeekSlider.Value / 1000d * _durationMs));
        }
    }

    private void Seek_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_dragging || _updatingSlider || _player is null || _durationMs <= 0)
        {
            return;
        }

        var time = PlayableTime((long)(e.NewValue / 1000d * _durationMs));
        _player.Time = time;
        Playback.PositionText.Text = Format(time);
        ApplyPlaybackFades();
    }

    private void UpdateClock()
    {
        if (_player is null || _durationMs <= 0 || _dragging)
        {
            return;
        }

        var time = PlayableTime(_player.Time);
        if (time != _player.Time)
        {
            _player.Time = time;
        }

        _updatingSlider = true;
        Playback.SeekSlider.Value = Math.Clamp(time * 1000d / _durationMs, 0, 1000);
        _updatingSlider = false;
        Playback.PositionText.Text = Format(time);
        ApplyPlaybackFades();
        UpdatePlayIcon();
    }

    private void ApplyPlaybackFades()
    {
        var video = 1d;
        var audio = 1d;
        if (_player is not null && _durationMs > 0)
        {
            var output = OutputTimeMs(PlayableTime(_player.Time));
            var total = OutputDurationMs();
            video = FadeLevel(VideoFadeInBox?.IsChecked == true, VideoFadeInSeconds?.Value ?? 0, VideoFadeOutBox?.IsChecked == true, VideoFadeOutSeconds?.Value ?? 0, output, total);
            audio = FadeLevel(AudioFadeInBox?.IsChecked == true, AudioFadeInSeconds?.Value ?? 0, AudioFadeOutBox?.IsChecked == true, AudioFadeOutSeconds?.Value ?? 0, output, total);
        }

        if (PictureHost is not null)
        {
            PictureHost.Opacity = video;
        }

        var listen = Playback.VolumeSlider?.Value ?? 0;
        if (_player is not null)
        {
            var faded = listen * audio;
            _player.Mute = faded <= 0.5;
            _player.Volume = (int)Math.Round(faded);
        }

        if (Playback.MuteIcon is not null)
        {
            Playback.MuteIcon.Glyph = listen <= 0.5 ? "\uE74F" : "\uE767";
        }
    }

    private long OutputDurationMs()
        => (long)(KeptMs() / Math.Max(0.25, SpeedSlider?.Value ?? 1));

    private long OutputTimeMs(long sourceMs)
    {
        var start = _trimStartMs;
        var end = _trimEndMs > start ? _trimEndMs : _durationMs;
        var time = Math.Clamp(sourceMs, start, Math.Max(start, end));
        long removed = 0;
        foreach (var cut in _cuts.OrderBy(item => item.StartMs))
        {
            var from = Math.Max(cut.StartMs, start);
            var to = Math.Min(cut.EndMs, end);
            if (to <= from)
            {
                continue;
            }

            if (time >= to)
            {
                removed += to - from;
            }
            else if (time > from)
            {
                removed += time - from;
            }
        }

        var into = Math.Max(0, time - start - removed);
        return (long)(into / Math.Max(0.25, SpeedSlider?.Value ?? 1));
    }

    private static double FadeLevel(bool fadeIn, double inSeconds, bool fadeOut, double outSeconds, long outputMs, long totalMs)
    {
        var level = 1d;
        var time = Math.Max(0, outputMs) / 1000d;
        var total = Math.Max(0, totalMs) / 1000d;
        if (fadeIn && inSeconds > 0 && total > 0)
        {
            var length = Math.Min(inSeconds, total);
            if (time < length)
            {
                level = Math.Min(level, time / length);
            }
        }

        if (fadeOut && outSeconds > 0 && total > 0)
        {
            var length = Math.Min(outSeconds, total);
            var fadeStart = Math.Max(0, total - length);
            if (time > fadeStart)
            {
                level = Math.Min(level, Math.Max(0, total - time) / length);
            }
        }

        return Math.Clamp(level, 0, 1);
    }

    private void Fade_Changed(object sender, RoutedEventArgs e) => NoteFadeChange();

    private void Fade_Changed(object sender, NumberBoxValueChangedEventArgs e) => NoteFadeChange();

    private void NoteFadeChange()
    {
        if (_restoring
            || VideoFadeInBox is null
            || VideoFadeOutBox is null
            || AudioFadeInBox is null
            || AudioFadeOutBox is null
            || VideoFadeInSeconds is null
            || VideoFadeOutSeconds is null
            || AudioFadeInSeconds is null
            || AudioFadeOutSeconds is null)
        {
            return;
        }

        VideoFadeInSeconds.IsEnabled = VideoFadeInBox.IsChecked == true;
        VideoFadeOutSeconds.IsEnabled = VideoFadeOutBox.IsChecked == true;
        AudioFadeInSeconds.IsEnabled = AudioFadeInBox.IsChecked == true;
        AudioFadeOutSeconds.IsEnabled = AudioFadeOutBox.IsChecked == true;
        ApplyPlaybackFades();
        if (SaveButton is not null)
        {
            SaveButton.IsEnabled = HasEdits && !_saving;
        }

        NoteEdit();
    }

    private void UpdatePlayIcon()
    {
        Playback.PlayIcon.Glyph = _player?.IsPlaying == true ? "\uE769" : "\uE768";
    }

    private void SetDuration(long durationMs)
    {
        if (durationMs <= 0)
        {
            return;
        }

        _durationMs = durationMs;
        Playback.SetHoverDuration(durationMs);
        Playback.DurationText.Text = Format(durationMs);
        Playback.SeekSlider.IsEnabled = true;
        if (!_trimReady)
        {
            _trimStartMs = 0;
            _trimEndMs = durationMs;
            Playback.BeginTrim(passThrough: true);
            _trimReady = true;
            ApplyTimelineMode();
            UpdateTrimSummary();
        }
        else if (!HasTrim)
        {
            _trimEndMs = durationMs;
            Playback.SetTrimFractions(0, 1);
        }

        UpdateSpeedText();
    }

    private void SpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (SpeedLabel is null || DurationLabel is null || SaveButton is null)
        {
            return;
        }

        ApplyRate();
        UpdateSpeedText();
        SaveButton.IsEnabled = HasEdits && !_saving;
        NoteEdit();
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rate))
        {
            SpeedSlider.Value = rate;
        }
    }

    private void ApplyRate()
    {
        _player?.SetRate((float)SpeedSlider.Value);
    }

    private void UpdateSpeedText()
    {
        var rate = SpeedSlider.Value;
        SpeedLabel.Text = $"{rate:0.##}×";
        if (_durationMs <= 0)
        {
            DurationLabel.Text = "Length appears when the video opens.";
            return;
        }

        var next = (long)(KeptMs() / rate);
        DurationLabel.Text = $"Kept {Format(KeptMs())}. Saved {Format(next)}.";
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            SelectTool(tag);
        }
    }

    private void SelectTool(string tool)
    {
        _tool = tool;
        ShowPanel(SpeedPanel, tool == "speed");
        ShowPanel(SoundPanel, tool == "sound");
        ShowPanel(RotatePanel, tool == "rotate");
        ShowPanel(FadePanel, tool == "fade");
        ShowPanel(CropPanel, tool == "crop");
        ShowPanel(TrimPanel, tool == "trim");
        ShowPanel(CutPanel, tool == "cut");
        MarkTool(ToolSpeedButton, ToolSpeedMark, tool == "speed");
        MarkTool(ToolSoundButton, ToolSoundMark, tool == "sound");
        MarkTool(ToolRotateButton, ToolRotateMark, tool == "rotate");
        MarkTool(ToolFadeButton, ToolFadeMark, tool == "fade");
        MarkTool(ToolCropButton, ToolCropMark, tool == "crop");
        MarkTool(ToolTrimButton, ToolTrimMark, tool == "trim");
        MarkTool(ToolCutButton, ToolCutMark, tool == "cut");
        _timelineMode = tool switch
        {
            "trim" => "trim",
            "cut" => "cut",
            _ => "seek"
        };
        ApplyTimelineMode();
        UpdateCropChrome();
    }

    private static void ShowPanel(UIElement? panel, bool show)
    {
        if (panel is not null)
        {
            panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void MarkTool(Button? button, UIElement? mark, bool selected)
    {
        if (button is null)
        {
            return;
        }

        VisualStateManager.GoToState(button, selected ? "Selected" : "Unselected", false);
        if (mark is Microsoft.UI.Xaml.Shapes.Shape dot)
        {
            if (selected)
            {
                dot.Fill = new SolidColorBrush(Microsoft.UI.Colors.White);
            }
            else
            {
                dot.ClearValue(Microsoft.UI.Xaml.Shapes.Shape.FillProperty);
            }
        }
    }

    private void UpdateToolMarks()
    {
        SetMark(ToolSpeedMark, HasSpeedChange);
        SetMark(ToolSoundMark, HasVolumeChange);
        SetMark(ToolRotateMark, HasOrientation);
        SetMark(ToolFadeMark, HasVideoFade || HasAudioFade);
        SetMark(ToolCropMark, HasCrop);
        SetMark(ToolTrimMark, HasTrim);
        SetMark(ToolCutMark, HasCuts);
    }

    private static void SetMark(UIElement? mark, bool active)
    {
        if (mark is not null)
        {
            mark.Opacity = active ? 1 : 0;
        }
    }

    private void ApplyTimelineMode()
    {
        _selectingCut = _timelineMode == "cut";
        Playback.SetCutPicking(_selectingCut);
        Playback.SetTrimInteractive(_timelineMode == "trim");
        if (SelectCutButton is not null)
        {
            SelectCutButton.Content = _selectingCut ? "Selecting" : "Timeline";
            VisualStateManager.GoToState(SelectCutButton, _selectingCut ? "Selected" : "Unselected", false);
        }
    }

    private static Style Accent() => (Style)Application.Current.Resources["AccentButtonStyle"];

    private void SelectCut_Click(object sender, RoutedEventArgs e)
    {
        _timelineMode = _timelineMode == "cut" ? "seek" : "cut";
        ApplyTimelineMode();
    }

    private void MarkStart_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
        {
            return;
        }

        _markStartMs = _player.Time;
        UpdateCutMark();
    }

    private void CutTime_LostFocus(object sender, RoutedEventArgs e) => ApplyTypedCut(seek: false);

    private void CutTime_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ApplyTypedCut(seek: true);
            e.Handled = true;
        }
    }

    private void ApplyTypedCut(bool seek)
    {
        if (_durationMs <= 0 || CutFromBox is null || CutToBox is null)
        {
            return;
        }

        var fromText = CutFromBox.Text.Trim();
        var toText = CutToBox.Text.Trim();
        var fromOk = TryParseClock(fromText, out var fromMs);
        var toOk = TryParseClock(toText, out var toMs);
        if (fromText.Length > 0 && !fromOk || toText.Length > 0 && !toOk)
        {
            CutMarkLabel.Text = "Use a time like 1:05.4.";
            return;
        }

        _markStartMs = fromText.Length == 0 ? null : Math.Clamp(fromMs, 0, _durationMs);
        _markEndMs = toText.Length == 0 ? null : Math.Clamp(toMs, 0, _durationMs);
        UpdateCutMark();
        if (seek && _player is not null && _markStartMs is long start)
        {
            _player.Time = PlayableTime(start);
            UpdateClock();
        }
    }

    private void MarkEnd_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
        {
            return;
        }

        _markEndMs = _player.Time;
        UpdateCutMark();
    }

    private void Timeline_Clicked(object? sender, double fraction)
    {
        if (_player is null || _durationMs <= 0)
        {
            return;
        }

        _player.Time = PlayableTime((long)(fraction * _durationMs));
        UpdateClock();
    }

    private void Cut_Selected(object? sender, VideoPlaybackBar.CutSelection selection)
    {
        if (_durationMs <= 0)
        {
            return;
        }

        _markStartMs = (long)(selection.Start * _durationMs);
        _markEndMs = (long)(selection.End * _durationMs);
        UpdateCutMark();
    }

    private void RemoveCut_Click(object sender, RoutedEventArgs e)
    {
        if (_markStartMs is not long start || _markEndMs is not long end || _durationMs <= 0)
        {
            return;
        }

        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (end - start < 400)
        {
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Message = "Select at least half a second.";
            StatusBar.IsOpen = true;
            return;
        }

        _cuts.Add((start, end));
        NormalizeCuts();
        _markStartMs = null;
        _markEndMs = null;
        if (CutFromBox is not null)
        {
            CutFromBox.Text = string.Empty;
        }

        if (CutToBox is not null)
        {
            CutToBox.Text = string.Empty;
        }

        UpdateCutMark();
        RefreshCuts();
        SaveButton.IsEnabled = HasEdits && !_saving;
    }

    private void NormalizeCuts()
    {
        var merged = VideoTrimmer.MergeSpans(
            _cuts.Select(cut => (TimeSpan.FromMilliseconds(cut.StartMs), TimeSpan.FromMilliseconds(cut.EndMs))).ToArray(),
            TimeSpan.FromMilliseconds(Math.Max(0, _durationMs)));
        _cuts.Clear();
        _cuts.AddRange(merged.Select(span => ((long)span.Start.TotalMilliseconds, (long)span.End.TotalMilliseconds)));
    }

    private void UpdateCutMark()
    {
        if (CutMarkLabel is null)
        {
            return;
        }

        var hasBoth = _markStartMs is long start && _markEndMs is long end;
        var fromMs = _markStartMs ?? 0;
        var toMs = _markEndMs ?? 0;
        CutMarkLabel.Text = hasBoth
            ? $"Selected {FormatFine(Math.Abs(toMs - fromMs))}: {FormatFine(Math.Min(fromMs, toMs))} to {FormatFine(Math.Max(fromMs, toMs))}."
            : _markStartMs is long onlyStart
                ? $"Start {FormatFine(onlyStart)}. Set the end."
                : _markEndMs is long onlyEnd
                    ? $"End {FormatFine(onlyEnd)}. Set the start."
                    : "Nothing selected yet.";
        if (CutFromBox is not null && CutFromBox.FocusState == FocusState.Unfocused && _markStartMs is long from)
        {
            CutFromBox.Text = FormatFine(from);
        }

        if (CutToBox is not null && CutToBox.FocusState == FocusState.Unfocused && _markEndMs is long to)
        {
            CutToBox.Text = FormatFine(to);
        }

        if (_durationMs > 0 && hasBoth)
        {
            Playback.SetSelectionFraction(Math.Min(fromMs, toMs) / (double)_durationMs, Math.Max(fromMs, toMs) / (double)_durationMs);
        }
        else if (!hasBoth)
        {
            Playback.SetSelectionFraction(null, null);
        }

        NoteEdit();
    }

    private void RefreshCuts()
    {
        CutList.Children.Clear();
        if (_durationMs > 0)
        {
            Playback.SetRemovedFractions(_cuts.Select(cut => (cut.StartMs / (double)_durationMs, cut.EndMs / (double)_durationMs)).ToArray());
        }

        foreach (var cut in _cuts.ToArray())
        {
            var row = new StackPanel { Spacing = 8 };
            var label = new TextBlock
            {
                Text = $"{FormatFine(cut.StartMs)} – {FormatFine(cut.EndMs)}",
                TextWrapping = TextWrapping.Wrap
            };
            var restore = new Button
            {
                Content = "Put back",
                HorizontalAlignment = HorizontalAlignment.Left,
                Style = (Style)Resources["EditorChip"],
                Tag = cut
            };
            restore.Click += (_, _) =>
            {
                _cuts.Remove(cut);
                RefreshCuts();
                UpdateSpeedText();
                SaveButton.IsEnabled = HasEdits && !_saving;
            };
            row.Children.Add(label);
            row.Children.Add(restore);
            CutList.Children.Add(new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(12),
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                Child = row
            });
        }

        UpdateSpeedText();
    }

    private long PlayableTime(long time)
    {
        var start = _trimStartMs;
        var end = _trimEndMs > start ? _trimEndMs : _durationMs;
        if (end > start && (time < start || time >= end))
        {
            return start;
        }

        foreach (var cut in _cuts)
        {
            if (time >= cut.StartMs && time < cut.EndMs)
            {
                var landed = Math.Min(end, cut.EndMs);
                return landed >= end ? start : Math.Max(landed, start);
            }
        }

        return time;
    }

    private List<(long StartMs, long EndMs)> RemovedRanges()
    {
        var ranges = new List<(long StartMs, long EndMs)>();
        if (_trimStartMs > 50)
        {
            ranges.Add((0, _trimStartMs));
        }

        ranges.AddRange(_cuts);
        if (_durationMs - _trimEndMs > 50)
        {
            ranges.Add((_trimEndMs, _durationMs));
        }

        return ranges;
    }

    private long KeptMs()
    {
        var start = _trimStartMs;
        var end = _trimEndMs > start ? _trimEndMs : _durationMs;
        var removedInside = _cuts.Sum(cut =>
        {
            var from = Math.Max(cut.StartMs, start);
            var to = Math.Min(cut.EndMs, end);
            return Math.Max(0, to - from);
        });
        return Math.Max(0, end - start - removedInside);
    }

    private void ApplyTrimFromBar()
    {
        if (_durationMs <= 0 || TrimSummary is null)
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
        UpdateSpeedText();
        if (SaveButton is not null)
        {
            SaveButton.IsEnabled = HasEdits && !_saving;
        }

        NoteEdit();
    }

    private void UpdateTrimSummary()
    {
        if (TrimSummary is null)
        {
            return;
        }

        TrimStartLabel.Text = $"Start {FormatFine(_trimStartMs)}";
        TrimEndLabel.Text = $"End {FormatFine(_trimEndMs > 0 ? _trimEndMs : _durationMs)}";
        if (!HasTrim)
        {
            TrimSummary.Text = "The whole video is kept.";
            return;
        }

        var keep = Math.Max(0, _trimEndMs - _trimStartMs);
        TrimSummary.Text = $"Keeps {FormatFine(keep)}, from {FormatFine(_trimStartMs)} to {FormatFine(_trimEndMs)}.";
    }

    private void NudgeTrim_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || _durationMs <= 0)
        {
            return;
        }

        var parts = tag.Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[1], out var delta))
        {
            return;
        }

        _timelineMode = "trim";
        ApplyTimelineMode();
        var start = _trimStartMs;
        var end = _trimEndMs > start ? _trimEndMs : _durationMs;
        if (parts[0] == "start")
        {
            start = Math.Clamp(start + delta, 0, end - 400);
        }
        else
        {
            end = Math.Clamp(end + delta, start + 400, _durationMs);
        }

        Playback.SetTrimFractions(start / (double)_durationMs, end / (double)_durationMs);
        ApplyTrimFromBar();
        if (_player is not null)
        {
            _player.Time = parts[0] == "start" ? _trimStartMs : Math.Max(_trimStartMs, _trimEndMs - 1);
        }
    }

    private void TrimStart_Click(object sender, RoutedEventArgs e) => SetTrimEdge(start: true);

    private void TrimEnd_Click(object sender, RoutedEventArgs e) => SetTrimEdge(start: false);

    private void SetTrimEdge(bool start)
    {
        if (_player is null || _durationMs <= 0)
        {
            return;
        }

        var fraction = _player.Time / (double)_durationMs;
        if (start)
        {
            Playback.SetTrimFractions(fraction, Playback.TrimEnd);
        }
        else
        {
            Playback.SetTrimFractions(Playback.TrimStart, fraction);
        }

        ApplyTrimFromBar();
    }

    private void ResetTrim_Click(object sender, RoutedEventArgs e)
    {
        if (_durationMs <= 0)
        {
            return;
        }

        Playback.SetTrimFractions(0, 1);
        ApplyTrimFromBar();
    }

    private void SeekToFraction(double fraction)
    {
        if (_player is null || _durationMs <= 0)
        {
            return;
        }

        _player.Time = (long)(fraction * _durationMs);
        UpdateClock();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (VolumeLabel is null || SaveButton is null)
        {
            return;
        }

        var volume = (int)e.NewValue;
        VolumeLabel.Text = volume <= 0 ? "Muted in the file" : $"{volume}% in the file";
        SaveButton.IsEnabled = HasEdits && !_saving;
        NoteEdit();
    }

    private void PlaybackVolume_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (e.NewValue > 0)
        {
            _listenVolume = e.NewValue;
        }

        PlaybackStore.PlaybackVolume.Save(e.NewValue);
        ApplyVolume();
    }

    private void PlaybackMute_Click(object sender, RoutedEventArgs e)
    {
        if (Playback.VolumeSlider.Value > 0)
        {
            _listenVolume = Playback.VolumeSlider.Value;
            Playback.VolumeSlider.Value = 0;
        }
        else
        {
            Playback.VolumeSlider.Value = _listenVolume <= 0 ? 80 : _listenVolume;
        }
    }

    private void VolumePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var volume))
        {
            VolumeSlider.Value = volume;
        }
    }

    private async void ExtractAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_extracting || _saving || string.IsNullOrWhiteSpace(_path) || _item is null)
        {
            return;
        }

        if (!_hasAudio)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = "This video has no audio track.";
            StatusBar.IsOpen = true;
            return;
        }

        var choice = await AskAudioSaveAsync();
        if (choice is null)
        {
            return;
        }

        string destination;
        var temporary = false;
        if (choice.OnPc)
        {
            var picked = await FilePickerHelper.PickSaveAudioAsync(App.MainAppWindow, choice.Name, choice.Extension);
            if (picked is null)
            {
                return;
            }

            destination = picked.Path;
        }
        else
        {
            destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + choice.Extension);
            temporary = true;
        }

        _extracting = true;
        ExtractAudioButton.IsEnabled = false;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Message = "Saving the audio…";
        StatusBar.IsOpen = true;
        try
        {
            await VideoTrimmer.ExtractAudioAsync(
                _path,
                destination,
                TimeSpan.FromMilliseconds(Math.Max(_durationMs, 1)),
                _cuts.Select(cut => (TimeSpan.FromMilliseconds(cut.StartMs), TimeSpan.FromMilliseconds(cut.EndMs))).ToArray(),
                TimeSpan.FromMilliseconds(_trimStartMs),
                TimeSpan.FromMilliseconds(_trimEndMs > 0 ? _trimEndMs : _durationMs),
                SpeedSlider.Value,
                VolumeSlider.Value / 100d,
                _audioRate,
                AudioFadeInBox.IsChecked == true ? AudioFadeInSeconds.Value : 0,
                AudioFadeOutBox.IsChecked == true ? AudioFadeOutSeconds.Value : 0);
            var savedTo = destination;
            if (!choice.OnPc)
            {
                await using var input = File.OpenRead(destination);
                App.MediaLibrary.ImportMedia(input, choice.Name + choice.Extension, choice.Album);
                savedTo = string.IsNullOrWhiteSpace(choice.Album) ? "the library" : choice.Album!;
            }

            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Message = "Saved the audio to " + savedTo;
            StatusBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = ex.Message;
            StatusBar.IsOpen = true;
        }
        finally
        {
            if (temporary && File.Exists(destination))
            {
                try
                {
                    File.Delete(destination);
                }
                catch (IOException)
                {
                }
            }

            _extracting = false;
            ExtractAudioButton.IsEnabled = true;
        }
    }

    private async Task<AudioSaveChoice?> AskAudioSaveAsync()
    {
        var suggested = Path.GetFileNameWithoutExtension(_item!.DisplayName);
        var nameBox = new TextBox
        {
            Header = "Name",
            Text = string.IsNullOrWhiteSpace(suggested) ? "Audio" : suggested
        };
        var format = new ComboBox
        {
            Header = "Format",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items = { "MP3", "WAV" },
            SelectedIndex = 0
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
            Title = "Extract audio",
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { nameBox, format, method, album, newFolder }
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

        var name = CleanAudioName(nameBox.Text);
        var extension = format.SelectedIndex == 1 ? ".wav" : ".mp3";
        if (method.SelectedIndex == 1)
        {
            return new AudioSaveChoice(name, extension, true, null);
        }

        if (album.SelectedItem as string == "New folder…")
        {
            if (string.IsNullOrWhiteSpace(newFolder.Text))
            {
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.Message = "Enter a folder name.";
                StatusBar.IsOpen = true;
                return null;
            }

            try
            {
                var created = App.MediaLibrary.CreateFolder(newFolder.Text);
                return new AudioSaveChoice(name, extension, false, created);
            }
            catch (Exception ex)
            {
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.Message = ex.Message;
                StatusBar.IsOpen = true;
                return null;
            }
        }

        var selected = album.SelectedItem as string;
        return new AudioSaveChoice(name, extension, false, selected == "Library" ? null : selected);
    }

    private static string CleanAudioName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(character => invalid.Contains(character) ? ' ' : character).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(cleaned) ? "Audio" : cleaned;
    }

    private sealed record AudioSaveChoice(string Name, string Extension, bool OnPc, string? Album);

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || _item is null || string.IsNullOrWhiteSpace(_path) || !HasEdits)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Save edits",
            Content = HasSpeedChange
                ? "Save as a new video, or overwrite this one? Overwrite keeps the original, and bookmarks move to the new times."
                : "Save as a new video, or overwrite this one? Overwrite keeps the original.",
            PrimaryButtonText = "Save as new",
            SecondaryButtonText = "Overwrite",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result is ContentDialogResult.None)
        {
            return;
        }

        var overwrite = result == ContentDialogResult.Secondary;
        var rate = SpeedSlider.Value;
        var source = _path;
        var temp = source + ".speed.mp4";
        string? cutFile = null;
        _saving = true;
        SaveButton.IsEnabled = false;
        SaveProgress.IsActive = true;
        StatusBar.IsOpen = false;
        try
        {
            Playback.ReleaseHoverFile();
            ReleasePlayer();
            var produced = source;
            string? working = null;
            string? oriented = null;
            var filters = CurrentOrientation().FfmpegFilters;
            var needsEncoder = HasSpeedChange || HasVolumeChange || (HasCrop && _videoWidth <= 0);
            if (needsEncoder && filters is not null)
            {
                oriented = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
                await VideoTrimmer.RenderAsync(
                    source,
                    oriented,
                    [],
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(Math.Max(_durationMs, 1)),
                    null,
                    filters);
                produced = oriented;
            }
            if (!needsEncoder && (HasCuts || HasTrim || HasCrop || HasOrientation))
            {
                working = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(source));
                File.Copy(source, working, overwrite: true);
                try
                {
                    (int X, int Y, int Width, int Height)? crop = HasCrop ? PixelCrop() : null;
                    await VideoTrimmer.RenderAsync(
                        working,
                        temp,
                        _cuts.Select(cut => (TimeSpan.FromMilliseconds(cut.StartMs), TimeSpan.FromMilliseconds(cut.EndMs))).ToArray(),
                        TimeSpan.FromMilliseconds(_trimStartMs),
                        TimeSpan.FromMilliseconds(_trimEndMs > 0 ? _trimEndMs : _durationMs),
                        crop,
                        needsEncoder ? null : filters);
                }
                finally
                {
                    if (File.Exists(working))
                    {
                        File.Delete(working);
                    }
                }
            }
            else if (HasCuts || HasTrim)
            {
                working = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(source));
                File.Copy(source, working, overwrite: true);
                cutFile = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(source) + ".sections.mp4");
                if (File.Exists(cutFile))
                {
                    File.Delete(cutFile);
                }

                var trimWhileEncoding = _durationMs > 0 && (HasSpeedChange || HasVolumeChange || HasCrop);
                if (!trimWhileEncoding)
                {
                    try
                    {
                        await VideoTrimmer.RemoveSectionsAsync(
                            working,
                            cutFile,
                            _cuts.Select(cut => (TimeSpan.FromMilliseconds(cut.StartMs), TimeSpan.FromMilliseconds(cut.EndMs))).ToArray(),
                            TimeSpan.FromMilliseconds(_trimStartMs),
                            TimeSpan.FromMilliseconds(_trimEndMs > 0 ? _trimEndMs : _durationMs));
                    }
                    finally
                    {
                        if (File.Exists(working))
                        {
                            File.Delete(working);
                        }
                    }

                    produced = cutFile;
                }
                else if (File.Exists(working))
                {
                    File.Delete(working);
                }
            }

            if (needsEncoder)
            {
                var crop = HasCrop
                    ? new VideoSpeedEncoder.VideoCrop(CropSurface.Left, CropSurface.Top, CropSurface.Right, CropSurface.Bottom)
                    : (VideoSpeedEncoder.VideoCrop?)null;
                await VideoTrimmer.ChangeSpeedAsync(
                    produced,
                    temp,
                    rate,
                    VolumeSlider.Value / 100d,
                    crop,
                    (HasCuts || HasTrim) && _durationMs > 0 ? KeptSourceRanges() : null);
            }
            else if (cutFile is not null)
            {
                temp = cutFile;
                cutFile = null;
            }

            if (HasVideoFade || (_hasAudio && HasAudioFade))
            {
                var faded = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
                var edited = needsEncoder || HasCuts || HasTrim || HasCrop || HasOrientation;
                var sourceForFade = edited && File.Exists(temp) ? temp : source;
                await VideoTrimmer.ApplyFadesAsync(
                    sourceForFade,
                    faded,
                    OutputDurationMs() / 1000d,
                    VideoFadeInBox.IsChecked == true ? VideoFadeInSeconds.Value : 0,
                    VideoFadeOutBox.IsChecked == true ? VideoFadeOutSeconds.Value : 0,
                    _hasAudio && AudioFadeInBox.IsChecked == true ? AudioFadeInSeconds.Value : 0,
                    _hasAudio && AudioFadeOutBox.IsChecked == true ? AudioFadeOutSeconds.Value : 0);
                if (!string.Equals(sourceForFade, source, StringComparison.OrdinalIgnoreCase) && File.Exists(sourceForFade))
                {
                    File.Delete(sourceForFade);
                }

                temp = faded;
            }

            if (cutFile is not null && !string.Equals(cutFile, temp, StringComparison.OrdinalIgnoreCase) && File.Exists(cutFile))
            {
                File.Delete(cutFile);
                cutFile = null;
            }

            if (oriented is not null && !string.Equals(oriented, temp, StringComparison.OrdinalIgnoreCase) && File.Exists(oriented))
            {
                File.Delete(oriented);
            }

            MediaItem saved;
            if (overwrite)
            {
                App.MediaLibrary.PreserveOriginal(source);
                File.Replace(temp, source, destinationBackupFileName: null, ignoreMetadataErrors: true);
                PlaybackStore.PlaybackBookmarks.Remap(source, RemovedRanges(), rate);
                PlaybackStore.PlaybackProgress.Save(source, 0, 1);
                saved = App.MediaLibrary.GetById(_item.Id) ?? _item;
            }
            else
            {
                var named = Path.Combine(Path.GetTempPath(), EditedFileName(source, rate));
                if (!string.Equals(temp, named, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(named))
                    {
                        File.Delete(named);
                    }

                    File.Move(temp, named);
                    temp = named;
                }

                saved = App.MediaLibrary.ImportMedia(temp, null);
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // The library already has its own copy.
                }

                foreach (var folder in _item.FolderName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!LibraryFolder.IsSmartName(folder)
                        && !folder.Equals(LibraryFolder.Screenshots, StringComparison.OrdinalIgnoreCase)
                        && !folder.Equals(LibraryFolder.Unfiled, StringComparison.OrdinalIgnoreCase))
                    {
                        App.MediaLibrary.AddToFolder(saved.FilePath, folder);
                    }
                }
            }

            _allowLeave = true;
            ReleasePlayer();
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
                Frame.ForwardStack.Clear();
            }
            else if (Frame.Navigate(typeof(HomePage)))
            {
                Frame.BackStack.Clear();
            }

            if (App.MainAppWindow is not MainWindow window || !window.ShowPlayer(saved))
            {
                _allowLeave = false;
                throw new InvalidOperationException("The saved video could not be opened.");
            }
        }
        catch (Exception ex)
        {
            if (File.Exists(temp) && !string.Equals(temp, source, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // The failed export can stay until the next save.
                }
            }

            if (cutFile is not null && File.Exists(cutFile))
            {
                try
                {
                    File.Delete(cutFile);
                }
                catch (IOException)
                {
                    // The failed export can stay until the next save.
                }
            }

            StatusBar.Message = ex.Message;
            StatusBar.IsOpen = true;
            _pendingPath = source;
            _videoView = null;
            AttachVideo();
        }
        finally
        {
            _saving = false;
            SaveProgress.IsActive = false;
            SaveButton.IsEnabled = HasEdits;
        }
    }

    private (long Start, long End)[] KeptSourceRanges()
    {
        var duration = TimeSpan.FromMilliseconds(_durationMs);
        var removed = _cuts
            .Select(cut => (TimeSpan.FromMilliseconds(cut.StartMs), TimeSpan.FromMilliseconds(cut.EndMs)))
            .ToArray();
        var from = TimeSpan.FromMilliseconds(_trimStartMs);
        var to = TimeSpan.FromMilliseconds(_trimEndMs > 0 ? _trimEndMs : _durationMs);
        return VideoTrimmer.KeptSpans(duration, removed)
            .Select(span => (Start: span.Start < from ? from : span.Start, End: span.End > to ? to : span.End))
            .Where(span => span.End > span.Start)
            .Select(span => (span.Start.Ticks, span.End.Ticks))
            .ToArray();
    }

    private string EditedFileName(string source, double rate)
    {
        var stem = Path.GetFileNameWithoutExtension(source);
        var parts = new List<string>();
        if (HasSpeedChange)
        {
            parts.Add($"{rate:0.##}x");
        }

        if (HasVolumeChange)
        {
            var volume = (int)VolumeSlider.Value;
            parts.Add(volume <= 0 ? "muted" : $"{volume}%");
        }

        if (_quarterTurns != 0)
        {
            parts.Add($"{_quarterTurns * 90}deg");
        }

        if (_flipHorizontal)
        {
            parts.Add("hflip");
        }

        if (_flipVertical)
        {
            parts.Add("vflip");
        }

        if (HasVideoFade)
        {
            parts.Add("vfade");
        }

        if (HasAudioFade)
        {
            parts.Add("afade");
        }

        if (HasCrop)
        {
            parts.Add("crop");
        }

        if (HasTrim)
        {
            parts.Add("trim");
        }

        if (HasCuts)
        {
            parts.Add("cut");
        }

        var description = parts.Count == 0 ? "edit" : string.Join(' ', parts);
        var videos = Path.Combine(App.MediaLibrary.LibraryRoot, "Videos");
        var name = $"{stem} {description}";
        var candidate = name;
        for (var copy = 2; File.Exists(Path.Combine(videos, candidate + ".mp4")); copy++)
        {
            candidate = $"{name} {copy}";
        }

        return candidate + ".mp4";
    }

    private void ApplyVideoSize(Media media)
    {
        if (!ReferenceEquals(media, _openMedia))
        {
            return;
        }

        foreach (var track in media.Tracks)
        {
            if (track.TrackType == TrackType.Audio)
            {
                _hasAudio = true;
                if (track.Data.Audio.Rate > 0)
                {
                    _audioRate = (int)track.Data.Audio.Rate;
                }
            }

            if (track.TrackType != TrackType.Video || track.Data.Video.Width <= 0 || track.Data.Video.Height <= 0)
            {
                continue;
            }

            _videoWidth = (int)track.Data.Video.Width;
            _videoHeight = (int)track.Data.Video.Height;
            LayoutCrop();
            if (media.Duration > 0)
            {
                SetDuration(media.Duration);
            }
        }
    }

    private void VideoHost_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutCrop();

    private void LayoutCrop()
    {
        if (CropSurface is null || _videoWidth <= 0 || _videoHeight <= 0 || VideoHost.ActualWidth <= 1 || VideoHost.ActualHeight <= 1)
        {
            return;
        }

        var (pictureWidth, pictureHeight) = CurrentOrientation().DisplaySize(_videoWidth, _videoHeight);
        var scale = Math.Min(VideoHost.ActualWidth / pictureWidth, VideoHost.ActualHeight / pictureHeight);
        var width = pictureWidth * scale;
        var height = pictureHeight * scale;
        var x = (VideoHost.ActualWidth - width) / 2;
        var y = (VideoHost.ActualHeight - height) / 2;
        CropSurface.SetPicture(x, y, width, height);
        var swapped = (_quarterTurns & 1) == 1;
        var viewWidth = Math.Max(2, swapped ? height : width);
        var viewHeight = Math.Max(2, swapped ? width : height);
        PictureHost.HorizontalAlignment = HorizontalAlignment.Left;
        PictureHost.VerticalAlignment = VerticalAlignment.Top;
        PictureHost.Width = viewWidth;
        PictureHost.Height = viewHeight;
        PictureHost.Margin = new Thickness(x + (width - viewWidth) / 2, y + (height - viewHeight) / 2, 0, 0);
        PictureScale.ScaleX = _flipHorizontal ? -1 : 1;
        PictureScale.ScaleY = _flipVertical ? -1 : 1;
        PictureRotation.Angle = (_quarterTurns & 3) * 90;
    }

    private void CropSurface_Changed(object sender, EventArgs e)
    {
        if (_cropPreview)
        {
            ApplyCropPreview();
        }

        UpdateCropSummary();
        if (SaveButton is not null)
        {
            SaveButton.IsEnabled = HasEdits && !_saving;
        }

        NoteEdit();
    }

    private void UpdateCropSummary()
    {
        if (CropSummary is null || PreviewCropButton is null)
        {
            return;
        }

        PreviewCropButton.IsEnabled = HasCrop;
        if (!HasCrop)
        {
            CropSummary.Text = "The whole picture is kept.";
            if (_cropPreview)
            {
                _cropPreview = false;
                ApplyCropPreview();
            }

            return;
        }

        if (_videoWidth <= 0 || _videoHeight <= 0)
        {
            CropSummary.Text = "A crop is selected.";
            return;
        }

        var (x, y, width, height) = PixelCrop();
        CropSummary.Text = $"Keeps {width}×{height}, starting {x} px across and {y} px down.";
    }

    private (int X, int Y, int Width, int Height) PixelCrop()
    {
        var (pictureWidth, pictureHeight) = CurrentOrientation().DisplaySize(_videoWidth, _videoHeight);
        var x = Math.Clamp((int)Math.Round(CropSurface.Left * pictureWidth), 0, Math.Max(0, pictureWidth - 2)) & ~1;
        var y = Math.Clamp((int)Math.Round(CropSurface.Top * pictureHeight), 0, Math.Max(0, pictureHeight - 2)) & ~1;
        var right = Math.Clamp((int)Math.Round(CropSurface.Right * pictureWidth), x + 2, pictureWidth) & ~1;
        var bottom = Math.Clamp((int)Math.Round(CropSurface.Bottom * pictureHeight), y + 2, pictureHeight) & ~1;
        return (x, y, Math.Max(2, right - x), Math.Max(2, bottom - y));
    }

    private void ResetCrop_Click(object sender, RoutedEventArgs e)
    {
        _cropPreview = false;
        CropSurface.Reset();
        ApplyCropPreview();
        if (PreviewCropButton is not null)
        {
            PreviewCropButton.Content = "Preview";
        }
    }

    private void PreviewCrop_Click(object sender, RoutedEventArgs e)
    {
        if (!HasCrop)
        {
            return;
        }

        _cropPreview = !_cropPreview;
        ApplyCropPreview();
        PreviewCropButton.Content = _cropPreview ? "Full frame" : "Preview";
    }

    private void ApplyCropPreview()
    {
        if (_player is null)
        {
            return;
        }

        if (!_cropPreview || !HasCrop || _videoWidth <= 0 || _videoHeight <= 0)
        {
            _player.CropGeometry = string.Empty;
            UpdateCropChrome();
            return;
        }

        var (x, y, width, height) = PixelCrop();
        _player.CropGeometry = $"{width}x{height}+{x}+{y}";
        UpdateCropChrome();
    }

    private void UpdateCropChrome()
    {
        if (CropSurface is null)
        {
            return;
        }

        var show = _tool == "crop" && !_cropPreview;
        CropSurface.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CropSurface.IsHitTestVisible = show;
    }

    private void NoteEdit()
    {
        UpdateToolMarks();
        if (!_historyReady || _restoring)
        {
            return;
        }

        _historyTimer.Stop();
        _historyTimer.Start();
    }

    private void CommitHistory()
    {
        if (!_historyReady || _restoring)
        {
            return;
        }

        var snap = Capture();
        if (Same(snap, _history[_historyIndex]))
        {
            return;
        }

        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        _history.Add(snap);
        _historyIndex = _history.Count - 1;
        UpdateHistoryButtons();
    }

    private static bool Same(EditorSnapshot left, EditorSnapshot right)
    {
        return left.Speed == right.Speed
            && left.Volume == right.Volume
            && left.TrimStartMs == right.TrimStartMs
            && left.TrimEndMs == right.TrimEndMs
            && left.MarkStartMs == right.MarkStartMs
            && left.MarkEndMs == right.MarkEndMs
            && left.CropLeft == right.CropLeft
            && left.CropTop == right.CropTop
            && left.CropRight == right.CropRight
            && left.CropBottom == right.CropBottom
            && left.CropPreview == right.CropPreview
            && left.Cuts.SequenceEqual(right.Cuts)
            && left.QuarterTurns == right.QuarterTurns
            && left.FlipHorizontal == right.FlipHorizontal
            && left.FlipVertical == right.FlipVertical
            && left.VideoFadeIn == right.VideoFadeIn
            && Math.Abs(left.VideoFadeInSeconds - right.VideoFadeInSeconds) < 0.001
            && left.VideoFadeOut == right.VideoFadeOut
            && Math.Abs(left.VideoFadeOutSeconds - right.VideoFadeOutSeconds) < 0.001
            && left.AudioFadeIn == right.AudioFadeIn
            && Math.Abs(left.AudioFadeInSeconds - right.AudioFadeInSeconds) < 0.001
            && left.AudioFadeOut == right.AudioFadeOut
            && Math.Abs(left.AudioFadeOutSeconds - right.AudioFadeOutSeconds) < 0.001;
    }

    private void Undo_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox)
        {
            return;
        }

        args.Handled = true;
        Undo_Click(sender, new RoutedEventArgs());
    }

    private void Redo_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox)
        {
            return;
        }

        args.Handled = true;
        Redo_Click(sender, new RoutedEventArgs());
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex <= 0)
        {
            return;
        }

        _historyTimer.Stop();
        _historyIndex--;
        Restore(_history[_historyIndex]);
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex >= _history.Count - 1)
        {
            return;
        }

        _historyTimer.Stop();
        _historyIndex++;
        Restore(_history[_historyIndex]);
    }

    private EditorSnapshot Capture()
    {
        return new EditorSnapshot(
            SpeedSlider.Value,
            VolumeSlider.Value,
            _trimStartMs,
            _trimEndMs,
            _cuts.ToArray(),
            _markStartMs,
            _markEndMs,
            CropSurface.Left,
            CropSurface.Top,
            CropSurface.Right,
            CropSurface.Bottom,
            _cropPreview,
            _quarterTurns,
            _flipHorizontal,
            _flipVertical,
            VideoFadeInBox.IsChecked == true,
            VideoFadeInSeconds.Value,
            VideoFadeOutBox.IsChecked == true,
            VideoFadeOutSeconds.Value,
            AudioFadeInBox.IsChecked == true,
            AudioFadeInSeconds.Value,
            AudioFadeOutBox.IsChecked == true,
            AudioFadeOutSeconds.Value);
    }

    private void Restore(EditorSnapshot snap)
    {
        _restoring = true;
        try
        {
            SpeedSlider.Value = snap.Speed;
            VolumeSlider.Value = snap.Volume;
            _trimStartMs = snap.TrimStartMs;
            _trimEndMs = snap.TrimEndMs;
            if (_durationMs > 0)
            {
                Playback.SetTrimFractions(snap.TrimStartMs / (double)_durationMs, Math.Max(snap.TrimStartMs + 1, snap.TrimEndMs) / (double)_durationMs);
            }

            _cuts.Clear();
            _cuts.AddRange(snap.Cuts);
            _markStartMs = snap.MarkStartMs;
            _markEndMs = snap.MarkEndMs;
            NormalizeCuts();
            UpdateCutMark();
            UpdateTrimSummary();
            _cropPreview = snap.CropPreview;
            CropSurface.SetFractions(snap.CropLeft, snap.CropTop, snap.CropRight, snap.CropBottom);
            if (PreviewCropButton is not null)
            {
                PreviewCropButton.Content = _cropPreview ? "Full frame" : "Preview";
            }

            _quarterTurns = snap.QuarterTurns;
            _flipHorizontal = snap.FlipHorizontal;
            _flipVertical = snap.FlipVertical;
            UpdateOrientationSummary();
            VideoFadeInBox.IsChecked = snap.VideoFadeIn;
            VideoFadeInSeconds.Value = snap.VideoFadeInSeconds;
            VideoFadeOutBox.IsChecked = snap.VideoFadeOut;
            VideoFadeOutSeconds.Value = snap.VideoFadeOutSeconds;
            AudioFadeInBox.IsChecked = snap.AudioFadeIn;
            AudioFadeInSeconds.Value = snap.AudioFadeInSeconds;
            AudioFadeOutBox.IsChecked = snap.AudioFadeOut;
            AudioFadeOutSeconds.Value = snap.AudioFadeOutSeconds;
            ApplyCropPreview();
            LayoutCrop();
            ApplyRate();
            ApplyVolume();
            UpdateSpeedText();
            if (SaveButton is not null)
            {
                SaveButton.IsEnabled = HasEdits && !_saving;
            }
        }
        finally
        {
            _restoring = false;
            UpdateHistoryButtons();
            UpdateToolMarks();
            UpdateCropChrome();
        }
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = _historyIndex > 0;
        RedoButton.IsEnabled = _historyIndex < _history.Count - 1;
    }

    private readonly record struct EditorSnapshot(
        double Speed,
        double Volume,
        long TrimStartMs,
        long TrimEndMs,
        (long StartMs, long EndMs)[] Cuts,
        long? MarkStartMs,
        long? MarkEndMs,
        double CropLeft,
        double CropTop,
        double CropRight,
        double CropBottom,
        bool CropPreview,
        int QuarterTurns,
        bool FlipHorizontal,
        bool FlipVertical,
        bool VideoFadeIn,
        double VideoFadeInSeconds,
        bool VideoFadeOut,
        double VideoFadeOutSeconds,
        bool AudioFadeIn,
        double AudioFadeInSeconds,
        bool AudioFadeOut,
        double AudioFadeOutSeconds);

    private void RotateLeft_Click(object sender, RoutedEventArgs e) => ChangeOrientation((_quarterTurns + 3) % 4, _flipHorizontal, _flipVertical);

    private void RotateRight_Click(object sender, RoutedEventArgs e) => ChangeOrientation((_quarterTurns + 1) % 4, _flipHorizontal, _flipVertical);

    private void FlipHorizontal_Click(object sender, RoutedEventArgs e) => ChangeOrientation(_quarterTurns, !_flipHorizontal, _flipVertical);

    private void FlipVertical_Click(object sender, RoutedEventArgs e) => ChangeOrientation(_quarterTurns, _flipHorizontal, !_flipVertical);

    private void ResetOrientation_Click(object sender, RoutedEventArgs e) => ChangeOrientation(0, false, false);

    private void ChangeOrientation(int quarterTurns, bool flipHorizontal, bool flipVertical)
    {
        _quarterTurns = quarterTurns;
        _flipHorizontal = flipHorizontal;
        _flipVertical = flipVertical;
        UpdateOrientationSummary();
        LayoutCrop();
        if (SaveButton is not null)
        {
            SaveButton.IsEnabled = HasEdits && !_saving;
        }

        NoteEdit();
    }

    private void UpdateOrientationSummary()
    {
        if (OrientationSummary is null)
        {
            return;
        }

        if (!HasOrientation)
        {
            OrientationSummary.Text = "Original.";
            return;
        }

        var parts = new List<string>();
        if (_quarterTurns != 0)
        {
            parts.Add($"{_quarterTurns * 90}° clockwise");
        }

        if (_flipHorizontal)
        {
            parts.Add("flipped horizontally");
        }

        if (_flipVertical)
        {
            parts.Add("flipped vertically");
        }

        OrientationSummary.Text = string.Join(", ", parts) + ".";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (HasEdits)
        {
            _pendingIsBack = true;
            _pendingPageType = null;
            LeavePrompt.Visibility = Visibility.Visible;
            return;
        }

        Leave();
    }

    private void LeavePromptStay_Click(object sender, RoutedEventArgs e)
    {
        _closeWindow = false;
        LeavePrompt.Visibility = Visibility.Collapsed;
    }

    private void LeavePromptConfirm_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
        _allowLeave = true;
        if (_closeWindow)
        {
            _closeWindow = false;
            App.MainAppWindow.Close();
            return;
        }

        if (_pendingIsBack && Frame.CanGoBack)
        {
            Frame.GoBack();
            return;
        }

        if (_pendingPageType is not null)
        {
            NavigationHelper.Follow(Frame, _pendingPageType, _pendingParameter, clearBackStack: false);
            return;
        }

        Leave();
    }

    private void Leave()
    {
        _allowLeave = true;
        ReleasePlayer();
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private static string Format(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private static bool TryParseClock(string text, out long milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':');
        double seconds;
        if (parts.Length == 1)
        {
            if (!double.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            {
                return false;
            }
        }
        else if (parts.Length is 2 or 3)
        {
            var secondPart = parts[^1].Replace(',', '.');
            if (!double.TryParse(secondPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
            {
                return false;
            }

            if (!int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            {
                return false;
            }

            var hours = 0;
            if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours))
            {
                return false;
            }

            seconds = hours * 3600d + minutes * 60d + secs;
        }
        else
        {
            return false;
        }

        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return false;
        }

        milliseconds = (long)Math.Round(seconds * 1000);
        return true;
    }

    private static string FormatFine(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        var tenths = time.Milliseconds / 100;
        var clock = time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
        return $"{clock}.{tenths}";
    }
}
