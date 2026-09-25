using System.Globalization;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Controls;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.Core.Models;
using PlaybackStore = PersonalMediaPlayer.App.Playback;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class VideoEditorPage : Page
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
    private bool _dragging;
    private bool _saving;
    private bool _allowLeave;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private bool _pendingIsBack;
    private bool _selectingCut;
    private string _timelineMode = "seek";
    private bool _trimReady;
    private long _trimStartMs;
    private long _trimEndMs;
    private long? _markStartMs;
    private long? _markEndMs;
    private readonly List<(long StartMs, long EndMs)> _cuts = [];

    public VideoEditorPage()
    {
        InitializeComponent();
        Playback.SpeedCombo.Visibility = Visibility.Collapsed;
        Playback.FullScreenButton.Visibility = Visibility.Collapsed;
        Playback.MuteButton.Visibility = Visibility.Collapsed;
        Playback.VolumeSlider.Visibility = Visibility.Collapsed;
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
            _videoView = new VideoView();
            _videoView.Initialized += VideoView_Initialized;
        }

        if (VideoHost.Child is null)
        {
            VideoHost.Child = _videoView;
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

    private bool HasEdits => HasSpeedChange || HasVolumeChange || HasCuts || HasTrim;

    private void VideoView_Initialized(object? sender, InitializedEventArgs e)
    {
        _swapChainOptions = e.SwapChainOptions;
        _libVlc = new LibVLC(enableDebugLogs: false, e.SwapChainOptions);
        _player = new VlcMediaPlayer(_libVlc);
        _player.LengthChanged += (_, args) => DispatcherQueue.TryEnqueue(() => SetDuration(args.Length));
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

        using var media = new Media(_libVlc, path, FromType.FromPath);
        media.Parse(MediaParseOptions.ParseLocal);
        SetDuration(media.Duration);
        _player.Play(media);
        ApplyRate();
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

        _libVlc?.Dispose();
        _libVlc = null;
        if (VideoHost.Child is not null)
        {
            VideoHost.Child = null;
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
            PlayFile(_path);
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

        var next = Math.Clamp(_player.Time + delta, 0, Math.Max(0, _durationMs));
        _player.Time = next;
        UpdateClock();
    }

    private void ApplyVolume()
    {
        var volume = (int)VolumeSlider.Value;
        if (_player is not null)
        {
            _player.Mute = volume <= 0;
            _player.Volume = volume;
        }
    }

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
        if (_dragging || _updatingSlider || _player is null || _durationMs <= 0)
        {
            return;
        }

        _player.Time = PlayableTime((long)(e.NewValue / 1000d * _durationMs));
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
        UpdatePlayIcon();
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
            DurationLabel.Text = "The new length appears when the video has opened.";
            return;
        }

        var next = (long)(KeptMs() / rate);
        DurationLabel.Text = $"Kept picture {Format(KeptMs())}. Saved file {Format(next)}.";
    }

    private void TimelineMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            _timelineMode = tag;
            ApplyTimelineMode();
        }
    }

    private void ApplyTimelineMode()
    {
        _selectingCut = _timelineMode == "cut";
        Playback.SetCutPicking(_selectingCut);
        Playback.SetTrimInteractive(_timelineMode == "trim");
        if (ModeSeekButton is null || ModeTrimButton is null || ModeCutButton is null || TimelineHint is null || SelectCutButton is null)
        {
            return;
        }

        ModeSeekButton.Style = _timelineMode == "seek" ? Accent() : null;
        ModeTrimButton.Style = _timelineMode == "trim" ? Accent() : null;
        ModeCutButton.Style = _timelineMode == "cut" ? Accent() : null;
        SelectCutButton.Content = _selectingCut ? "Timeline is selecting" : "Use the timeline";
        TimelineHint.Text = _timelineMode switch
        {
            "trim" => "Drag a white handle. Left is the first frame kept. Right is the last. Drag the middle to move inside that span.",
            "cut" => "Drag across the part to delete. A short click only moves the playhead.",
            _ => "Click or drag the line to move the playhead. The white marks show the kept span."
        };
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
            var row = new StackPanel { Spacing = 6 };
            var label = new TextBlock
            {
                Text = $"Removed {FormatFine(cut.EndMs - cut.StartMs)}: {FormatFine(cut.StartMs)} to {FormatFine(cut.EndMs)}",
                TextWrapping = TextWrapping.Wrap
            };
            var restore = new Button
            {
                Content = "Put back",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
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
            CutList.Children.Add(row);
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
        VolumeLabel.Text = volume <= 0 ? "Muted" : $"{volume}%";
        ApplyVolume();
        SaveButton.IsEnabled = HasEdits && !_saving;
    }

    private void VolumePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var volume))
        {
            VolumeSlider.Value = volume;
        }
    }

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
            if (HasCuts || HasTrim)
            {
                working = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(source));
                File.Copy(source, working, overwrite: true);
                cutFile = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(source) + ".sections.mp4");
                if (File.Exists(cutFile))
                {
                    File.Delete(cutFile);
                }

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

            if (HasSpeedChange || HasVolumeChange)
            {
                await VideoTrimmer.ChangeSpeedAsync(produced, temp, rate, VolumeSlider.Value / 100d);
            }
            else if (cutFile is not null)
            {
                temp = cutFile;
                cutFile = null;
            }

            if (cutFile is not null && !string.Equals(cutFile, temp, StringComparison.OrdinalIgnoreCase) && File.Exists(cutFile))
            {
                File.Delete(cutFile);
                cutFile = null;
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
            if (!Frame.Navigate(typeof(VideoPlayerPage), saved))
            {
                _allowLeave = false;
                throw new InvalidOperationException("The saved video could not be opened.");
            }

            var editor = Frame.BackStack.LastOrDefault();
            if (editor?.SourcePageType == typeof(VideoEditorPage))
            {
                Frame.BackStack.Remove(editor);
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

    private void LeavePromptStay_Click(object sender, RoutedEventArgs e) => LeavePrompt.Visibility = Visibility.Collapsed;

    private void LeavePromptConfirm_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
        _allowLeave = true;
        if (_pendingIsBack && Frame.CanGoBack)
        {
            Frame.GoBack();
            return;
        }

        if (_pendingPageType is not null)
        {
            Frame.Navigate(_pendingPageType, _pendingParameter);
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
