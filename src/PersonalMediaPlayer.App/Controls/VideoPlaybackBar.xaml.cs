using LibVLCSharp.Shared;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.Editing;
using Windows.Foundation;
using Windows.System;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class VideoPlaybackBar : UserControl
{
    private enum TrimHandle
    {
        None,
        Start,
        End,
        Seek
    }

    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly TimelineThumbnails _thumbs = new();
    private TrimHandle _trimDrag;
    private double _trimStart;
    private double _trimEnd = 1;
    private string? _hoverPath;
    private long _hoverDurationMs;
    private TimeSpan _wantedHover = TimeSpan.MinValue;
    private bool _hoverBusy;
    private bool _hoverDirty;
    private bool _hoverOpen;
    private bool _cutPicking;
    private bool _trimPassThrough;
    private double _trimGrab = 18;
    private bool _cutDragging;
    private double _cutAnchor;
    private List<(double Start, double End)> _removedFractions = [];
    private (double Start, double End)? _selection;
    private static readonly double[] ZoomSteps = [1, 2, 4, 8, 16];
    private int _zoomIndex;
    private MediaPlayer? _subtitlePlayer;
    private int _subtitleTrack = -1;
    private bool _subtitlesOn = true;
    private List<(int Id, string Name)>? _captionChoices;
    private int _announcedCaption = int.MinValue;
    private bool _timelineZoom;
    private bool _layingZoom;
    private bool _sectionRepeat;
    private double? _markA;
    private double? _markB;
    private bool _suppressSettingsOpen;
    private int _openLists;
    private bool _fillingChoices;

    public event EventHandler? SectionARequested;

    public event EventHandler? SectionBRequested;

    public event EventHandler? SectionClearRequested;

    public VideoPlaybackBar()
    {
        InitializeComponent();
        SpeedCombo.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdateSettingsButton());
        SettingsButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Settings_Pressed), true);
        SettingsButton.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Settings_Released), true);
        SettingsButton.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(Settings_Canceled), true);
        WatchCombo(SpeedCombo);
        WatchCombo(QualityCombo);
        WatchCombo(AudioCombo);
        WatchCombo(CaptionCombo);
        SettingsPopup.Closed += (_, _) =>
        {
            if (_openLists > 0)
            {
                SettingsPopup.IsOpen = true;
            }
        };
        UpdateSettingsButton();
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            _ = PumpHoverAsync();
        };
        Loaded += (_, _) =>
        {
            TimelineHost.AddHandler(PointerMovedEvent, new PointerEventHandler(Timeline_Moved), true);
            TimelineHost.AddHandler(PointerPressedEvent, new PointerEventHandler(Timeline_Pressed), true);
            TimelineHost.AddHandler(PointerReleasedEvent, new PointerEventHandler(Timeline_Released), true);
            TimelineHost.AddHandler(PointerExitedEvent, new PointerEventHandler(Timeline_Exited), true);
            TimelineHost.AddHandler(PointerCanceledEvent, new PointerEventHandler(Timeline_Released), true);
            TimelineHost.SizeChanged += (_, _) =>
            {
                DrawRemovedFractions();
                ArrangeRepeat();
            };
            TimelineScroll.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(TimelineScroll_Wheel), true);
        };
        Unloaded += (_, _) =>
        {
            _hoverTimer.Stop();
            _thumbs.Dispose();
            CloseSettings();
        };
    }

    private void Settings_Pressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressSettingsOpen = SettingsPopup.IsOpen;
    }

    private void Settings_Released(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(SettingsButton).Position;
        var onButton = point.X >= 0 && point.Y >= 0
            && point.X <= SettingsButton.ActualWidth
            && point.Y <= SettingsButton.ActualHeight;
        if (!onButton)
        {
            _suppressSettingsOpen = false;
        }
    }

    private void Settings_Canceled(object sender, PointerRoutedEventArgs e) => _suppressSettingsOpen = false;

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsOpen)
        {
            _suppressSettingsOpen = false;
            SettingsPopup.IsOpen = false;
            return;
        }

        if (SettingsPopup.IsOpen)
        {
            SettingsPopup.IsOpen = false;
            return;
        }

        OpenSettings();
    }

    public void CloseSettings()
    {
        _suppressSettingsOpen = false;
        _openLists = 0;
        SettingsPopup.IsLightDismissEnabled = true;
        SettingsPopup.IsOpen = false;
    }

    private void OpenSettings()
    {
        PlaceSettings();
        SettingsPopup.IsOpen = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (SettingsPopup.IsOpen)
            {
                PlaceSettings();
            }
        });
    }

    private void PlaceSettings()
    {
        SettingsCard.Measure(new Size(320, 800));
        var width = SettingsCard.ActualWidth > 1 ? SettingsCard.ActualWidth : SettingsCard.DesiredSize.Width;
        var height = SettingsCard.ActualHeight > 1 ? SettingsCard.ActualHeight : SettingsCard.DesiredSize.Height;
        if (width <= 1)
        {
            width = 300;
        }

        if (height <= 1)
        {
            height = 280;
        }

        var origin = SettingsButton.TransformToVisual(this).TransformPoint(new Point(0, 0));
        var buttonWidth = SettingsButton.ActualWidth > 1 ? SettingsButton.ActualWidth : 44;
        SettingsPopup.HorizontalOffset = Math.Max(0, origin.X + buttonWidth - width);
        SettingsPopup.VerticalOffset = origin.Y - height - 8;
    }

    private void WatchCombo(ComboBox combo)
    {
        combo.DropDownOpened += (_, _) =>
        {
            _openLists++;
            SettingsPopup.IsLightDismissEnabled = false;
        };
        combo.DropDownClosed += (_, _) =>
        {
            _openLists = Math.Max(0, _openLists - 1);
            if (_openLists == 0)
            {
                SettingsPopup.IsLightDismissEnabled = true;
            }
        };
    }

    private void UpdateSettingsButton()
    {
        var speed = SpeedCombo.Visibility == Visibility.Visible;
        var captions = SubtitleButton.Visibility == Visibility.Visible || CaptionCombo.Visibility == Visibility.Visible;
        var repeat = RepeatButtons.Visibility == Visibility.Visible;
        CaptionRow.Visibility = captions ? Visibility.Visible : Visibility.Collapsed;
        SpeedRow.Visibility = speed ? Visibility.Visible : Visibility.Collapsed;
        var quality = QualityRow.Visibility == Visibility.Visible;
        var audio = AudioRow.Visibility == Visibility.Visible;
        var available = speed || captions || repeat || quality || audio;
        SettingsButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (!available)
        {
            CloseSettings();
        }
    }

    public void UseSubtitles(MediaPlayer player)
    {
        _subtitlePlayer = player;
        ApplySubtitles();
    }

    public void ClearSubtitles()
    {
        _subtitlePlayer = null;
        _subtitleTrack = -1;
        _captionChoices = null;
        _announcedCaption = int.MinValue;
        SubtitleButton.Visibility = Visibility.Collapsed;
        UpdateSettingsButton();
    }

    private void Subtitle_Click(object sender, RoutedEventArgs e)
    {
        var tracks = SubtitleTracks();
        if (tracks.Count > 1)
        {
            var menu = new MenuFlyout();
            var off = new MenuFlyoutItem { Text = "Off" };
            off.Click += (_, _) =>
            {
                _subtitlesOn = false;
                ApplySubtitles();
            };
            menu.Items.Add(off);
            foreach (var track in tracks)
            {
                var item = new MenuFlyoutItem { Text = string.IsNullOrWhiteSpace(track.Name) ? "Subtitles" : track.Name };
                var id = track.Id;
                item.Click += (_, _) =>
                {
                    _subtitleTrack = id;
                    _subtitlesOn = true;
                    ApplySubtitles();
                };
                menu.Items.Add(item);
            }

            SettingsPopup.IsLightDismissEnabled = false;
            menu.Closed += (_, _) => SettingsPopup.IsLightDismissEnabled = true;
            menu.ShowAt(SubtitleButton);
            return;
        }

        _subtitlesOn = !_subtitlesOn;
        ApplySubtitles();
    }

    private void ApplySubtitles()
    {
        var tracks = SubtitleTracks();
        if (tracks.Count == 0 || (_subtitlePlayer is null && _captionChoices is null))
        {
            SubtitleButton.Visibility = Visibility.Collapsed;
            CaptionCombo.Visibility = Visibility.Collapsed;
            UpdateSettingsButton();
            return;
        }

        if (tracks.All(track => track.Id != _subtitleTrack))
        {
            _subtitleTrack = tracks[0].Id;
        }

        var selected = tracks.First(track => track.Id == _subtitleTrack);
        var name = string.IsNullOrWhiteSpace(selected.Name) ? "Subtitles" : selected.Name;
        if (_captionChoices is { Count: > 0 })
        {
            ShowCaptionCombo(tracks);
            SubtitleButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            CaptionCombo.Visibility = Visibility.Collapsed;
            SubtitleButton.Visibility = Visibility.Visible;
            SubtitleText.Text = _subtitlesOn ? "CC" : "Off";
            SubtitleButton.Opacity = _subtitlesOn ? 1 : 0.45;
            ToolTipService.SetToolTip(SubtitleButton, _subtitlesOn ? $"Hide {name}" : tracks.Count > 1 ? "Choose subtitles" : $"Show {name}");
        }

        _subtitlePlayer?.SetSpu(-1);
        SubtitlesChanged?.Invoke(this, _subtitlesOn);
        AnnounceCaption();
        UpdateSettingsButton();
    }

    public event EventHandler<int>? QualityChosen;

    public event EventHandler<int>? AudioChosen;

    public void OfferQualityChoices(IReadOnlyList<string> labels, int selectedIndex)
        => FillChoice(QualityCombo, QualityRow, labels, selectedIndex);

    public void OfferAudioChoices(IReadOnlyList<string> labels, int selectedIndex)
        => FillChoice(AudioCombo, AudioRow, labels, selectedIndex);

    public void ClearQualityChoices() => ClearChoice(QualityCombo, QualityRow);

    public void ClearAudioChoices() => ClearChoice(AudioCombo, AudioRow);

    public event EventHandler<bool>? SubtitlesChanged;

    public event EventHandler<int>? CaptionChosen;

    public void OfferCaptions()
    {
        _subtitlesOn = true;
        CaptionCombo.Visibility = Visibility.Collapsed;
        SubtitleButton.Visibility = Visibility.Visible;
        SubtitleText.Text = "CC";
        SubtitleButton.Opacity = 1;
        ToolTipService.SetToolTip(SubtitleButton, "Hide subtitles");
        SubtitlesChanged?.Invoke(this, true);
        UpdateSettingsButton();
    }

    public void ClearHoverCaptions()
    {
        _captionChoices = null;
        _announcedCaption = int.MinValue;
        // LibVLC faults if subtitle tracks are read after Stop, or on a player that was already released.
        _subtitlePlayer = null;
        ApplySubtitles();
    }

    public void OfferCaptionChoices(IReadOnlyList<string> names, int selectedIndex = 0, bool announce = true)
    {
        var choices = new List<(int Id, string Name)>();
        for (var index = 0; index < names.Count; index++)
        {
            var name = string.IsNullOrWhiteSpace(names[index]) ? "Subtitles" : names[index];
            choices.Add((index, name));
        }

        if (choices.Count == 0)
        {
            _captionChoices = null;
            _subtitleTrack = -1;
            _subtitlesOn = false;
        }
        else
        {
            _captionChoices = choices;
            _subtitlesOn = selectedIndex >= 0;
            _subtitleTrack = _subtitlesOn
                ? choices[Math.Clamp(selectedIndex, 0, choices.Count - 1)].Id
                : -1;
        }

        _announcedCaption = announce ? int.MinValue : _subtitlesOn ? _subtitleTrack : -1;
        ApplySubtitles();
    }

    private void AnnounceCaption()
    {
        if (_captionChoices is null)
        {
            return;
        }

        var chosen = _subtitlesOn ? _subtitleTrack : -1;
        if (chosen == _announcedCaption)
        {
            return;
        }

        _announcedCaption = chosen;
        CaptionChosen?.Invoke(this, chosen);
    }

    private List<(int Id, string Name)> SubtitleTracks()
    {
        if (_captionChoices is { Count: > 0 })
        {
            return _captionChoices;
        }

        if (_subtitlePlayer is null)
        {
            return [];
        }

        return _subtitlePlayer.SpuDescription.Where(track => track.Id >= 0).Select(track => (track.Id, track.Name ?? string.Empty)).ToList();
    }

    private void QualityCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingChoices || QualityCombo.SelectedIndex < 0)
        {
            return;
        }

        QualityChosen?.Invoke(this, QualityCombo.SelectedIndex);
    }

    private void AudioCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingChoices || AudioCombo.SelectedIndex < 0)
        {
            return;
        }

        AudioChosen?.Invoke(this, AudioCombo.SelectedIndex);
    }

    private void CaptionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingChoices || CaptionCombo.SelectedIndex < 0 || _captionChoices is null)
        {
            return;
        }

        if (CaptionCombo.SelectedIndex == 0)
        {
            _subtitlesOn = false;
        }
        else
        {
            var tracks = SubtitleTracks();
            var index = CaptionCombo.SelectedIndex - 1;
            if (index < 0 || index >= tracks.Count)
            {
                return;
            }

            _subtitleTrack = tracks[index].Id;
            _subtitlesOn = true;
        }

        ApplySubtitles();
    }

    private void ShowCaptionCombo(List<(int Id, string Name)> tracks)
    {
        var labels = new List<string> { "Off" };
        foreach (var track in tracks)
        {
            labels.Add(string.IsNullOrWhiteSpace(track.Name) ? "Subtitles" : track.Name);
        }

        var selected = 0;
        if (_subtitlesOn)
        {
            var index = tracks.FindIndex(track => track.Id == _subtitleTrack);
            selected = index < 0 ? 1 : index + 1;
        }

        if (SameLabels(CaptionCombo, labels) && CaptionCombo.SelectedIndex == selected && CaptionCombo.Visibility == Visibility.Visible)
        {
            return;
        }

        _fillingChoices = true;
        CaptionCombo.Items.Clear();
        foreach (var label in labels)
        {
            CaptionCombo.Items.Add(label);
        }

        CaptionCombo.SelectedIndex = selected;
        CaptionCombo.Visibility = Visibility.Visible;
        _fillingChoices = false;
    }

    private void FillChoice(ComboBox combo, StackPanel row, IReadOnlyList<string> labels, int selectedIndex)
    {
        if (labels.Count == 0)
        {
            ClearChoice(combo, row);
            return;
        }

        var selected = Math.Clamp(selectedIndex, 0, labels.Count - 1);
        if (SameLabels(combo, labels) && combo.SelectedIndex == selected && row.Visibility == Visibility.Visible)
        {
            return;
        }

        _fillingChoices = true;
        combo.Items.Clear();
        foreach (var label in labels)
        {
            combo.Items.Add(label);
        }

        combo.SelectedIndex = selected;
        row.Visibility = Visibility.Visible;
        _fillingChoices = false;
        UpdateSettingsButton();
    }

    private void ClearChoice(ComboBox combo, StackPanel row)
    {
        if (combo.Items.Count == 0 && row.Visibility == Visibility.Collapsed)
        {
            return;
        }

        _fillingChoices = true;
        combo.Items.Clear();
        row.Visibility = Visibility.Collapsed;
        _fillingChoices = false;
        UpdateSettingsButton();
    }

    private static bool SameLabels(ComboBox combo, IReadOnlyList<string> labels)
    {
        if (combo.Items.Count != labels.Count)
        {
            return false;
        }

        for (var i = 0; i < labels.Count; i++)
        {
            if (!string.Equals(combo.Items[i] as string, labels[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public void UseTimelineZoom(bool enabled)
    {
        _timelineZoom = enabled;
        ZoomBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled && _zoomIndex != 0)
        {
            _zoomIndex = 0;
            LayoutZoom(0);
        }

        UpdateZoomLabel();
    }

    public void SetHoverSource(string? path)
    {
        if (string.Equals(_hoverPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _hoverPath = string.IsNullOrWhiteSpace(path) ? null : path;
        _thumbs.Cancel();
        HideHover();
    }

    public void ReleaseHoverFile()
    {
        _hoverTimer.Stop();
        _hoverOpen = false;
        _thumbs.ReleaseFile();
        HideHover();
    }

    public void SetHoverDuration(long durationMs)
    {
        _hoverDurationMs = durationMs > 0 ? durationMs : 0;
        if (_hoverDurationMs == 0)
        {
            HideHover();
        }
    }

    public double TrimStart => _trimStart;

    public double TrimEnd => _trimEnd;

    public event EventHandler? TrimRangeChanged;

    public event EventHandler<double>? TrimSeekRequested;

    public event EventHandler<CutSelection>? CutSelected;

    public event EventHandler<double>? TimelineClicked;

    public readonly record struct CutSelection(double Start, double End);

    public void SetCutPicking(bool enabled)
    {
        _cutPicking = enabled;
        _cutDragging = false;
        SeekSlider.IsHitTestVisible = !enabled && (_trimPassThrough || TrimCanvas.Visibility != Visibility.Visible);
    }

    public void SetSelectionFraction(double? start, double? end)
    {
        _selection = start is double from && end is double to
            ? (Math.Min(from, to), Math.Max(from, to))
            : null;
        DrawRemovedFractions();
    }

    public void SetRemovedFractions(IReadOnlyList<(double Start, double End)> fractions)
    {
        _removedFractions = fractions.ToList();
        DrawRemovedFractions();
    }

    public void UseSectionRepeat(bool enabled)
    {
        _sectionRepeat = enabled;
        if (!enabled)
        {
            _markA = null;
            _markB = null;
        }

        UpdateRepeatChrome();
    }

    public void SetSectionPrompt(string hint, string startTip, string endTip)
    {
        RepeatHint.Text = hint;
        ToolTipService.SetToolTip(RepeatAButton, startTip);
        ToolTipService.SetToolTip(RepeatBButton, endTip);
    }

    public void SetSectionMarks(double? a, double? b)
    {
        _markA = a is double start ? Math.Clamp(start, 0, 1) : null;
        _markB = b is double end ? Math.Clamp(end, 0, 1) : null;
        if (_markA is null && _markB is null)
        {
            SetSectionPrompt("Repeat a section", "Mark where the repeat starts", "Mark where the repeat ends");
        }

        UpdateRepeatChrome();
    }

    private void RepeatA_Click(object sender, RoutedEventArgs e) => SectionARequested?.Invoke(this, EventArgs.Empty);

    private void RepeatB_Click(object sender, RoutedEventArgs e) => SectionBRequested?.Invoke(this, EventArgs.Empty);

    private void RepeatClear_Click(object sender, RoutedEventArgs e) => SectionClearRequested?.Invoke(this, EventArgs.Empty);

    private void UpdateRepeatChrome()
    {
        var trimming = TrimCanvas.Visibility == Visibility.Visible;
        RepeatButtons.Visibility = _sectionRepeat && !trimming ? Visibility.Visible : Visibility.Collapsed;
        RepeatClearButton.Visibility = _markA is not null || _markB is not null ? Visibility.Visible : Visibility.Collapsed;
        Style? accent = Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) ? style as Style : null;
        RepeatAButton.Style = _markA is null && _markB is not null ? accent : null;
        RepeatBButton.Style = _markB is null && _markA is not null ? accent : null;
        ArrangeRepeat();
        UpdateSettingsButton();
    }

    private void ArrangeRepeat()
    {
        var width = TimelineHost.ActualWidth;
        var trimming = TrimCanvas.Visibility == Visibility.Visible;
        if (!_sectionRepeat || trimming || width <= 1 || (_markA is null && _markB is null))
        {
            RepeatCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        RepeatCanvas.Visibility = Visibility.Visible;
        PlaceRepeatMark(RepeatMarkA, _markA, width);
        PlaceRepeatMark(RepeatMarkB, _markB, width);
        if (_markA is double start && _markB is double end)
        {
            var left = Math.Min(start, end) * width;
            var right = Math.Max(start, end) * width;
            Canvas.SetLeft(RepeatSpan, left);
            Canvas.SetTop(RepeatSpan, 12);
            RepeatSpan.Width = Math.Max(0, right - left);
            RepeatSpan.Visibility = Visibility.Visible;
        }
        else
        {
            RepeatSpan.Visibility = Visibility.Collapsed;
        }
    }

    private static void PlaceRepeatMark(FrameworkElement mark, double? fraction, double width)
    {
        if (fraction is not double value)
        {
            mark.Visibility = Visibility.Collapsed;
            return;
        }

        mark.Visibility = Visibility.Visible;
        Canvas.SetLeft(mark, Math.Clamp(value * width - 9, 0, Math.Max(0, width - 18)));
        Canvas.SetTop(mark, 3);
    }

    public void BeginTrim(bool passThrough = false)
    {
        _trimPassThrough = passThrough;
        _trimStart = 0;
        _trimEnd = 1;
        TrimCanvas.Background = passThrough ? null : new SolidColorBrush(Colors.Transparent);
        TrimCanvas.Visibility = Visibility.Visible;
        TrimCanvas.IsHitTestVisible = true;
        SeekSlider.IsHitTestVisible = passThrough && !_cutPicking;
        ArrangeTrim();
        UpdateRepeatChrome();
    }

    public void SetTrimInteractive(bool enabled)
    {
        _trimGrab = enabled ? 28 : 18;
        TrimStartThumb.Width = enabled ? 18 : 12;
        TrimEndThumb.Width = enabled ? 18 : 12;
        TrimStartThumb.Height = enabled ? 32 : 22;
        TrimEndThumb.Height = enabled ? 32 : 22;
        TrimCanvas.Background = enabled ? new SolidColorBrush(Colors.Transparent) : null;
        TrimCanvas.IsHitTestVisible = enabled && TrimCanvas.Visibility == Visibility.Visible;
        TrimStartThumb.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        TrimEndThumb.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
        {
            _trimDrag = TrimHandle.None;
        }

        SeekSlider.IsHitTestVisible = !enabled && !_cutPicking;
        ArrangeTrim();
    }

    public void SetTrimFractions(double start, double end)
    {
        const double minGap = 0.01;
        _trimStart = Math.Clamp(Math.Min(start, end - minGap), 0, 1);
        _trimEnd = Math.Clamp(Math.Max(end, _trimStart + minGap), 0, 1);
        ArrangeTrim();
    }

    public void EndTrim()
    {
        _trimDrag = TrimHandle.None;
        _trimPassThrough = false;
        TrimCanvas.Background = new SolidColorBrush(Colors.Transparent);
        TrimCanvas.Visibility = Visibility.Collapsed;
        TrimCanvas.IsHitTestVisible = false;
        SeekSlider.IsHitTestVisible = !_cutPicking;
        UpdateRepeatChrome();
    }

    private void Timeline_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_hoverDurationMs <= 0 || string.IsNullOrWhiteSpace(_hoverPath) || TimelineHost.ActualWidth <= 1)
        {
            HideHover();
            return;
        }

        if (_cutDragging && TimelineHost.ActualWidth > 1)
        {
            var dragEnd = CutFraction(e);
            _selection = (Math.Min(_cutAnchor, dragEnd), Math.Max(_cutAnchor, dragEnd));
            DrawRemovedFractions();
        }

        var x = Math.Clamp(e.GetCurrentPoint(TimelineHost).Position.X, 0, TimelineHost.ActualWidth);
        var ms = (long)(x / TimelineHost.ActualWidth * _hoverDurationMs);
        HoverTime.Text = FormatHover(ms);
        _wantedHover = TimeSpan.FromMilliseconds(ms);
        _hoverOpen = true;
        HoverCard.Visibility = Visibility.Visible;
        var cardWidth = HoverCard.ActualWidth > 1 ? HoverCard.ActualWidth : 168;
        var left = e.GetCurrentPoint(this).Position.X - cardWidth / 2;
        HoverCard.Margin = new Thickness(Math.Clamp(left, 0, Math.Max(0, ActualWidth - cardWidth)), -128, 0, 0);
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void Timeline_Exited(object sender, PointerRoutedEventArgs e) => HideHover();

    private void Timeline_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_trimDrag != TrimHandle.None || !_cutPicking || TimelineHost.ActualWidth <= 1)
        {
            return;
        }

        _cutDragging = true;
        _cutAnchor = CutFraction(e);
        TimelineHost.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Timeline_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_cutDragging)
        {
            return;
        }

        _cutDragging = false;
        TimelineHost.ReleasePointerCaptures();
        var end = CutFraction(e);
        var start = Math.Min(_cutAnchor, end);
        end = Math.Max(_cutAnchor, end);
        if (end - start < 0.008)
        {
            TimelineClicked?.Invoke(this, end);
        }
        else
        {
            CutSelected?.Invoke(this, new CutSelection(start, end));
        }

        e.Handled = true;
    }

    private double CutFraction(PointerRoutedEventArgs e)
        => Math.Clamp(e.GetCurrentPoint(TimelineHost).Position.X / Math.Max(1, TimelineHost.ActualWidth), 0, 1);

    private void DrawRemovedFractions()
    {
        CutsCanvas.Children.Clear();
        var width = TimelineHost.ActualWidth;
        var height = Math.Max(8, TimelineHost.ActualHeight);
        if (width <= 1)
        {
            return;
        }

        CutsCanvas.Width = width;
        CutsCanvas.Height = height;
        foreach (var (start, end) in _removedFractions)
        {
            AddSpan(start, end, width, height, 8, ColorHelper.FromArgb(210, 220, 48, 48));
        }

        if (_selection is { } selected)
        {
            AddSpan(selected.Start, selected.End, width, height, 16, ColorHelper.FromArgb(230, 255, 186, 46));
        }
    }

    private void AddSpan(double start, double end, double width, double height, double band, Windows.UI.Color color)
    {
        var mark = new Border
        {
            Width = Math.Max(2, (end - start) * width),
            Height = band,
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(mark, start * width);
        Canvas.SetTop(mark, (height - band) / 2);
        CutsCanvas.Children.Add(mark);
    }

    private void HideHover()
    {
        _hoverOpen = false;
        _hoverTimer.Stop();
        _thumbs.Cancel();
        HoverCard.Visibility = Visibility.Collapsed;
        HoverImage.Source = null;
    }

    private async Task PumpHoverAsync()
    {
        if (_hoverBusy)
        {
            _hoverDirty = true;
            return;
        }

        _hoverBusy = true;
        try
        {
            do
            {
                _hoverDirty = false;
                var path = _hoverPath;
                var time = _wantedHover;
                if (!_hoverOpen || path is null || time < TimeSpan.Zero)
                {
                    return;
                }

                try
                {
                    var bitmap = await _thumbs.GrabAsync(path, time);
                    if (_hoverOpen && bitmap is not null && time == _wantedHover)
                    {
                        HoverImage.Source = bitmap;
                    }
                }
                catch (Exception)
                {
                    if (_hoverOpen)
                    {
                        HoverImage.Source = null;
                    }
                }
            }
            while (_hoverDirty && _hoverOpen);
        }
        finally
        {
            _hoverBusy = false;
        }
    }

    private static string FormatHover(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => StepZoom(-1);

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => StepZoom(1);

    private void TimelineScroll_Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (!_timelineZoom || !e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            return;
        }

        e.Handled = true;
        StepZoom(e.GetCurrentPoint(TimelineScroll).Properties.MouseWheelDelta > 0 ? 1 : -1);
    }

    private void TimelineScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_layingZoom)
        {
            LayoutZoom(ViewCenterFraction());
        }
    }

    private void StepZoom(int direction)
    {
        var next = Math.Clamp(_zoomIndex + direction, 0, ZoomSteps.Length - 1);
        if (next == _zoomIndex)
        {
            return;
        }

        _zoomIndex = next;
        LayoutZoom(ViewCenterFraction());
    }

    private double ViewCenterFraction()
    {
        var width = TimelineHost.ActualWidth;
        var viewport = TimelineScroll.ViewportWidth;
        if (width <= 1 || viewport <= 1)
        {
            return 0;
        }

        return Math.Clamp((TimelineScroll.HorizontalOffset + viewport / 2) / width, 0, 1);
    }

    private void LayoutZoom(double anchorFraction)
    {
        var viewport = TimelineScroll.ViewportWidth;
        if (viewport <= 1 || _layingZoom)
        {
            UpdateZoomLabel();
            return;
        }

        _layingZoom = true;
        try
        {
            var zoom = ZoomSteps[_zoomIndex];
            var width = Math.Max(viewport, viewport * zoom);
            TimelineHost.Width = width;
            UpdateZoomLabel();
            TimelineHost.UpdateLayout();
            var offset = anchorFraction * width - viewport / 2;
            TimelineScroll.ChangeView(Math.Clamp(offset, 0, Math.Max(0, width - viewport)), null, null, true);
            DrawRemovedFractions();
            ArrangeTrim();
        }
        finally
        {
            _layingZoom = false;
        }
    }

    private void UpdateZoomLabel()
    {
        if (ZoomLabel is null)
        {
            return;
        }

        ZoomLabel.Text = $"{ZoomSteps[_zoomIndex]:0}×";
        ZoomOutButton.IsEnabled = _zoomIndex > 0;
        ZoomInButton.IsEnabled = _zoomIndex < ZoomSteps.Length - 1;
    }

    private void TrimCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => ArrangeTrim();

    private void Trim_Pressed(object sender, PointerRoutedEventArgs e)
    {
        var width = TrimCanvas.ActualWidth;
        if (width <= 1)
        {
            return;
        }

        var x = e.GetCurrentPoint(TrimCanvas).Position.X;
        var startX = _trimStart * width;
        var endX = _trimEnd * width;
        _trimDrag = Math.Abs(x - startX) <= _trimGrab
            ? TrimHandle.Start
            : Math.Abs(x - endX) <= _trimGrab
                ? TrimHandle.End
                : TrimHandle.Seek;
        TrimCanvas.CapturePointer(e.Pointer);
        ApplyTrimDrag(x);
        e.Handled = true;
    }

    private void Trim_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_trimDrag == TrimHandle.None)
        {
            return;
        }

        ApplyTrimDrag(e.GetCurrentPoint(TrimCanvas).Position.X);
        e.Handled = true;
    }

    private void Trim_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_trimDrag == TrimHandle.None)
        {
            return;
        }

        _trimDrag = TrimHandle.None;
        TrimCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void ApplyTrimDrag(double x)
    {
        var width = Math.Max(1, TrimCanvas.ActualWidth);
        var fraction = Math.Clamp(x / width, 0, 1);
        const double minGap = 0.01;
        if (_trimDrag == TrimHandle.Start)
        {
            _trimStart = Math.Min(fraction, _trimEnd - minGap);
        }
        else if (_trimDrag == TrimHandle.End)
        {
            _trimEnd = Math.Max(fraction, _trimStart + minGap);
        }
        else if (_trimDrag == TrimHandle.Seek)
        {
            var inside = Math.Clamp(fraction, _trimStart, _trimEnd);
            TrimSeekRequested?.Invoke(this, inside);
            return;
        }

        _trimStart = Math.Clamp(_trimStart, 0, 1);
        _trimEnd = Math.Clamp(_trimEnd, 0, 1);
        ArrangeTrim();
        TrimRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ArrangeTrim()
    {
        var width = TrimCanvas.ActualWidth;
        if (width <= 1)
        {
            return;
        }

        var startX = _trimStart * width;
        var endX = _trimEnd * width;
        Canvas.SetLeft(TrimRange, startX);
        Canvas.SetTop(TrimRange, 12);
        TrimRange.Width = Math.Max(0, endX - startX);
        Canvas.SetLeft(TrimStartThumb, Math.Clamp(startX - 6, 0, width - 12));
        Canvas.SetTop(TrimStartThumb, 3);
        Canvas.SetLeft(TrimEndThumb, Math.Clamp(endX - 6, 0, width - 12));
        Canvas.SetTop(TrimEndThumb, 3);
    }
}
