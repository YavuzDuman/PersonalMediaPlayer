using System.Runtime.InteropServices.WindowsRuntime;
using LibVLCSharp.Shared;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Text;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;
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
    private readonly DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private readonly TimelineThumbnails _thumbs = new();
    private TrimHandle _trimDrag;
    private double _trimStart;
    private double _trimEnd = 1;
    private string? _hoverPath;
    private AudioWaveform.Levels _waveLevels = AudioWaveform.Levels.Empty;
    private AudioWaveform.WaveformKey _waveKey;
    private int _waveSerial;
    private bool _waveLoading;
    private bool _waveAttempted;
    private bool _waveQueued;
    private readonly List<(long StartMs, long EndMs, string Text)> _captionCues = [];
    private int _captionTip = -1;
    private long _hoverDurationMs;
    private TimeSpan _wantedHover = TimeSpan.MinValue;
    private bool _hoverBusy;
    private bool _hoverDirty;
    private bool _hoverOpen;
    private bool _cutPicking;
    private bool _trimPassThrough;
    private double _trimGrab = 18;
    private bool _cutDragging;
    private bool _audioDragging;
    private bool _seekDragging;
    private bool _holdView;
    private double _edgePointerX = double.NaN;
    private FrameworkElement? _seekThumb;
    private bool _audioInteractive = true;
    private double _audioAnchor;
    private List<(double Start, double End, int Volume)> _silenceFractions = [];
    private (double Start, double End)? _audioSelection;
    private double _cutAnchor;
    private List<(double Start, double End)> _removedFractions = [];
    private (double Start, double End)? _selection;
    private int _zoomIndex;
    private bool _timelineHovered;
    private MediaPlayer? _subtitlePlayer;
    private int _subtitleTrack = -1;
    private bool _subtitlesOn = true;
    private bool _fileCaptions;
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
    private bool _fillingLook;
    private bool _sliderDriving;
    private bool _lookHooked;
    private bool _soundOffered;
    private bool _fillingSound;
    private bool _soundHooked;
    private SettingsSection _settingsSection;
    private readonly DispatcherTimer _soundApplyTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };

    private enum SettingsSection
    {
        Home,
        Look,
        Sound
    }

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
        WatchCombo(CaptionSizeCombo);
        WatchCombo(CaptionColorCombo);
        WatchCombo(CaptionPlateCombo);
        WatchCombo(CaptionSpotCombo);
        DelaySlider.ValueChanged += DelaySlider_Changed;
        CaptionSizeCombo.SelectionChanged += Look_Changed;
        CaptionColorCombo.SelectionChanged += Look_Changed;
        CaptionPlateCombo.SelectionChanged += Look_Changed;
        CaptionSpotCombo.SelectionChanged += Look_Changed;
        DelaySlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(DelaySlider_Released), true);
        DelaySlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(DelaySlider_Released), true);
        VolumeSlider.ValueChanged += (_, e) => VolumeReadout.Text = ((int)Math.Round(e.NewValue)).ToString() + "%";
        HookLook();
        HookSound();
        BassSlider.ValueChanged += CustomSlider_Changed;
        VoiceSlider.ValueChanged += CustomSlider_Changed;
        TrebleSlider.ValueChanged += CustomSlider_Changed;
        BassSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(CustomSlider_Released), true);
        BassSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(CustomSlider_Released), true);
        VoiceSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(CustomSlider_Released), true);
        VoiceSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(CustomSlider_Released), true);
        TrebleSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(CustomSlider_Released), true);
        TrebleSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(CustomSlider_Released), true);
        NormalizeSwitch.Toggled += Normalize_Toggled;
        _soundApplyTimer.Tick += (_, _) =>
        {
            _soundApplyTimer.Stop();
            PlaybackSound.Commit();
        };
        SettingsPopup.Opened += (_, _) => NotifyChromeHold();
        SettingsPopup.Closed += (_, _) =>
        {
            if (_openLists > 0)
            {
                SettingsPopup.IsOpen = true;
                return;
            }

            ShowSettingsHome();
            NotifyChromeHold();
        };
        UpdateSettingsButton();
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            _ = PumpHoverAsync();
        };
        _edgeTimer.Tick += (_, _) => AdvanceEdgeScroll();
        Loaded += (_, _) =>
        {
            HookLook();
            TimelineHost.AddHandler(PointerMovedEvent, new PointerEventHandler(Timeline_Moved), true);
            TimelineHost.AddHandler(PointerPressedEvent, new PointerEventHandler(Timeline_Pressed), true);
            TimelineHost.AddHandler(PointerReleasedEvent, new PointerEventHandler(Timeline_Released), true);
            TimelineHost.AddHandler(PointerExitedEvent, new PointerEventHandler(Timeline_Exited), true);
            TimelineHost.AddHandler(PointerCanceledEvent, new PointerEventHandler(Timeline_Released), true);
            TimelineHost.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Timeline_Released), true);
            TimelineHost.SizeChanged += (_, _) =>
            {
                DrawRemovedFractions();
                DrawAudioLane();
                DrawCaptions();
                ArrangeRepeat();
                UpdateSeekPrecision();
            };
            CaptionLane.SizeChanged += (_, _) => DrawCaptions();
            TimelineScroll.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(TimelineScroll_Wheel), true);
            TimelineScroll.AddHandler(PointerEnteredEvent, new PointerEventHandler(TimelineScroll_Entered), true);
            TimelineScroll.AddHandler(PointerExitedEvent, new PointerEventHandler(TimelineScroll_Exited), true);
            SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(Seek_EdgePressed), true);
            SeekSlider.AddHandler(PointerMovedEvent, new PointerEventHandler(Seek_EdgeMoved), true);
            SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(Seek_EdgeReleased), true);
            SeekSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(Seek_EdgeReleased), true);
            SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Seek_EdgeReleased), true);
        };
        Unloaded += (_, _) =>
        {
            _hoverTimer.Stop();
            _edgeTimer.Stop();
            _thumbs.Dispose();
            CloseSettings();
            _soundApplyTimer.Stop();
            if (_lookHooked)
            {
                SubtitleLook.Changed -= LookChanged;
                _lookHooked = false;
            }

            if (_soundHooked)
            {
                PlaybackSound.Changed -= SoundChanged;
                _soundHooked = false;
            }
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

    public bool KeepsChromeOpen => SettingsPopup.IsOpen || _openLists > 0 || TimeEntryOpen;

    public bool TimeEntryOpen => PositionBox.Visibility == Visibility.Visible;

    public event EventHandler<string>? TimeSubmitted;

    private bool _allowTimeEntry;

    public bool AllowTimeEntry
    {
        get => _allowTimeEntry;
        set
        {
            if (_allowTimeEntry == value)
            {
                return;
            }

            _allowTimeEntry = value;
            ToolTipService.SetToolTip(PositionHit, value ? "Type a time" : null);
            if (!value && TimeEntryOpen)
            {
                EndTimeEntry();
            }
        }
    }

    public void BeginTimeEntry()
    {
        if (!_allowTimeEntry || TimeEntryOpen)
        {
            return;
        }

        TimeMessage.Visibility = Visibility.Collapsed;
        PositionBox.Text = PositionText.Text;
        PositionHit.Visibility = Visibility.Collapsed;
        PositionBox.Visibility = Visibility.Visible;
        NotifyChromeHold();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!TimeEntryOpen)
            {
                return;
            }

            PositionBox.Focus(FocusState.Programmatic);
            PositionBox.SelectAll();
        });
    }

    public void SelectTimeEntry()
    {
        if (!TimeEntryOpen)
        {
            return;
        }

        PositionBox.Focus(FocusState.Programmatic);
        PositionBox.SelectAll();
    }

    public void ShowTimeMessage(string message)
    {
        TimeMessage.Text = message;
        TimeMessage.Visibility = Visibility.Visible;
    }

    public void EndTimeEntry()
    {
        if (!TimeEntryOpen && TimeMessage.Visibility == Visibility.Collapsed)
        {
            return;
        }

        PositionBox.Visibility = Visibility.Collapsed;
        PositionHit.Visibility = Visibility.Visible;
        TimeMessage.Visibility = Visibility.Collapsed;
        NotifyChromeHold();
    }

    private void Position_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!_allowTimeEntry)
        {
            return;
        }

        e.Handled = true;
        BeginTimeEntry();
    }

    private void PositionBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            TimeSubmitted?.Invoke(this, PositionBox.Text);
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            EndTimeEntry();
        }
    }

    private void PositionBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!TimeEntryOpen)
        {
            return;
        }

        EndTimeEntry();
    }

    public event EventHandler? ChromeHoldChanged;

    private void NotifyChromeHold() => ChromeHoldChanged?.Invoke(this, EventArgs.Empty);

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
        SettingsScroll.MaxHeight = double.PositiveInfinity;
        SettingsCard.Measure(new Size(360, 4000));
        var height = SettingsCard.DesiredSize.Height;
        if (double.IsNaN(height) || double.IsInfinity(height) || height > 520)
        {
            SettingsScroll.MaxHeight = 520;
            SettingsCard.Measure(new Size(360, 560));
            height = SettingsCard.DesiredSize.Height;
        }

        var width = SettingsCard.ActualWidth > 1 ? SettingsCard.ActualWidth : SettingsCard.DesiredSize.Width;
        if (width <= 1)
        {
            width = 360;
        }

        if (double.IsNaN(height) || height <= 1)
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
            NotifyChromeHold();
        };
        combo.DropDownClosed += (_, _) =>
        {
            _openLists = Math.Max(0, _openLists - 1);
            if (_openLists == 0)
            {
                SettingsPopup.IsLightDismissEnabled = true;
            }

            NotifyChromeHold();
        };
    }

    private void UpdateSettingsButton()
    {
        var speed = SpeedCombo.Visibility == Visibility.Visible;
        var captions = SubtitleButton.Visibility == Visibility.Visible || CaptionCombo.Visibility == Visibility.Visible;
        var repeat = RepeatButtons.Visibility == Visibility.Visible;
        CaptionRow.Visibility = captions ? Visibility.Visible : Visibility.Collapsed;
        OpenLookButton.Visibility = captions ? Visibility.Visible : Visibility.Collapsed;
        if (!captions && _settingsSection == SettingsSection.Look)
        {
            ShowSettingsHome();
        }
        SpeedRow.Visibility = speed ? Visibility.Visible : Visibility.Collapsed;
        var quality = QualityRow.Visibility == Visibility.Visible;
        var audio = AudioRow.Visibility == Visibility.Visible;
        var available = speed || captions || repeat || quality || audio;
        SettingsButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (!available)
        {
            CloseSettings();
        }
        else if (SettingsPopup.IsOpen)
        {
            PlaceSettings();
        }
    }

    private void HookLook()
    {
        if (_lookHooked)
        {
            return;
        }

        SubtitleLook.Changed += LookChanged;
        _lookHooked = true;
        ShowLook();
    }

    private void LookChanged()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(LookChanged);
            return;
        }

        ShowLook();
    }

    private void ShowLook()
    {
        var style = SubtitleLook.Current;
        _fillingLook = true;
        if (!_sliderDriving)
        {
            DelaySlider.Value = SubtitleLook.Seconds(style.DelayMs);
        }

        DelayText.Text = SubtitleLook.DelayLabel(style.DelayMs);
        CaptionSizeCombo.SelectedIndex = (int)style.Size;
        CaptionColorCombo.SelectedIndex = (int)style.Color;
        CaptionPlateCombo.SelectedIndex = (int)style.Plate;
        CaptionSpotCombo.SelectedIndex = (int)style.Spot;
        _fillingLook = false;
    }

    private void Look_Changed(object sender, SelectionChangedEventArgs e) => CommitLook();

    private void DelaySlider_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_fillingLook)
        {
            return;
        }

        _sliderDriving = true;
        try
        {
            CommitLook();
        }
        finally
        {
            _sliderDriving = false;
        }
    }

    private void DelaySlider_Released(object sender, PointerRoutedEventArgs e) => SnapDelaySlider();

    private void SnapDelaySlider()
    {
        var seconds = SubtitleLook.Seconds(SubtitleLook.Current.DelayMs);
        if (Math.Abs(DelaySlider.Value - seconds) < 0.001)
        {
            return;
        }

        _fillingLook = true;
        DelaySlider.Value = seconds;
        _fillingLook = false;
        DelayText.Text = SubtitleLook.DelayLabel(SubtitleLook.Current.DelayMs);
    }

    private void CommitLook()
    {
        if (_fillingLook
            || CaptionSizeCombo.SelectedIndex < 0
            || CaptionColorCombo.SelectedIndex < 0
            || CaptionPlateCombo.SelectedIndex < 0
            || CaptionSpotCombo.SelectedIndex < 0)
        {
            return;
        }

        var style = new SubtitleStyle(
            SubtitleLook.FromSeconds(DelaySlider.Value),
            (SubtitleSize)CaptionSizeCombo.SelectedIndex,
            (SubtitleTextColor)CaptionColorCombo.SelectedIndex,
            (SubtitlePlate)CaptionPlateCombo.SelectedIndex,
            (SubtitleSpot)CaptionSpotCombo.SelectedIndex);
        SubtitleLook.Save(style);
        DelayText.Text = SubtitleLook.DelayLabel(style.DelayMs);
    }

    private void DelayEarlier_Click(object sender, RoutedEventArgs e)
    {
        var current = SubtitleLook.Current;
        SubtitleLook.Save(current with { DelayMs = SubtitleLook.Step(current.DelayMs, -SubtitleLook.StepMs) });
    }

    private void DelayLater_Click(object sender, RoutedEventArgs e)
    {
        var current = SubtitleLook.Current;
        SubtitleLook.Save(current with { DelayMs = SubtitleLook.Step(current.DelayMs, SubtitleLook.StepMs) });
    }

    private void ResetLook_Click(object sender, RoutedEventArgs e) => SubtitleLook.Reset();

    public void OfferSound()
    {
        _soundOffered = true;
        OpenSoundButton.Visibility = Visibility.Visible;
        if (SettingsPopup.IsOpen)
        {
            PlaceSettings();
        }
    }

    private void OpenLook_Click(object sender, RoutedEventArgs e)
    {
        if (OpenLookButton.Visibility != Visibility.Visible)
        {
            return;
        }

        ShowLookPage();
    }

    private void OpenSound_Click(object sender, RoutedEventArgs e)
    {
        if (!_soundOffered)
        {
            return;
        }

        ShowSoundPage();
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e) => ShowSettingsHome(place: true);

    private void ShowSettingsHome(bool place = false)
    {
        _settingsSection = SettingsSection.Home;
        SettingsBackButton.Visibility = Visibility.Collapsed;
        SettingsTitle.Text = "Playback";
        SettingsHome.Visibility = Visibility.Visible;
        LookPage.Visibility = Visibility.Collapsed;
        SoundPage.Visibility = Visibility.Collapsed;
        if (place && SettingsPopup.IsOpen)
        {
            PlaceSettings();
        }
    }

    private void ShowLookPage()
    {
        _settingsSection = SettingsSection.Look;
        SettingsBackButton.Visibility = Visibility.Visible;
        SettingsTitle.Text = "Subtitles";
        SettingsHome.Visibility = Visibility.Collapsed;
        LookPage.Visibility = Visibility.Visible;
        SoundPage.Visibility = Visibility.Collapsed;
        if (SettingsPopup.IsOpen)
        {
            PlaceSettings();
        }
    }

    private void ShowSoundPage()
    {
        _settingsSection = SettingsSection.Sound;
        SettingsBackButton.Visibility = Visibility.Visible;
        SettingsTitle.Text = "Sound";
        SettingsHome.Visibility = Visibility.Collapsed;
        LookPage.Visibility = Visibility.Collapsed;
        SoundPage.Visibility = Visibility.Visible;
        ShowSound();
        if (SettingsPopup.IsOpen)
        {
            PlaceSettings();
        }
    }

    private void HookSound()
    {
        if (_soundHooked)
        {
            return;
        }

        PlaybackSound.Changed += SoundChanged;
        _soundHooked = true;
        ShowSound();
    }

    private void SoundChanged()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(SoundChanged);
            return;
        }

        ShowSound();
    }

    private void ShowSound()
    {
        var sound = PlaybackSound.Current;
        _fillingSound = true;
        BassSlider.Value = sound.Bass;
        VoiceSlider.Value = sound.Voice;
        TrebleSlider.Value = sound.Treble;
        NormalizeSwitch.IsOn = sound.Normalize;
        _fillingSound = false;
        BassGainText.Text = "Bass  " + PlaybackSound.GainLabel(sound.Bass);
        VoiceGainText.Text = "Voice  " + PlaybackSound.GainLabel(sound.Voice);
        TrebleGainText.Text = "Treble  " + PlaybackSound.GainLabel(sound.Treble);
        CustomBands.Visibility = sound.Preset == SoundPreset.Custom ? Visibility.Visible : Visibility.Collapsed;
        MarkPreset(SoundOffButton, sound.Preset == SoundPreset.Flat);
        MarkPreset(SoundBassButton, sound.Preset == SoundPreset.Bass);
        MarkPreset(SoundVoiceButton, sound.Preset == SoundPreset.Voice);
        MarkPreset(SoundTrebleButton, sound.Preset == SoundPreset.Treble);
        MarkPreset(SoundCustomButton, sound.Preset == SoundPreset.Custom);
        if (SettingsPopup.IsOpen && _settingsSection == SettingsSection.Sound)
        {
            PlaceSettings();
        }
    }

    private static void MarkPreset(Button button, bool selected)
    {
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        if (resources.TryGetValue(selected ? "AccentFillColorDefaultBrush" : "SubtleFillColorSecondaryBrush", out var fill)
            && fill is Brush fillBrush)
        {
            button.Background = fillBrush;
        }

        if (resources.TryGetValue(selected ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush", out var text)
            && text is Brush textBrush)
        {
            button.Foreground = textBrush;
        }
    }

    private void SoundPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag || !Enum.TryParse(tag, out SoundPreset preset) || !Enum.IsDefined(preset))
        {
            return;
        }

        _soundApplyTimer.Stop();
        PlaybackSound.Save(PlaybackSound.Current with { Preset = preset }, apply: true);
    }

    private void CustomSlider_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_fillingSound)
        {
            return;
        }

        StageCustom();
        _soundApplyTimer.Stop();
        _soundApplyTimer.Start();
    }

    private void CustomSlider_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_fillingSound)
        {
            return;
        }

        _soundApplyTimer.Stop();
        PlaybackSound.Commit();
    }

    private void StageCustom()
    {
        var state = PlaybackSound.Current with
        {
            Preset = SoundPreset.Custom,
            Bass = (int)Math.Round(BassSlider.Value),
            Voice = (int)Math.Round(VoiceSlider.Value),
            Treble = (int)Math.Round(TrebleSlider.Value)
        };
        PlaybackSound.Save(state, apply: false);
        BassGainText.Text = "Bass  " + PlaybackSound.GainLabel(state.Bass);
        VoiceGainText.Text = "Voice  " + PlaybackSound.GainLabel(state.Voice);
        TrebleGainText.Text = "Treble  " + PlaybackSound.GainLabel(state.Treble);
    }

    private void Normalize_Toggled(object sender, RoutedEventArgs e)
    {
        if (_fillingSound)
        {
            return;
        }

        _soundApplyTimer.Stop();
        PlaybackSound.Save(PlaybackSound.Current with { Normalize = NormalizeSwitch.IsOn }, apply: true);
    }

    private void ResetSound_Click(object sender, RoutedEventArgs e)
    {
        _soundApplyTimer.Stop();
        PlaybackSound.Reset();
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
        _fileCaptions = false;
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
        if (tracks.Count == 0)
        {
            ShowFileCaptionToggle();
            return;
        }

        if (_subtitlePlayer is null && _captionChoices is null)
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

    public event EventHandler? CaptionExportRequested;

    public void ShowCaptionExport(bool available)
    {
        ExportCaptionsButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExportCaptions_Click(object sender, RoutedEventArgs e)
    {
        CaptionExportRequested?.Invoke(this, EventArgs.Empty);
    }

    public void OfferCaptions()
    {
        _fileCaptions = true;
        _subtitlesOn = true;
        ShowFileCaptionToggle();
    }

    public void ClearHoverCaptions()
    {
        ShowCaptionExport(false);
        _captionChoices = null;
        _fileCaptions = false;
        _announcedCaption = int.MinValue;
        // LibVLC faults if subtitle tracks are read after Stop, or on a player that was already released.
        _subtitlePlayer = null;
        ApplySubtitles();
    }

    private void ShowFileCaptionToggle()
    {
        if (!_fileCaptions)
        {
            SubtitleButton.Visibility = Visibility.Collapsed;
            CaptionCombo.Visibility = Visibility.Collapsed;
            UpdateSettingsButton();
            return;
        }

        CaptionCombo.Visibility = Visibility.Collapsed;
        SubtitleButton.Visibility = Visibility.Visible;
        SubtitleText.Text = _subtitlesOn ? "CC" : "Off";
        SubtitleButton.Opacity = _subtitlesOn ? 1 : 0.45;
        ToolTipService.SetToolTip(SubtitleButton, _subtitlesOn ? "Hide subtitles" : "Show subtitles");
        _subtitlePlayer?.SetSpu(-1);
        SubtitlesChanged?.Invoke(this, _subtitlesOn);
        UpdateSettingsButton();
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
        var next = string.IsNullOrWhiteSpace(path) ? null : path;
        var key = next is null ? default : AudioWaveform.WaveformKey.For(next);
        var same = string.Equals(_hoverPath, next, StringComparison.OrdinalIgnoreCase) && key.Equals(_waveKey);
        if (string.Equals(_hoverPath, next, StringComparison.OrdinalIgnoreCase))
        {
            if (same && (_waveLevels.HasPeaks || _waveLoading || _waveAttempted))
            {
                UpdatePreviewCache();
                return;
            }

            RequestWaveform();
            UpdatePreviewCache();
            return;
        }

        _hoverPath = next;
        _thumbs.Cancel();
        HideHover();
        _waveSerial++;
        _waveLoading = false;
        _waveAttempted = false;
        _waveLevels = AudioWaveform.Levels.Empty;
        // The duration still belongs to the previous file until SetHoverDuration.
        TimelinePreviewCache.Cancel();
        RequestWaveform();
    }

    public void ReleaseHoverFile()
    {
        _hoverTimer.Stop();
        _hoverOpen = false;
        _thumbs.ReleaseFile();
        TimelinePreviewCache.Cancel();
        HideHover();
    }

    public void SetHoverDuration(long durationMs)
    {
        _hoverDurationMs = durationMs > 0 ? durationMs : 0;
        if (_hoverDurationMs == 0)
        {
            HideHover();
        }

        UpdatePreviewCache();
        UpdateCaptionLane();
        DrawAudioLane();
    }

    private void UpdatePreviewCache()
    {
        if (string.IsNullOrWhiteSpace(_hoverPath) || _hoverDurationMs <= 0)
        {
            TimelinePreviewCache.Cancel();
            return;
        }

        TimelinePreviewCache.Ensure(_hoverPath, _hoverDurationMs / 1000d);
    }

    public double TrimStart => _trimStart;

    public double TrimEnd => _trimEnd;

    public event EventHandler? TrimRangeChanged;

    public event EventHandler<double>? TrimSeekRequested;

    public event EventHandler<CutSelection>? CutSelected;

    public event EventHandler<CutSelection>? AudioRangeSelected;

    public event EventHandler<double>? AudioSilenceClicked;

    public event EventHandler<double>? TimelineClicked;

    public event EventHandler<long>? CaptionLineChosen;

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
        DrawCaptions();
    }

    public void ShowAudioLane(bool show)
    {
        var visible = show ? Visibility.Visible : Visibility.Collapsed;
        AudioLane.Visibility = visible;
        AudioLaneLabel.Visibility = visible;
        AudioHint.Visibility = visible;
        FitTimeline();
        UpdatePartVolumeVisibility();
        if (!show)
        {
            _waveSerial++;
            _waveLoading = false;
        }

        DrawAudioLane();
        if (show)
        {
            RequestWaveform();
        }
    }

    public void SetAudioLaneInteractive(bool enabled, string? hint)
    {
        _audioInteractive = enabled;
        AudioLane.IsHitTestVisible = enabled && AudioLane.Visibility == Visibility.Visible;
        AudioLane.Opacity = enabled ? 1 : 0.55;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            AudioHint.Text = hint;
        }

        UpdatePartVolumeVisibility();
        if (!enabled)
        {
            _waveSerial++;
            _waveLoading = false;
            _waveAttempted = false;
            _waveLevels = AudioWaveform.Levels.Empty;
        }

        DrawAudioLane();
        if (enabled)
        {
            RequestWaveform();
        }
    }

    public int PartVolumePercent => PartVolumeSlider is null ? 0 : (int)Math.Round(PartVolumeSlider.Value);

    public event EventHandler<int>? PartVolumeChanged;

    private bool _settingPartVolume;

    public void SetPartVolumePercent(int volume)
    {
        if (PartVolumeSlider is null)
        {
            return;
        }

        volume = Math.Clamp(volume, 0, 200);
        _settingPartVolume = true;
        try
        {
            PartVolumeSlider.Value = volume;
            if (PartVolumeReadout is not null)
            {
                PartVolumeReadout.Text = $"{volume}%";
            }
        }
        finally
        {
            _settingPartVolume = false;
        }
    }

    private void UpdatePartVolumeVisibility()
    {
        PartVolumeBar.Visibility = _audioInteractive && AudioLane.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PartVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (PartVolumeReadout is not null)
        {
            PartVolumeReadout.Text = $"{(int)Math.Round(e.NewValue)}%";
        }

        if (_settingPartVolume)
        {
            return;
        }

        PartVolumeChanged?.Invoke(this, (int)Math.Round(e.NewValue));
    }

    private void PartVolumePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var volume))
        {
            PartVolumeSlider.Value = volume;
        }
    }

    public void SetSilenceFractions(IReadOnlyList<(double Start, double End, int Volume)> fractions)
    {
        _silenceFractions = fractions.ToList();
        _audioSelection = null;
        DrawAudioLane();
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
        TrimRange.Visibility = passThrough ? Visibility.Collapsed : Visibility.Visible;
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
        TrimRange.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetTrimFractions(double start, double end)
    {
        var minGap = TrimGap();
        _trimStart = Math.Clamp(Math.Min(start, end - minGap), 0, 1);
        _trimEnd = Math.Clamp(Math.Max(end, _trimStart + minGap), 0, 1);
        ArrangeTrim();
        DrawCaptions();
        DrawAudioLane();
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
        if (IsTimelineOverlay(e.OriginalSource as DependencyObject))
        {
            HideHover();
            return;
        }

        if (_hoverDurationMs <= 0 || string.IsNullOrWhiteSpace(_hoverPath) || TimelineHost.ActualWidth <= 1)
        {
            HideHover();
            return;
        }

        if (_cutDragging && TimelineHost.ActualWidth > 1)
        {
            RememberEdgePointer(e);
            var dragEnd = CutFraction();
            _selection = (Math.Min(_cutAnchor, dragEnd), Math.Max(_cutAnchor, dragEnd));
            DrawRemovedFractions();
        }

        var rawX = _cutDragging && !double.IsNaN(_edgePointerX)
            ? PointerContentX()
            : e.GetCurrentPoint(TimelineHost).Position.X;
        var x = Math.Clamp(rawX, 0, TimelineHost.ActualWidth);
        var ms = HoverMilliseconds(e, x);
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
        if (_audioDragging || IsTimelineOverlay(e.OriginalSource as DependencyObject) || _trimDrag != TrimHandle.None || !_cutPicking || TimelineHost.ActualWidth <= 1)
        {
            return;
        }

        _cutDragging = true;
        RememberEdgePointer(e);
        _cutAnchor = CutFraction();
        TimelineHost.CapturePointer(e.Pointer);
        StartEdge();
        e.Handled = true;
    }

    private void Timeline_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_cutDragging)
        {
            return;
        }

        RememberEdgePointer(e);
        var end = CutFraction();
        _cutDragging = false;
        StopEdge();
        TimelineHost.ReleasePointerCaptures();
        var start = Math.Min(_cutAnchor, end);
        end = Math.Max(_cutAnchor, end);
        if (!TimelineGesture.IsDrag(start, end, TimelineHost.ActualWidth))
        {
            TimelineClicked?.Invoke(this, end);
        }
        else
        {
            CutSelected?.Invoke(this, new CutSelection(start, end));
        }

        e.Handled = true;
    }

    private double CutFraction()
        => Math.Clamp(PointerContentX() / Math.Max(1, TimelineHost.ActualWidth), 0, 1);

    private void DrawRemovedFractions()
    {
        CutsCanvas.Children.Clear();
        var width = TimelineHost.ActualWidth;
        var height = 28;
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

        foreach (var (start, end) in _removedFractions)
        {
            AddSpanLength(CutsCanvas, start, end, width, 6, always: false);
        }

        if (_selection is { } marked)
        {
            AddSpanLength(CutsCanvas, marked.Start, marked.End, width, 6, always: true);
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

    // A click moves the playhead so its center is under the pointer. The preview uses that
    // same value, which is inset from the raw width by half the thumb.
    private long HoverMilliseconds(PointerRoutedEventArgs e, double contentX)
    {
        if (HoverFollowsSlider())
        {
            var value = SliderValueAt(e.GetCurrentPoint(SeekSlider).Position.X);
            if (SeekSlider.Maximum > 0)
            {
                return (long)(value / SeekSlider.Maximum * _hoverDurationMs);
            }
        }

        return (long)(contentX / Math.Max(1, TimelineHost.ActualWidth) * _hoverDurationMs);
    }

    private bool HoverFollowsSlider()
        => !_cutDragging
           && _trimDrag == TrimHandle.None
           && !(_timelineZoom && _zoomIndex > 0)
           && SeekSlider.IsHitTestVisible
           && SeekSlider.ActualWidth > 1
           && SeekSlider.Maximum > SeekSlider.Minimum;

    private double SliderValueAt(double pointerX)
    {
        var fraction = TimelineGesture.SliderFraction(pointerX, SeekSlider.ActualWidth, SeekThumbWidth());
        var range = SeekSlider.Maximum - SeekSlider.Minimum;
        return TimelineGesture.SnapValue(
            SeekSlider.Minimum + fraction * range,
            SeekSlider.Minimum,
            SeekSlider.Maximum,
            SeekSlider.StepFrequency);
    }

    private double SeekThumbWidth()
    {
        if (_seekThumb is null)
        {
            SeekSlider.ApplyTemplate();
            _seekThumb = FindNamed(SeekSlider, "HorizontalThumb");
        }

        var width = _seekThumb?.ActualWidth ?? 0;
        return width >= 1 && width < SeekSlider.ActualWidth ? width : 18;
    }

    private static FrameworkElement? FindNamed(DependencyObject parent, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            var nested = FindNamed(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
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
        if (!_timelineZoom)
        {
            return;
        }

        var delta = e.GetCurrentPoint(TimelineScroll).Properties.MouseWheelDelta;
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            e.Handled = true;
            var point = e.GetCurrentPoint(TimelineHost).Position.X;
            var fraction = TimelineHost.ActualWidth > 1
                ? Math.Clamp(point / TimelineHost.ActualWidth, 0, 1)
                : (double?)null;
            StepZoom(delta > 0 ? 1 : -1, fraction);
            return;
        }

        if (_zoomIndex == 0 || delta == 0)
        {
            return;
        }

        e.Handled = true;
        if (Math.Abs(PanBy(TimelineGesture.WheelOffset(TimelineScroll.ViewportWidth, delta))) >= 0.5)
        {
            _holdView = true;
        }
    }

    private void ScrollEarlier_Click(object sender, RoutedEventArgs e) => PanPage(-1);

    private void ScrollLater_Click(object sender, RoutedEventArgs e) => PanPage(1);

    private void TimelinePan_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_timelineZoom || _zoomIndex == 0)
        {
            return;
        }

        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or NumberBox or Slider or RichEditBox)
        {
            return;
        }

        var distance = sender.Key == VirtualKey.Left
            ? -TimelineGesture.KeyPan(TimelineScroll.ViewportWidth)
            : TimelineGesture.KeyPan(TimelineScroll.ViewportWidth);
        if (Math.Abs(PanBy(distance)) < 0.5)
        {
            return;
        }

        _holdView = true;
        args.Handled = true;
    }

    private void PanPage(int direction)
    {
        if (Math.Abs(PanBy(direction * TimelineGesture.PagePan(TimelineScroll.ViewportWidth))) >= 0.5)
        {
            _holdView = true;
        }
    }

    private double PanBy(double delta)
    {
        if (Math.Abs(delta) < 0.5 || _layingZoom)
        {
            return 0;
        }

        var width = TimelineHost.ActualWidth;
        var viewport = TimelineScroll.ViewportWidth;
        if (width <= viewport + 1 || viewport <= 1)
        {
            return 0;
        }

        var next = Math.Clamp(TimelineScroll.HorizontalOffset + delta, 0, Math.Max(0, width - viewport));
        var applied = next - TimelineScroll.HorizontalOffset;
        if (Math.Abs(applied) < 0.5)
        {
            return 0;
        }

        TimelineScroll.ChangeView(next, null, null, true);
        return applied;
    }

    private void Seek_EdgePressed(object sender, PointerRoutedEventArgs e)
    {
        _seekDragging = true;
        RememberEdgePointer(e);
        if (_timelineZoom && _zoomIndex > 0)
        {
            MoveSeekToContent();
        }

        StartEdge();
    }

    private void Seek_EdgeMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_seekDragging)
        {
            return;
        }

        RememberEdgePointer(e);
        if (_timelineZoom && _zoomIndex > 0)
        {
            MoveSeekToContent();
        }
    }

    private void Seek_EdgeReleased(object sender, PointerRoutedEventArgs e)
    {
        _seekDragging = false;
        StopEdge();
    }

    private void RememberEdgePointer(PointerRoutedEventArgs e)
        => _edgePointerX = e.GetCurrentPoint(TimelineScroll).Position.X;

    private double PointerContentX()
        => _edgePointerX + TimelineScroll.HorizontalOffset;

    private void StartEdge()
    {
        if (_timelineZoom && _zoomIndex > 0)
        {
            _edgeTimer.Start();
        }
    }

    private void StopEdge()
    {
        if (_seekDragging || _audioDragging || _cutDragging || _trimDrag != TrimHandle.None)
        {
            return;
        }

        _edgeTimer.Stop();
    }

    private void AdvanceEdgeScroll()
    {
        if (_zoomIndex == 0 || _layingZoom || double.IsNaN(_edgePointerX))
        {
            return;
        }

        var applied = PanBy(TimelineGesture.EdgeScroll(_edgePointerX, TimelineScroll.ViewportWidth));
        if (Math.Abs(applied) < 0.5)
        {
            return;
        }

        _holdView = true;
        var contentX = PointerContentX();
        var width = Math.Max(1, TimelineHost.ActualWidth);
        if (_trimDrag != TrimHandle.None)
        {
            ApplyTrimDrag(contentX);
        }

        if (_cutDragging)
        {
            var end = Math.Clamp(contentX / width, 0, 1);
            _selection = (Math.Min(_cutAnchor, end), Math.Max(_cutAnchor, end));
            DrawRemovedFractions();
        }

        if (_audioDragging)
        {
            var end = Math.Clamp(contentX / Math.Max(1, AudioLane.ActualWidth), 0, 1);
            _audioSelection = (Math.Min(_audioAnchor, end), Math.Max(_audioAnchor, end));
            DrawAudioLane();
        }

        if (_seekDragging)
        {
            MoveSeekToContent();
        }
    }

    private void MoveSeekToContent()
    {
        if (SeekSlider is null || SeekSlider.Maximum <= SeekSlider.Minimum || double.IsNaN(_edgePointerX))
        {
            return;
        }

        var width = Math.Max(1, TimelineHost.ActualWidth);
        var range = SeekSlider.Maximum - SeekSlider.Minimum;
        SeekSlider.Value = Math.Clamp(SeekSlider.Minimum + PointerContentX() / width * range, SeekSlider.Minimum, SeekSlider.Maximum);
    }

    private void TimelineScroll_Entered(object sender, PointerRoutedEventArgs e) => _timelineHovered = true;

    private void TimelineScroll_Exited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(TimelineScroll).Position;
        _timelineHovered = point.X >= 0 && point.Y >= 0 && point.X < TimelineScroll.ActualWidth && point.Y < TimelineScroll.ActualHeight;
    }

    private void TimelineScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_layingZoom)
        {
            LayoutZoom(ViewCenterFraction());
        }
    }

    private void StepZoom(int direction, double? anchor = null)
    {
        var next = Math.Clamp(_zoomIndex + direction, 0, MaxZoomIndex());
        if (next == _zoomIndex)
        {
            return;
        }

        _zoomIndex = next;
        LayoutZoom(anchor ?? ZoomAnchorFraction());
    }

    private int MaxZoomIndex() => TimelineGesture.HighestZoomIndex(TimelineScroll.ViewportWidth);

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
            var maxIndex = MaxZoomIndex();
            if (_zoomIndex > maxIndex)
            {
                _zoomIndex = maxIndex;
            }

            var width = TimelineGesture.TimelineWidth(viewport, _zoomIndex);
            TimelineHost.Width = width;
            UpdateZoomLabel();
            UpdateSeekPrecision();
            TimelineHost.UpdateLayout();
            var offset = anchorFraction * width - viewport / 2;
            TimelineScroll.ChangeView(Math.Clamp(offset, 0, Math.Max(0, width - viewport)), null, null, true);
            DrawRemovedFractions();
            DrawAudioLane();
            DrawCaptions();
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

        ZoomLabel.Text = $"{TimelineGesture.ZoomSteps[Math.Clamp(_zoomIndex, 0, TimelineGesture.ZoomSteps.Length - 1)]:0}×";
        ZoomOutButton.IsEnabled = _zoomIndex > 0;
        ZoomInButton.IsEnabled = _zoomIndex < MaxZoomIndex();
        var zoomed = _timelineZoom && _zoomIndex > 0;
        if (!zoomed)
        {
            _holdView = false;
        }

        if (ScrollEarlierButton is not null)
        {
            ScrollEarlierButton.IsEnabled = zoomed;
        }

        if (ScrollLaterButton is not null)
        {
            ScrollLaterButton.IsEnabled = zoomed;
        }
    }

    private void UpdateSeekPrecision()
    {
        if (_seekDragging || SeekSlider is null)
        {
            return;
        }

        var width = TimelineHost.ActualWidth;
        if (TimelineHost.Width > width)
        {
            width = TimelineHost.Width;
        }

        if (width <= 1 || double.IsNaN(width))
        {
            return;
        }

        var step = TimelineGesture.SeekStep(SeekSlider.Maximum, width);
        if (step <= 0)
        {
            return;
        }

        SeekSlider.StepFrequency = step;
        SeekSlider.SmallChange = step;
        var viewport = Math.Max(1, TimelineScroll.ViewportWidth);
        SeekSlider.LargeChange = Math.Clamp(viewport / width * SeekSlider.Maximum, step, SeekSlider.Maximum);
    }

    private double ZoomAnchorFraction()
    {
        var playhead = SeekSlider.Maximum > 0 ? SeekSlider.Value / SeekSlider.Maximum : 0;
        var width = TimelineHost.ActualWidth;
        var viewport = TimelineScroll.ViewportWidth;
        if (width <= 1 || viewport <= 1)
        {
            return playhead;
        }

        var left = TimelineScroll.HorizontalOffset;
        var x = playhead * width;
        if (x >= left - 1 && x <= left + viewport + 1)
        {
            return playhead;
        }

        return ViewCenterFraction();
    }

    public void RevealFraction(double fraction)
    {
        if (!_timelineZoom || _zoomIndex == 0 || _layingZoom || _timelineHovered || _holdView || _audioDragging || _cutDragging || _trimDrag != TrimHandle.None || _seekDragging)
        {
            if (_holdView && PlayheadInView(fraction))
            {
                _holdView = false;
            }

            return;
        }

        var width = TimelineHost.ActualWidth;
        var viewport = TimelineScroll.ViewportWidth;
        if (width <= viewport + 1 || viewport <= 1)
        {
            return;
        }

        var x = Math.Clamp(fraction, 0, 1) * width;
        var left = TimelineScroll.HorizontalOffset;
        var right = left + viewport;
        const double margin = 64;
        double target;
        if (x < left + margin)
        {
            target = x - margin;
        }
        else if (x > right - margin)
        {
            target = x - (viewport - margin);
        }
        else
        {
            return;
        }

        TimelineScroll.ChangeView(Math.Clamp(target, 0, Math.Max(0, width - viewport)), null, null, true);
    }

    private bool PlayheadInView(double fraction)
    {
        var width = TimelineHost.ActualWidth;
        var viewport = TimelineScroll.ViewportWidth;
        if (width <= viewport + 1 || viewport <= 1)
        {
            return true;
        }

        var x = Math.Clamp(fraction, 0, 1) * width;
        var left = TimelineScroll.HorizontalOffset;
        return x >= left + 64 && x <= left + viewport - 64;
    }

    private void TrimCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => ArrangeTrim();

    private void Trim_Pressed(object sender, PointerRoutedEventArgs e)
    {
        var width = TrimCanvas.ActualWidth;
        if (width <= 1)
        {
            return;
        }

        RememberEdgePointer(e);
        var x = PointerContentX();
        var startX = _trimStart * width;
        var endX = _trimEnd * width;
        _trimDrag = Math.Abs(x - startX) <= _trimGrab
            ? TrimHandle.Start
            : Math.Abs(x - endX) <= _trimGrab
                ? TrimHandle.End
                : TrimHandle.Seek;
        TrimCanvas.CapturePointer(e.Pointer);
        ApplyTrimDrag(x);
        StartEdge();
        e.Handled = true;
    }

    private void Trim_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_trimDrag == TrimHandle.None)
        {
            return;
        }

        RememberEdgePointer(e);
        ApplyTrimDrag(PointerContentX());
        e.Handled = true;
    }

    private void Trim_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_trimDrag == TrimHandle.None)
        {
            return;
        }

        RememberEdgePointer(e);
        ApplyTrimDrag(PointerContentX());
        _trimDrag = TrimHandle.None;
        StopEdge();
        TrimCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void ApplyTrimDrag(double x)
    {
        var width = Math.Max(1, TrimCanvas.ActualWidth);
        var fraction = Math.Clamp(x / width, 0, 1);
        var minGap = TrimGap();
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
        DrawAudioLane();
    }

    private void Audio_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_audioInteractive || AudioLane.ActualWidth <= 1)
        {
            return;
        }

        _audioDragging = true;
        RememberEdgePointer(e);
        _audioAnchor = AudioFraction();
        _audioSelection = (_audioAnchor, _audioAnchor);
        AudioLane.CapturePointer(e.Pointer);
        StartEdge();
        DrawAudioLane();
        e.Handled = true;
    }

    private void Audio_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (!_audioDragging || AudioLane.ActualWidth <= 1)
        {
            return;
        }

        RememberEdgePointer(e);
        var end = AudioFraction();
        _audioSelection = (Math.Min(_audioAnchor, end), Math.Max(_audioAnchor, end));
        DrawAudioLane();
        e.Handled = true;
    }

    private void Audio_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_audioDragging)
        {
            return;
        }

        RememberEdgePointer(e);
        var end = AudioFraction();
        _audioDragging = false;
        StopEdge();
        AudioLane.ReleasePointerCaptures();
        var start = Math.Min(_audioAnchor, end);
        end = Math.Max(_audioAnchor, end);
        _audioSelection = null;
        DrawAudioLane();
        if (!TimelineGesture.IsDrag(start, end, AudioLane.ActualWidth))
        {
            AudioSilenceClicked?.Invoke(this, end);
        }
        else
        {
            AudioRangeSelected?.Invoke(this, new CutSelection(start, end));
        }

        e.Handled = true;
    }

    private double AudioFraction()
        => Math.Clamp(PointerContentX() / Math.Max(1, AudioLane.ActualWidth), 0, 1);

    private double TrimGap()
    {
        var gap = TimelineGesture.TrimGapFraction(_hoverDurationMs);
        return gap > 0 ? gap : 0.01;
    }

    private void DrawAudioLane()
    {
        if (AudioCanvas is null || AudioLane.Visibility != Visibility.Visible)
        {
            return;
        }

        AudioCanvas.Children.Clear();
        var width = AudioLane.ActualWidth;
        if (width <= 1)
        {
            width = TimelineHost.ActualWidth;
        }

        if (width <= 1)
        {
            return;
        }

        AudioCanvas.Width = width;
        AudioCanvas.Height = AudioLane.Height;
        var lane = AudioLane.Height > 1 ? AudioLane.Height : 32;
        if (!_audioInteractive)
        {
            var empty = new TextBlock
            {
                Width = width,
                Height = lane,
                Text = "No audio",
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(200, 255, 255, 255)),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            AudioCanvas.Children.Add(empty);
            return;
        }

        if (_waveLevels.HasPeaks)
        {
            AddWaveform(width, lane);
        }
        else
        {
            AddAudioBand(0, 1, width, 8, ColorHelper.FromArgb(255, 126, 182, 214));
        }

        var outside = ColorHelper.FromArgb(110, 0, 0, 0);
        if (_trimStart > 0.001)
        {
            AddAudioBand(0, _trimStart, width, lane, outside);
        }

        if (_trimEnd < 0.999)
        {
            AddAudioBand(_trimEnd, 1, width, lane, outside);
        }
        foreach (var (start, end, volume) in _silenceFractions)
        {
            AddAudioBand(start, end, width, lane, AudioLevelColor(volume));
        }

        if (_audioSelection is { } selected && selected.End > selected.Start)
        {
            AddAudioBand(selected.Start, selected.End, width, lane, ColorHelper.FromArgb(100, 255, 196, 64));
        }

        foreach (var (start, end, volume) in _silenceFractions)
        {
            AddSpanLength(AudioCanvas, start, end, width, 3, always: false, text: volume <= 0 ? "0%" : $"{volume}%");
        }

        if (_audioSelection is { } drag && drag.End > drag.Start)
        {
            AddSpanLength(AudioCanvas, drag.Start, drag.End, width, 3, always: true, text: AudioDragLabel(drag.Start, drag.End));
        }
    }

    private void AddSpanLength(Canvas canvas, double start, double end, double width, double top, bool always, string? text = null)
    {
        if (!always && Math.Abs(end - start) * width < 48)
        {
            return;
        }

        var label = text ?? SpanLabel(start, end);
        if (label is not null)
        {
            AddDragLabel(canvas, label, start, end, width, top);
        }
    }

    private static Windows.UI.Color AudioLevelColor(int volume)
    {
        if (volume <= 0)
        {
            return ColorHelper.FromArgb(140, 90, 28, 28);
        }

        if (volume < AudioSilence.OriginalVolume)
        {
            return ColorHelper.FromArgb(110, 32, 112, 168);
        }

        return ColorHelper.FromArgb(120, 214, 148, 32);
    }

    private string? SpanLabel(double start, double end)
    {
        if (_hoverDurationMs <= 0)
        {
            return null;
        }

        var ms = (long)Math.Round(Math.Abs(end - start) * _hoverDurationMs);
        if (ms <= 0)
        {
            return null;
        }

        var tenths = (int)((ms % 1000) / 100);
        var seconds = ms / 1000;
        if (seconds >= 3600)
        {
            return $"{seconds / 3600}:{(seconds / 60) % 60:00}:{seconds % 60:00}.{tenths}";
        }

        if (seconds >= 60)
        {
            return $"{seconds / 60}:{seconds % 60:00}.{tenths}";
        }

        return $"{seconds}.{tenths}s";
    }

    private string? AudioDragLabel(double start, double end)
    {
        var duration = SpanLabel(start, end);
        if (duration is null)
        {
            return null;
        }

        var level = PartVolumePercent <= 0 ? "0%" : $"{PartVolumePercent}%";
        return $"{duration} · {level}";
    }

    private static void AddDragLabel(Canvas canvas, string text, double start, double end, double width, double top)
    {
        var left = Math.Clamp(Math.Min(start, end), 0, 1) * width;
        var chip = new Border
        {
            Height = 16,
            Background = new SolidColorBrush(ColorHelper.FromArgb(220, 12, 12, 12)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            }
        };
        var reserve = text.Length * 7 + 8;
        Canvas.SetLeft(chip, Math.Clamp(left, 0, Math.Max(0, width - reserve)));
        Canvas.SetTop(chip, top);
        canvas.Children.Add(chip);
    }

    private void AddAudioBand(double start, double end, double width, double band, Windows.UI.Color color)
    {
        var left = Math.Clamp(Math.Min(start, end), 0, 1) * width;
        var right = Math.Clamp(Math.Max(start, end), 0, 1) * width;
        var mark = new Border
        {
            Width = Math.Max(2, right - left),
            Height = band,
            Background = new SolidColorBrush(color),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(mark, left);
        var lane = AudioLane.Height > 1 ? AudioLane.Height : band;
        Canvas.SetTop(mark, Math.Max(0, (lane - band) / 2));
        AudioCanvas.Children.Add(mark);
    }

    private void RequestWaveform()
    {
        if (AudioLane is null || AudioLane.Visibility != Visibility.Visible || !_audioInteractive || string.IsNullOrWhiteSpace(_hoverPath))
        {
            return;
        }

        var path = _hoverPath;
        var key = AudioWaveform.WaveformKey.For(path);
        if (AudioWaveform.TryGet(key, out var cached))
        {
            _waveKey = key;
            _waveAttempted = true;
            _waveLoading = false;
            _waveLevels = cached;
            DrawAudioLane();
            return;
        }

        if (_waveLoading && key.Equals(_waveKey))
        {
            return;
        }

        _waveKey = key;
        _waveLoading = true;
        _waveAttempted = true;
        var serial = ++_waveSerial;
        _ = LoadWaveformAsync(path, key, serial);
    }

    private async Task LoadWaveformAsync(string path, AudioWaveform.WaveformKey key, int serial)
    {
        AudioWaveform.Levels? levels = null;
        for (var attempt = 0; attempt < 4 && serial == _waveSerial; attempt++)
        {
            try
            {
                levels = await AudioWaveform.ReadAsync(path);
            }
            catch (Exception)
            {
                levels = null;
            }

            if (levels is not null)
            {
                break;
            }

            if (attempt < 3)
            {
                await Task.Delay(500);
            }
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (serial != _waveSerial)
            {
                return;
            }

            _waveLoading = false;
            if (levels is null)
            {
                _waveAttempted = false;
                DrawAudioLane();
                return;
            }

            if (!AudioWaveform.WaveformKey.For(path).Equals(key))
            {
                _waveAttempted = false;
                RequestWaveform();
                return;
            }

            _waveLevels = levels.Value;
            DrawAudioLane();
        });
    }

    private void AddWaveform(double width, double lane)
    {
        var viewport = TimelineScroll.ViewportWidth;
        var offset = TimelineScroll.HorizontalOffset;
        if (double.IsNaN(viewport) || viewport <= 1 || width <= viewport + 1)
        {
            offset = 0;
            viewport = width;
        }

        var scale = XamlRoot?.RasterizationScale ?? 1;
        if (scale < 1 || double.IsNaN(scale))
        {
            scale = 1;
        }

        var pixelWidth = Math.Clamp((int)Math.Round(viewport * scale), 1, 8192);
        var pixelHeight = Math.Clamp((int)Math.Round(lane * scale), 1, 256);
        var bars = Math.Max(1, pixelWidth / 2);
        var timeline = _hoverDurationMs > 0 ? _hoverDurationMs / 1000d : _waveLevels.DurationSeconds;
        var start = width > 1 ? offset / width : 0;
        var end = width > 1 ? Math.Min(1, (offset + viewport) / width) : 1;
        var window = AudioWaveform.Window(_waveLevels, timeline, start, end, bars);
        var pixels = new byte[pixelWidth * pixelHeight * 4];
        AudioWaveform.Paint(window, pixels, pixelWidth, pixelHeight, 232, 244, 255, 235);
        var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        bitmap.Invalidate();
        var image = new Image
        {
            Source = bitmap,
            Width = viewport,
            Height = lane,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(image, Math.Max(0, offset));
        Canvas.SetTop(image, 0);
        AudioCanvas.Children.Add(image);
    }

    private bool IsTimelineOverlay(DependencyObject? source) => IsLane(source, AudioLane) || IsLane(source, CaptionLane);

    private static bool IsLane(DependencyObject? source, DependencyObject lane)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, lane))
            {
                return true;
            }
        }

        return false;
    }

    public void SetCaptionCues(IReadOnlyList<(long StartMs, long EndMs, string Text)> cues)
    {
        _captionCues.Clear();
        _captionTip = -1;
        foreach (var cue in cues)
        {
            if (cue.EndMs <= cue.StartMs)
            {
                continue;
            }

            _captionCues.Add((cue.StartMs, cue.EndMs, OneCaptionLine(cue.Text)));
        }

        _captionCues.Sort((left, right) => left.StartMs.CompareTo(right.StartMs));
        UpdateCaptionLane();
    }

    private void UpdateCaptionLane()
    {
        var show = _captionCues.Count > 0;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (CaptionLane is not null)
        {
            CaptionLane.Visibility = visibility;
        }

        if (CaptionLaneLabel is not null)
        {
            CaptionLaneLabel.Visibility = visibility;
        }

        FitTimeline();
        DrawCaptions();
    }

    private void FitTimeline()
    {
        if (TimelineHost is null || TimelineHost.RowDefinitions.Count == 0)
        {
            return;
        }

        var height = TimelineHost.RowDefinitions[0].Height.GridUnitType == GridUnitType.Pixel
            ? TimelineHost.RowDefinitions[0].Height.Value
            : 28;
        if (CaptionLane is { Visibility: Visibility.Visible })
        {
            height += CaptionLane.Margin.Top + CaptionLane.Height + CaptionLane.Margin.Bottom;
        }

        if (AudioLane is { Visibility: Visibility.Visible })
        {
            height += AudioLane.Margin.Top + AudioLane.Height + AudioLane.Margin.Bottom;
        }

        TimelineHost.MinHeight = Math.Max(28, height);
    }

    private void TimelineScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        DrawCaptions();
        if (!_waveLevels.HasPeaks || _waveQueued)
        {
            return;
        }

        _waveQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _waveQueued = false;
            if (_waveLevels.HasPeaks)
            {
                DrawAudioLane();
            }
        });
    }

    private void Caption_Moved(object sender, PointerRoutedEventArgs e)
    {
        var width = CaptionWidth();
        var index = width <= 1 ? -1 : CueIndexAt(e.GetCurrentPoint(CaptionLane).Position.X, width);
        if (index == _captionTip)
        {
            return;
        }

        _captionTip = index;
        ToolTipService.SetToolTip(CaptionLane, index < 0 ? null : _captionCues[index].Text);
    }

    private void Caption_Exited(object sender, PointerRoutedEventArgs e)
    {
        _captionTip = -1;
        ToolTipService.SetToolTip(CaptionLane, null);
    }

    private void Caption_Pressed(object sender, PointerRoutedEventArgs e)
    {
        var width = CaptionWidth();
        if (width <= 1)
        {
            e.Handled = true;
            return;
        }

        var index = CueIndexAt(e.GetCurrentPoint(CaptionLane).Position.X, width);
        if (index >= 0)
        {
            CaptionLineChosen?.Invoke(this, _captionCues[index].StartMs);
        }

        e.Handled = true;
    }

    private double CaptionWidth()
    {
        var width = CaptionLane?.ActualWidth ?? 0;
        return width > 1 ? width : TimelineHost.ActualWidth;
    }

    private int CueIndexAt(double x, double width)
    {
        if (_hoverDurationMs <= 0 || _captionCues.Count == 0 || width <= 1)
        {
            return -1;
        }

        var fraction = x / width;
        var slop = 4 / width;
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < _captionCues.Count; i++)
        {
            var start = _captionCues[i].StartMs / (double)_hoverDurationMs;
            var end = _captionCues[i].EndMs / (double)_hoverDurationMs;
            if (fraction < start - slop || fraction > end + slop)
            {
                continue;
            }

            var distance = Math.Abs(fraction - (start + end) / 2);
            if (fraction >= start && fraction <= end)
            {
                distance -= 1;
            }

            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void DrawCaptions()
    {
        if (CaptionCanvas is null || CaptionLane is null || CaptionLane.Visibility != Visibility.Visible)
        {
            return;
        }

        CaptionCanvas.Children.Clear();
        var width = CaptionWidth();
        if (width <= 1 || _hoverDurationMs <= 0 || _captionCues.Count == 0)
        {
            return;
        }

        CaptionCanvas.Width = width;
        CaptionCanvas.Height = 18;
        var viewLeft = TimelineScroll.HorizontalOffset - 48;
        var viewRight = TimelineScroll.HorizontalOffset + Math.Max(TimelineScroll.ViewportWidth, width) + 48;
        double? runLeft = null;
        var runRight = 0d;
        var runKept = false;
        foreach (var cue in _captionCues)
        {
            var start = Math.Clamp(cue.StartMs / (double)_hoverDurationMs, 0, 1);
            var end = Math.Clamp(cue.EndMs / (double)_hoverDurationMs, 0, 1);
            if (end <= start)
            {
                continue;
            }

            var left = start * width;
            var right = Math.Max(left + 1, end * width);
            if (right < viewLeft || left > viewRight)
            {
                FlushCaptionRun(ref runLeft, runRight, width, runKept);
                continue;
            }

            var kept = CueKept(start, end);
            if (right - left >= 72)
            {
                FlushCaptionRun(ref runLeft, runRight, width, runKept);
                AddCaptionMark(left, right, width, kept, cue.Text);
                continue;
            }

            if (runLeft is not null && kept == runKept && left <= runRight + 1.5)
            {
                runRight = Math.Max(runRight, right);
                continue;
            }

            FlushCaptionRun(ref runLeft, runRight, width, runKept);
            runLeft = left;
            runRight = right;
            runKept = kept;
        }

        FlushCaptionRun(ref runLeft, runRight, width, runKept);
    }

    private void FlushCaptionRun(ref double? runLeft, double runRight, double width, bool kept)
    {
        if (runLeft is not double left)
        {
            return;
        }

        AddCaptionMark(left, runRight, width, kept, null);
        runLeft = null;
    }

    private bool CueKept(double start, double end)
    {
        var from = Math.Max(start, _trimStart);
        var to = Math.Min(end, _trimEnd);
        if (to - from <= 0.0000001)
        {
            return false;
        }

        foreach (var (cutStart, cutEnd) in _removedFractions)
        {
            if (Math.Min(to, cutEnd) - Math.Max(from, cutStart) >= (to - from) - 0.0000001)
            {
                return false;
            }
        }

        return true;
    }

    private void AddCaptionMark(double left, double right, double width, bool kept, string? text)
    {
        var wide = !string.IsNullOrWhiteSpace(text);
        var mark = new Border
        {
            Width = Math.Max(1, Math.Min(width, right) - Math.Max(0, left)),
            Height = wide ? 14 : 4,
            Background = new SolidColorBrush(kept
                ? ColorHelper.FromArgb(230, 214, 196, 255)
                : ColorHelper.FromArgb(90, 255, 255, 255)),
            CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false
        };
        if (wide)
        {
            mark.Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 24, 18, 36)),
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            };
        }

        Canvas.SetLeft(mark, Math.Max(0, left));
        Canvas.SetTop(mark, wide ? 2 : 7);
        CaptionCanvas.Children.Add(mark);
    }

    private static string OneCaptionLine(string text)
    {
        var line = string.Join(' ', text.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= 80 ? line : line[..80];
    }
}
