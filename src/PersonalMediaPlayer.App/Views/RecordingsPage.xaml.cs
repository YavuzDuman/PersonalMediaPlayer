using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Controls;
using Windows.ApplicationModel.DataTransfer;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core.Models;
using WinRT.Interop;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class RecordingsPage : Page
{
    private readonly List<MonitorChoice> _monitors = [];
    private readonly List<WindowChoice> _windows = [];
    private ScreenRecorder? _recorder;
    private RecordingBarWindow? _bar;
    private const string RecordingsAlbum = "Recordings";
    private System.Threading.Timer? _elapsedTimer;
    private bool _previewFullScreen;
    private bool _fullscreenTransition;
    private long? _seekAfterStart;
    private bool _pauseAfterStart;
    private bool _holdPauseUntilFrame;
    private string? _activePath;
    private nint _targetWindow;
    private bool _useArea;
    private bool _areaArmed;
    private bool _areaPicking;
    private bool _areaStarting;
    private int _areaToken;
    private MonitorRect _areaMonitor;
    private int _cropX;
    private int _cropY;
    private int _cropWidth;
    private int _cropHeight;
    private bool _areaBarOverlaps;
    private bool _finishInProgress;
    private LibVLC? _libVlc;
    private VlcMediaPlayer? _player;
    private Media? _previewMedia;
    private string? _previewPath;
    private string? _playWhenReady;
    private bool _previewSaved;
    private bool _allowLeave;
    private bool _allowWindowClose;
    private bool _discardOnFinish;
    private LeavePromptKind _promptKind;

    private enum LeavePromptKind
    {
        None,
        NavigatePreview,
        NavigateRecording,
        ClosePreview,
        CloseRecording
    }
    private bool _closeWindowAfterFinish;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private bool _pendingIsBack;
    private DispatcherTimer? _previewClock;
    private bool _previewDragging;
    private bool _previewTrimming;
    private long _previewTrimStartMs;
    private long _previewTrimEndMs;
    private double _lastPreviewVolume = 80;
    private bool _updatingPreviewSeek;
    private bool _hasPreviewDuration;
    private bool _previewEnded;
    private long _previewDurationMs;
    private const long MaxPreviewDurationMs = 24L * 60 * 60 * 1000;

    private bool HasUnsavedPreview => _previewPath is not null && !_previewSaved;

    internal bool IsAwaitingAreaStart => _areaArmed && _recorder is null;

    private static string RecordingsDirectory => Path.Combine(App.MediaLibrary.LibraryRoot, "Recordings");

    private static string PendingDirectory => Path.Combine(RecordingsDirectory, ".pending");

    public RecordingsPage()
    {
        InitializeComponent();
        _previewClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _previewClock.Tick += (_, _) => UpdatePreviewClock();
        Playback.SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(PreviewSeek_Pressed), true);
        Playback.SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(PreviewSeek_Released), true);
        Playback.SeekSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(PreviewSeek_Released), true);
        Playback.SeekSlider.ValueChanged += PreviewSeek_ValueChanged;
        Playback.VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        Playback.BackButton.Click += BackTen_Click;
        Playback.ForwardButton.Click += ForwardTen_Click;
        Playback.PlayButton.Click += PlayPreview_Click;
        Playback.FullScreenButton.Click += FullScreen_Click;
        Playback.MuteButton.Click += PreviewMute_Click;
        Playback.SpeedCombo.SelectionChanged += PreviewSpeed_Changed;
        Playback.TrimRangeChanged += (_, _) => ApplyPreviewTrimFromBar();
        Playback.TrimSeekRequested += (_, fraction) => SeekPreviewFraction(fraction);
        CursorSwitch.IsOn = CaptureSettings.LoadIncludeCursor();
        CursorSwitch.Toggled += (_, _) => CaptureSettings.SaveIncludeCursor(CursorSwitch.IsOn);
        SystemAudioSwitch.IsOn = CaptureSettings.LoadIncludeSystemAudio();
        SystemAudioSwitch.Toggled += (_, _) => CaptureSettings.SaveIncludeSystemAudio(SystemAudioSwitch.IsOn);
        Loaded += (_, _) =>
        {
            LoadMonitors();
            LoadWindows();
            ShowRecordings();
        };
    }

    internal void TogglePause()
    {
        if (_recorder is null)
        {
            if (_areaArmed)
            {
                _ = StartAreaCaptureAsync();
            }

            return;
        }

        if (_recorder.IsPaused)
        {
            _recorder.Resume();
            _bar?.SetPaused(false);
            StatusText.Text = "Recording.";
        }
        else
        {
            _recorder.Pause();
            _bar?.SetPaused(true);
            StatusText.Text = "Paused.";
        }
    }

    internal void RestartRecording() => _ = RestartAsync();

    internal void StopRecording() => _ = FinishAsync(keep: true);

    internal void CancelRecording()
    {
        if (_recorder is null)
        {
            if (_areaPicking)
            {
                ScreenshotSession.Current?.Cancel();
            }

            if (_areaArmed || _useArea || _areaPicking)
            {
                EndAreaSelection("Area selection cancelled.");
            }

            return;
        }

        _ = FinishAsync(keep: false);
    }

    internal void NotifyBarClosed(bool discard)
    {
        _bar = null;
        if (_finishInProgress)
        {
            return;
        }

        if (_recorder is null)
        {
            if (_areaArmed || _useArea || _areaPicking)
            {
                EndAreaSelection("Area selection cancelled.");
            }

            return;
        }

        _ = FinishAsync(keep: !discard);
    }

    internal bool TryHandleHostClose()
    {
        if (_allowWindowClose)
        {
            return false;
        }

        if (_discardOnFinish || _closeWindowAfterFinish || _promptKind is LeavePromptKind.ClosePreview or LeavePromptKind.CloseRecording)
        {
            return true;
        }

        if (IsAwaitingAreaStart || _areaPicking)
        {
            if (_areaPicking)
            {
                ScreenshotSession.Current?.Cancel();
            }

            EndAreaSelection("Area selection cancelled.");
            return false;
        }

        if (_recorder is null && !_finishInProgress && !HasUnsavedPreview)
        {
            return false;
        }

        var kind = _recorder is not null || (_finishInProgress && !HasUnsavedPreview)
            ? LeavePromptKind.CloseRecording
            : LeavePromptKind.ClosePreview;
        DispatcherQueue.TryEnqueue(() => ShowLeavePrompt(kind));
        return true;
    }

    internal bool PrepareToLeave(Type? pageType, object? parameter, bool back)
    {
        _pendingPageType = pageType;
        _pendingParameter = parameter;
        _pendingIsBack = back;
        if (_areaPicking)
        {
            ScreenshotSession.Current?.Cancel();
            EndAreaSelection("Area selection cancelled.");
        }
        else if (IsAwaitingAreaStart)
        {
            EndAreaSelection("Area selection cancelled.");
        }

        if (_recorder is not null || _finishInProgress || HasUnsavedPreview)
        {
            var kind = _recorder is not null || (_finishInProgress && !HasUnsavedPreview)
                ? LeavePromptKind.NavigateRecording
                : LeavePromptKind.NavigatePreview;
            ShowLeavePrompt(kind);
            return false;
        }

        ReleaseVideoBeforeLeaving();
        return true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _allowLeave = false;

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Playback.SetHoverSource(null);
        _previewClock?.Stop();
        if (_previewFullScreen && App.MainAppWindow is MainWindow window)
        {
            window.AppWindow.Changed -= PreviewWindow_Changed;
            window.SetFullScreen(false);
            _previewFullScreen = false;
        }
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (_areaPicking)
        {
            ScreenshotSession.Current?.Cancel();
            EndAreaSelection("Area selection cancelled.");
        }
        else if (_areaArmed && _recorder is null)
        {
            EndAreaSelection("Area selection cancelled.");
        }

        if (_allowLeave || (_recorder is null && !_finishInProgress && !HasUnsavedPreview && !_areaArmed && !_areaPicking && _player is null && _libVlc is null))
        {
            return;
        }

        e.Cancel = true;
        _pendingPageType = e.SourcePageType;
        _pendingParameter = e.Parameter;
        _pendingIsBack = e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back;
        if (LeavePrompt.Visibility != Visibility.Visible)
        {
            var kind = _recorder is not null ? LeavePromptKind.NavigateRecording : LeavePromptKind.NavigatePreview;
            DispatcherQueue.TryEnqueue(() => ShowLeavePrompt(kind));
        }
    }

    private void ReleaseVideoBeforeLeaving()
    {
        DisposePlayer();
    }

    private void ShowLeavePrompt(LeavePromptKind kind)
    {
        if (kind == LeavePromptKind.None)
        {
            return;
        }

        _promptKind = kind;
        var closing = kind is LeavePromptKind.ClosePreview or LeavePromptKind.CloseRecording;
        var recording = kind is LeavePromptKind.NavigateRecording or LeavePromptKind.CloseRecording;
        LeavePromptText.Text = recording
            ? "This recording is not saved yet. Discard it?"
            : "This video is not in Recordings. Leave without saving?";
        LeavePromptConfirm.Content = closing ? "Discard" : "Leave";
        LeavePrompt.Visibility = Visibility.Visible;
    }

    private void LeavePromptStay_Click(object sender, RoutedEventArgs e)
    {
        _promptKind = LeavePromptKind.None;
        LeavePrompt.Visibility = Visibility.Collapsed;
    }

    private async void LeavePromptConfirm_Click(object sender, RoutedEventArgs e)
    {
        var kind = _promptKind;
        _promptKind = LeavePromptKind.None;
        LeavePrompt.Visibility = Visibility.Collapsed;
        switch (kind)
        {
            case LeavePromptKind.NavigatePreview:
                DisposePlayer();
                ClosePreview(deleteFile: true);
                _allowLeave = true;
                ContinueNavigation();
                break;
            case LeavePromptKind.NavigateRecording:
                await FinishAsync(keep: false);
                DisposePlayer();
                _allowLeave = true;
                ContinueNavigation();
                break;
            case LeavePromptKind.ClosePreview:
                ClosePreview(deleteFile: true);
                _allowWindowClose = true;
                App.MainAppWindow.Close();
                break;
            case LeavePromptKind.CloseRecording:
                _closeWindowAfterFinish = true;
                if (_recorder is not null)
                {
                    await FinishAsync(keep: false);
                }
                else
                {
                    _discardOnFinish = true;
                }

                break;
        }
    }

    private void ContinueNavigation()
    {
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

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorCombo is null || WindowCombo is null || RefreshWindowsButton is null)
        {
            return;
        }

        var windowMode = SourceCombo.SelectedIndex == 1;
        var areaMode = SourceCombo.SelectedIndex == 2;
        MonitorCombo.Visibility = windowMode || areaMode ? Visibility.Collapsed : Visibility.Visible;
        WindowCombo.Visibility = windowMode ? Visibility.Visible : Visibility.Collapsed;
        RefreshWindowsButton.Visibility = windowMode ? Visibility.Visible : Visibility.Collapsed;
        if (StartButton is null)
        {
            return;
        }

        StartButton.Content = areaMode ? "Select area" : "Start";
        if (_recorder is null && !_areaArmed && !_areaPicking)
        {
            StatusText.Text = areaMode
                ? "Set the cursor, then select an area. The control bar appears next. Start is on that bar."
                : "Choose a monitor or window, then start. On a single display, move this window aside or it will appear in a monitor recording.";
        }
    }

    private void RefreshWindows_Click(object sender, RoutedEventArgs e) => LoadWindows();

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (SourceCombo.SelectedIndex == 2)
        {
            await BeginAreaSelectionAsync();
            return;
        }

        await StartAsync();
    }

    private void LoadMonitors()
    {
        ScreenGeometry.Refresh();
        _monitors.Clear();
        var index = 1;
        foreach (var monitor in ScreenGeometry.Monitors.Where(monitor => monitor.Handle != 0))
        {
            _monitors.Add(new MonitorChoice(monitor, $"Display {index} · {monitor.Width}×{monitor.Height}"));
            index++;
        }

        MonitorCombo.ItemsSource = null;
        MonitorCombo.ItemsSource = _monitors;
        if (_monitors.Count > 0 && MonitorCombo.SelectedIndex < 0)
        {
            MonitorCombo.SelectedIndex = 0;
        }
    }

    private void LoadWindows()
    {
        var exclude = WindowNative.GetWindowHandle(App.MainAppWindow);
        _windows.Clear();
        _windows.AddRange(WindowCatalog.List(exclude).Select(window => new WindowChoice(window.Handle, window.Title)));
        WindowCombo.ItemsSource = null;
        WindowCombo.ItemsSource = _windows;
        if (_windows.Count > 0 && WindowCombo.SelectedIndex < 0)
        {
            WindowCombo.SelectedIndex = 0;
        }
    }

    private async Task<bool> PrepareForNewRecordingAsync()
    {
        if (_recorder is not null || _areaPicking || _areaArmed)
        {
            return false;
        }

        if (HasUnsavedPreview)
        {
            var discard = new ContentDialog
            {
                Title = "Discard unsaved recording?",
                Content = "This video is not in Recordings. Starting a new one throws it away.",
                PrimaryButtonText = "Discard and record",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await discard.ShowAsync() != ContentDialogResult.Primary)
            {
                return false;
            }

            ClosePreview(deleteFile: true);
        }
        else if (_previewPath is not null)
        {
            ClosePreview(deleteFile: false);
        }

        return true;
    }

    private async Task BeginAreaSelectionAsync()
    {
        if (!await PrepareForNewRecordingAsync())
        {
            return;
        }

        _areaPicking = true;
        var token = ++_areaToken;
        SetRecordingState(active: true);
        using var abort = new CancellationTokenSource();
        ScreenshotSession.BeginPrepare(abort);
        System.Drawing.Bitmap? desktop = null;
        try
        {
            ScreenshotSession.HideWindow(App.MainAppWindow);
            await Task.Delay(200, abort.Token);
            ScreenGeometry.Refresh();
            desktop = await GraphicsCaptureService.CaptureAllMonitorsAsync(abort.Token);
            ScreenshotSession? session = null;
            session = new ScreenshotSession(
                App.MainAppWindow,
                desktop,
                ScreenGeometry.VirtualDesktop,
                ScreenGeometry.Monitors,
                CaptureOverlayKind.Region,
                onComplete: null,
                onCancel: () => DispatcherQueue.TryEnqueue(() =>
                {
                    if (token == _areaToken && _areaPicking)
                    {
                        EndAreaSelection("Area selection cancelled.");
                    }
                }),
                onRegion: selection =>
                {
                    var startX = session!.StartX;
                    var startY = session.StartY;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (token != _areaToken)
                        {
                            return;
                        }

                        try
                        {
                            ArmArea(selection, startX, startY);
                        }
                        catch (Exception ex)
                        {
                            EndAreaSelection(ex.Message);
                        }
                    });
                },
                hint: "Drag the area to record. It stays on the screen where you start. Release to set it. Esc cancels.",
                clipToStartMonitor: true);
            desktop = null;
            session.ShowOverlays();
        }
        catch (OperationCanceledException)
        {
            desktop?.Dispose();
            EndAreaSelection("Area selection cancelled.");
        }
        catch (Exception ex)
        {
            desktop?.Dispose();
            EndAreaSelection(ex.Message);
        }
        finally
        {
            ScreenshotSession.EndPrepare();
        }
    }

    private void ArmArea(System.Drawing.Rectangle selection, int startX, int startY)
    {
        _areaPicking = false;
        ScreenGeometry.Refresh();
        var monitor = ScreenGeometry.FromPoint(startX, startY);
        var clipped = System.Drawing.Rectangle.Intersect(selection, monitor.ToRectangle());
        if (monitor.Handle == 0 || clipped.Width < 16 || clipped.Height < 16)
        {
            EndAreaSelection("Drag a larger area. The smallest recording is 16 by 16.");
            return;
        }

        _useArea = true;
        _areaArmed = true;
        _areaMonitor = monitor;
        _cropX = clipped.X - monitor.X;
        _cropY = clipped.Y - monitor.Y;
        _cropWidth = clipped.Width & ~1;
        _cropHeight = clipped.Height & ~1;
        _targetWindow = 0;
        ShowBar();
        var detail = $"{_cropWidth}×{_cropHeight}";
        _areaBarOverlaps = BarOverlapsArea();
        if (_areaBarOverlaps)
        {
            detail += " · bar is inside the area";
        }

        _bar?.SetAwaitingStart(detail);
        StatusText.Text = _areaBarOverlaps
            ? $"Area {_cropWidth}×{_cropHeight}. The control bar sits inside that area, so it can appear in the video. Press Start on the bar."
            : $"Area {_cropWidth}×{_cropHeight}. Press Start on the control bar. The main window stays hidden until you stop.";
    }

    private void EndAreaSelection(string status)
    {
        _areaToken++;
        _areaPicking = false;
        _areaStarting = false;
        _areaArmed = false;
        _useArea = false;
        _areaBarOverlaps = false;
        var bar = _bar;
        _bar = null;
        bar?.CloseFromPage();
        SetRecordingState(active: false);
        if (StartButton is not null)
        {
            StartButton.Content = SourceCombo.SelectedIndex == 2 ? "Select area" : "Start";
        }

        ScreenshotSession.ShowWindow(App.MainAppWindow);
        StatusText.Text = status;
    }

    private async Task StartAreaCaptureAsync()
    {
        if (_recorder is not null || _areaStarting || !_useArea || _areaMonitor.Handle == 0)
        {
            return;
        }

        _areaStarting = true;
        var token = _areaToken;
        _bar?.SetBusy("Starting…");
        try
        {
            var item = GraphicsCaptureService.CreateItemForMonitor(_areaMonitor.Handle);
            Directory.CreateDirectory(PendingDirectory);
            _activePath = Path.Combine(PendingDirectory, $"Recording {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4");
            StatusText.Text = "Starting…";
            _targetWindow = 0;
            var started = await ScreenRecorder.StartAsync(
                item,
                _activePath,
                CursorSwitch.IsOn,
                new FrameCrop(_cropX, _cropY, _cropWidth, _cropHeight),
                SystemAudioSwitch.IsOn);
            if (token != _areaToken)
            {
                try
                {
                    await started.StopAsync(keep: false);
                }
                catch
                {
                    // The area was cancelled while capture was starting.
                }

                if (_activePath is not null && File.Exists(_activePath))
                {
                    try
                    {
                        File.Delete(_activePath);
                    }
                    catch
                    {
                        // The file is already gone or still in use.
                    }
                }

                _activePath = null;
                return;
            }

            _recorder = started;
            _areaArmed = false;
            SetRecordingState(active: true);
            TimerText.Text = "00:00";
            if (_bar is null)
            {
                ShowBar();
                var detail = $"{_cropWidth}×{_cropHeight}";
                if (_areaBarOverlaps)
                {
                    detail += " · bar is inside the area";
                }

                _bar?.SetDetail(detail);
            }

            _bar?.SetPaused(false);
            _elapsedTimer?.Dispose();
            _elapsedTimer = new System.Threading.Timer(_ => PublishElapsed(), null, 0, 200);
            StatusText.Text = SystemAudioSwitch.IsOn ? "Recording speakers." : "Recording.";
        }
        catch (Exception ex)
        {
            _recorder = null;
            var path = _activePath;
            _activePath = null;
            if (path is not null && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // The status below explains why recording did not start.
                }
            }

            EndAreaSelection(ex.Message);
        }
        finally
        {
            _areaStarting = false;
        }
    }

    private async Task StartAsync()
    {
        if (!await PrepareForNewRecordingAsync())
        {
            return;
        }

        try
        {
            var item = CreateItem();
            Directory.CreateDirectory(PendingDirectory);
            _activePath = Path.Combine(PendingDirectory, $"Recording {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4");
            StatusText.Text = "Starting…";
            _recorder = await ScreenRecorder.StartAsync(item, _activePath, CursorSwitch.IsOn, includeSystemAudio: SystemAudioSwitch.IsOn);
            SetRecordingState(active: true);
            TimerText.Text = "00:00";
            _elapsedTimer?.Dispose();
            _elapsedTimer = new System.Threading.Timer(_ => PublishElapsed(), null, 0, 200);
            ShowBar();
            if (SourceCombo.SelectedIndex == 0 || _targetWindow != WindowNative.GetWindowHandle(App.MainAppWindow))
            {
                ScreenshotSession.HideWindow(App.MainAppWindow);
            }

            StatusText.Text = SystemAudioSwitch.IsOn ? "Recording speakers." : "Recording.";
        }
        catch (Exception ex)
        {
            _recorder = null;
            var path = _activePath;
            _activePath = null;
            if (path is not null && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // The message below explains why recording did not start.
                }
            }

            StatusText.Text = ex.Message;
            SetRecordingState(active: false);
        }
    }

    private Windows.Graphics.Capture.GraphicsCaptureItem CreateItem()
    {
        if (SourceCombo.SelectedIndex == 0)
        {
            if (MonitorCombo.SelectedItem is not MonitorChoice monitor || monitor.Monitor.Handle == 0)
            {
                throw new InvalidOperationException("Choose a monitor.");
            }

            _targetWindow = 0;
            return GraphicsCaptureService.CreateItemForMonitor(monitor.Monitor.Handle);
        }

        if (WindowCombo.SelectedItem is not WindowChoice window || window.Handle == 0)
        {
            throw new InvalidOperationException("Choose a window.");
        }

        _targetWindow = window.Handle;
        return GraphicsCaptureService.CreateItemForWindow(window.Handle);
    }

    private async Task RestartAsync()
    {
        if (_recorder is null)
        {
            return;
        }

        StatusText.Text = "Restarting…";
        var area = _useArea;
        await FinishAsync(keep: false, restoreWindow: false);
        if (area)
        {
            await StartAreaCaptureAsync();
            return;
        }

        await StartAsync();
    }

    private async Task FinishAsync(bool keep, bool restoreWindow = true)
    {
        if (_finishInProgress)
        {
            return;
        }

        _finishInProgress = true;
        var recorder = _recorder;
        var path = _activePath;
        var finishedAt = recorder?.Elapsed.ToString(@"mm\:ss") ?? TimerText.Text;
        _recorder = null;
        _elapsedTimer?.Dispose();
        _elapsedTimer = null;
        TimerText.Text = finishedAt;
        try
        {
            if (recorder is not null)
            {
                recorder.StopCapture();
            }

            if (restoreWindow)
            {
                _useArea = false;
                _areaArmed = false;
                _areaBarOverlaps = false;
                ScreenshotSession.ShowWindow(App.MainAppWindow);
            }

            if (recorder is not null)
            {
                try
                {
                    await recorder.StopAsync(keep);
                }
                catch (Exception ex)
                {
                    StatusText.Text = ex.Message;
                    keep = false;
                }
            }

            if (!keep && path is not null && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    StatusText.Text = ex.Message;
                }
            }

            var bar = _bar;
            _bar = null;
            bar?.CloseFromPage();
        }
        finally
        {
            _finishInProgress = false;
        }
        SetRecordingState(active: false);
        if (restoreWindow)
        {
            ScreenshotSession.ShowWindow(App.MainAppWindow);
        }

        if (_discardOnFinish)
        {
            _discardOnFinish = false;
            keep = false;
            if (path is not null && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    StatusText.Text = ex.Message;
                }
            }
        }

        ShowRecordings();
        if (_closeWindowAfterFinish)
        {
            _closeWindowAfterFinish = false;
            _allowWindowClose = true;
            App.MainAppWindow.Close();
            return;
        }

        if (keep && path is not null && File.Exists(path) && new FileInfo(path).Length > 0)
        {
            ShowPreview(path, saved: false);
            StatusText.Text = "Review the recording, then Save.";
        }
        else if (keep)
        {
            StatusText.Text = "No video to review.";
        }
        else if (string.IsNullOrWhiteSpace(StatusText.Text) || StatusText.Text is "Recording." or "Paused." or "Restarting…" or "Review the recording, then Save.")
        {
            StatusText.Text = "Recording discarded.";
        }
    }

    private void ShowBar()
    {
        _bar = new RecordingBarWindow(this);
        PlaceBar();
        _bar.Activate();
    }

    private void PlaceBar()
    {
        if (_bar is null)
        {
            return;
        }

        ScreenGeometry.Refresh();
        if (_useArea && _areaMonitor.Width > 0)
        {
            var size = _bar.AppWindow.Size;
            var point = AreaBarPosition(_areaMonitor, new System.Drawing.Rectangle(_cropX, _cropY, _cropWidth, _cropHeight), Math.Max(size.Width, 320), Math.Max(size.Height, 96));
            _bar.AppWindow.Move(point);
            return;
        }

        var recorded = MonitorCombo.SelectedItem is MonitorChoice choice ? choice.Monitor : ScreenGeometry.Monitors.FirstOrDefault();
        var home = ScreenGeometry.Monitors.FirstOrDefault(monitor => monitor.Handle != 0 && monitor.Handle != recorded.Handle);
        if (home.Handle == 0)
        {
            home = recorded.Handle != 0 ? recorded : ScreenGeometry.Monitors[0];
        }

        _bar.AppWindow.Move(new Windows.Graphics.PointInt32(home.X + 24, home.Y + Math.Max(24, home.Height - 120)));
    }

    private bool BarOverlapsArea()
    {
        if (_bar is null)
        {
            return false;
        }

        var size = _bar.AppWindow.Size;
        var barWidth = Math.Max(size.Width, 1);
        var barHeight = Math.Max(size.Height, 1);
        var point = AreaBarPosition(_areaMonitor, new System.Drawing.Rectangle(_cropX, _cropY, _cropWidth, _cropHeight), barWidth, barHeight);
        var bar = new System.Drawing.Rectangle(point.X, point.Y, barWidth, barHeight);
        var area = new System.Drawing.Rectangle(_areaMonitor.X + _cropX, _areaMonitor.Y + _cropY, _cropWidth, _cropHeight);
        return bar.IntersectsWith(area);
    }

    private static Windows.Graphics.PointInt32 AreaBarPosition(MonitorRect monitor, System.Drawing.Rectangle crop, int barWidth, int barHeight)
    {
        var other = ScreenGeometry.Monitors.FirstOrDefault(item => item.Handle != 0 && item.Handle != monitor.Handle);
        if (other.Handle != 0)
        {
            return new Windows.Graphics.PointInt32(other.X + 24, other.Y + Math.Max(24, other.Height - barHeight - 24));
        }

        const int margin = 16;
        var below = monitor.Height - (crop.Y + crop.Height);
        var above = crop.Y;
        var right = monitor.Width - (crop.X + crop.Width);
        var left = crop.X;
        var belowFits = below >= barHeight + margin;
        var aboveFits = above >= barHeight + margin;
        int x;
        int y;
        if (belowFits || aboveFits)
        {
            var useBelow = belowFits && (!aboveFits || below >= above);
            y = useBelow
                ? monitor.Y + crop.Y + crop.Height + margin
                : monitor.Y + crop.Y - barHeight - margin;
            x = AlignOnMonitor(monitor.X, monitor.Width, crop.X, barWidth, margin);
        }
        else if (right >= barWidth + margin)
        {
            x = monitor.X + crop.X + crop.Width + margin;
            y = AlignOnMonitor(monitor.Y, monitor.Height, crop.Y, barHeight, margin);
        }
        else if (left >= barWidth + margin)
        {
            x = monitor.X + crop.X - barWidth - margin;
            y = AlignOnMonitor(monitor.Y, monitor.Height, crop.Y, barHeight, margin);
        }
        else
        {
            x = monitor.X + margin;
            y = monitor.Y + Math.Max(margin, monitor.Height - barHeight - margin);
        }

        return new Windows.Graphics.PointInt32(x, y);
    }

    private static int AlignOnMonitor(int origin, int span, int anchor, int bar, int margin)
    {
        var min = origin + margin;
        var max = origin + span - bar - margin;
        if (max < min)
        {
            return origin + margin;
        }

        return Math.Clamp(origin + anchor, min, max);
    }

    private void PublishElapsed()
    {
        var recorder = _recorder;
        if (recorder is null)
        {
            return;
        }

        var text = recorder.Elapsed.ToString(@"mm\:ss");
        DispatcherQueue.TryEnqueue(() => TimerText.Text = text);
        var bar = _bar;
        bar?.DispatcherQueue.TryEnqueue(() => bar.SetElapsed(text));
    }

    private void SetRecordingState(bool active)
    {
        StartButton.IsEnabled = !active;
        SourceCombo.IsEnabled = !active;
        MonitorCombo.IsEnabled = !active;
        WindowCombo.IsEnabled = !active;
        RefreshWindowsButton.IsEnabled = !active;
        CursorSwitch.IsEnabled = !active;
        SystemAudioSwitch.IsEnabled = !active;
    }

    private void ShowPreview(string path, bool saved)
    {
        _previewPath = path;
        Playback.SetHoverSource(path);
        _previewSaved = saved;
        NameBox.Text = Path.GetFileNameWithoutExtension(path);
        NameBox.IsEnabled = !saved;
        SavePreviewButton.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        DiscardPreviewButton.Content = saved ? "Delete" : "Discard";
        ClosePreviewButton.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        PreviewHint.Text = saved
            ? "Saved in the library, in All media and the Recordings folder."
            : "Not saved. Watch it, trim the ends if you want, then Save. Nothing is in the library until you save.";
        TrimPreviewButton.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        TrimPreviewButton.IsEnabled = false;
        if (_previewTrimming)
        {
            Playback.EndTrim();
            _previewTrimming = false;
        }
        SavedListHost.Visibility = Visibility.Collapsed;
        if (VideoHost.Child is null)
        {
            VideoHost.Child = VideoView;
        }

        PreviewHost.Visibility = Visibility.Visible;
        VideoHost.Visibility = Visibility.Visible;
        ResetPreviewDuration();
        _previewClock?.Start();
        _playWhenReady = path;
        if (_player is not null)
        {
            PlayFile(path);
        }
    }

    private void ClosePreview(bool deleteFile)
    {
        var path = _previewPath;
        DisposePlayer();
        ResetPreviewDuration();
        _previewPath = null;
        Playback.SetHoverSource(null);
        _previewSaved = false;
        _playWhenReady = null;
        PreviewHost.Visibility = Visibility.Collapsed;
        SavedListHost.Visibility = Visibility.Visible;
        if (deleteFile && path is not null && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        ShowRecordings();
    }

    private async void SavePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewSaved || _previewPath is null || !File.Exists(_previewPath))
        {
            return;
        }

        var name = NormalizeRecordingName(NameBox.Text);
        if (name is null)
        {
            StatusText.Text = "Enter a file name before saving.";
            return;
        }

        var source = _previewPath;
        var originalPending = source;
        MediaItem saved;
        try
        {
            StopPlayback();
            DisposePlayer();
            if (HasPreviewTrim)
            {
                var cut = source + ".cut.mp4";
                StatusText.Text = "Saving the trimmed recording…";
                await VideoTrimmer.TrimAsync(source, cut, TimeSpan.FromMilliseconds(_previewTrimStartMs), TimeSpan.FromMilliseconds(_previewTrimEndMs));
                source = cut;
            }

            source = RenamePendingFile(source, name);
            _previewPath = source;
            saved = App.MediaLibrary.ImportMedia(source, RecordingsAlbum);
            try
            {
                if (!string.Equals(source, saved.FilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(source))
                {
                    File.Delete(source);
                }

                if (!string.Equals(originalPending, saved.FilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(originalPending))
                {
                    File.Delete(originalPending);
                }
            }
            catch (IOException)
            {
                // The library copy is already saved. A locked pending file can be removed later.
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            if (File.Exists(source))
            {
                ShowPreview(source, saved: false);
            }

            return;
        }

        ShowPreview(saved.FilePath, saved: true);
        NameBox.Text = Path.GetFileNameWithoutExtension(saved.DisplayName);
        StatusText.Text = "Saved in the library.";
        ShowRecordings();
    }

    private void DiscardPreview_Click(object sender, RoutedEventArgs e)
    {
        var saved = _previewSaved;
        ClosePreview(deleteFile: true);
        StatusText.Text = saved ? "Recording deleted." : "Recording discarded.";
    }

    private void ClosePreview_Click(object sender, RoutedEventArgs e) => ClosePreview(deleteFile: false);

    private void BackTen_Click(object sender, RoutedEventArgs e) => SkipPreview(-10_000);

    private void ForwardTen_Click(object sender, RoutedEventArgs e) => SkipPreview(10_000);

    private void SkipPreview(long deltaMs)
    {
        if (_player is null || _previewPath is null)
        {
            return;
        }

        var wasPlaying = _player.IsPlaying;
        if (_previewEnded)
        {
            _previewEnded = false;
            _player.Stop();
        }

        var length = _hasPreviewDuration ? _previewDurationMs : Math.Max(_player.Length, 0);
        var next = Math.Max(0, _player.Time + deltaMs);
        if (length > 0 && next > length)
        {
            next = length;
        }

        _player.Time = next;
        if (wasPlaying)
        {
            if (!_player.IsPlaying)
            {
                _player.Play();
            }
        }
        else if (_player.IsPlaying)
        {
            _player.SetPause(true);
        }

        UpdatePreviewClock();
        UpdatePlayButton();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (e.NewValue > 0)
        {
            _lastPreviewVolume = e.NewValue;
        }

        ApplyPreviewVolume();
    }

    private void PreviewMute_Click(object sender, RoutedEventArgs e)
    {
        if (Playback.VolumeSlider.Value > 0)
        {
            _lastPreviewVolume = Playback.VolumeSlider.Value;
            Playback.VolumeSlider.Value = 0;
        }
        else
        {
            Playback.VolumeSlider.Value = _lastPreviewVolume <= 0 ? 80 : _lastPreviewVolume;
        }

        ApplyPreviewVolume();
    }

    private void ApplyPreviewVolume()
    {
        Playback.MuteIcon.Glyph = Playback.VolumeSlider.Value <= 0 ? "\uE74F" : "\uE767";
        if (_player is null)
        {
            return;
        }

        _player.Mute = Playback.VolumeSlider.Value <= 0;
        _player.Volume = (int)Math.Round(Playback.VolumeSlider.Value);
    }

    private void PreviewSpeed_Changed(object sender, SelectionChangedEventArgs e) => ApplyPreviewRate();

    private void ApplyPreviewRate()
    {
        if (Playback.SpeedCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && float.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rate)
            && rate > 0)
        {
            _player?.SetRate(rate);
        }
    }

    private async void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainAppWindow is not MainWindow window)
        {
            return;
        }

        if (window.IsFullScreen)
        {
            await ExitPreviewFullScreenAsync();
            return;
        }

        var resumeMs = _player?.Time ?? 0;
        var resumePlaying = _player?.IsPlaying == true;
        DisposePlayer();
        PageRoot.Padding = new Thickness(0);
        PageRoot.RowSpacing = 0;
        HeaderPanel.Visibility = Visibility.Collapsed;
        SetupCard.Visibility = Visibility.Collapsed;
        PreviewDetails.Visibility = Visibility.Collapsed;
        await window.SetFullScreenAsync(true);
        await WaitForLayoutAsync();
        _previewFullScreen = true;
        Playback.FullScreenIcon.Glyph = "\uE73F";
        window.AppWindow.Changed -= PreviewWindow_Changed;
        window.AppWindow.Changed += PreviewWindow_Changed;
        ResumePreview(resumeMs, resumePlaying);
    }

    private async Task ExitPreviewFullScreenAsync()
    {
        if (_fullscreenTransition)
        {
            return;
        }

        _fullscreenTransition = true;
        try
        {
            var resumeMs = _player?.Time ?? 0;
            var resumePlaying = _player?.IsPlaying == true;
            if (App.MainAppWindow is MainWindow window)
            {
                window.AppWindow.Changed -= PreviewWindow_Changed;
            }

            DisposePlayer();
            if (App.MainAppWindow is MainWindow host && host.IsFullScreen)
            {
                await host.SetFullScreenAsync(false);
            }

            RestorePreviewChrome();
            await WaitForLayoutAsync();
            ResumePreview(resumeMs, resumePlaying);
        }
        finally
        {
            _fullscreenTransition = false;
        }
    }

    private void PreviewWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange || sender.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            return;
        }

        sender.Changed -= PreviewWindow_Changed;
        DispatcherQueue.TryEnqueue(() => _ = ExitPreviewFullScreenAsync());
    }

    private void RestorePreviewChrome()
    {
        _previewFullScreen = false;
        PageRoot.Padding = new Thickness(24, 8, 24, 24);
        PageRoot.RowSpacing = 16;
        HeaderPanel.Visibility = Visibility.Visible;
        SetupCard.Visibility = Visibility.Visible;
        PreviewDetails.Visibility = Visibility.Visible;
        Playback.FullScreenIcon.Glyph = "\uE740";
    }

    private void ResumePreview(long positionMs, bool playing)
    {
        if (string.IsNullOrWhiteSpace(_previewPath))
        {
            return;
        }

        _seekAfterStart = positionMs > 0 ? positionMs : null;
        _pauseAfterStart = !playing;
        _playWhenReady = _previewPath;
        _previewClock?.Start();
        if (VideoHost.Child is null)
        {
            VideoHost.Child = VideoView;
        }

        VideoView.Width = 0;
        VideoView.Height = 0;
        DispatcherQueue.TryEnqueue(() =>
        {
            VideoView.Width = double.NaN;
            VideoView.Height = double.NaN;
        });
        if (_player is not null)
        {
            PlayFile(_previewPath);
        }
    }

    private Task WaitForLayoutAsync()
    {
        var done = new TaskCompletionSource();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => done.TrySetResult());
        return done.Task;
    }

    private void PlayPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null || _previewPath is null)
        {
            return;
        }

        if (_player.IsPlaying)
        {
            _player.SetPause(true);
        }
        else if (_previewEnded || _previewMedia is null)
        {
            _previewEnded = false;
            PlayFile(_previewPath);
        }
        else
        {
            if (_previewTrimming && (_player.Time < _previewTrimStartMs || _player.Time >= _previewTrimEndMs))
            {
                _player.Time = _previewTrimStartMs;
            }

            _player.Play();
        }

        UpdatePlayButton();
    }

    private void VideoView_Initialized(object sender, InitializedEventArgs e)
    {
        _previewMedia?.Dispose();
        _previewMedia = null;
        _libVlc?.Dispose();
        _player?.Dispose();
        _libVlc = new LibVLC(enableDebugLogs: false, e.SwapChainOptions);
        _player = new VlcMediaPlayer(_libVlc);
        _player.Playing += (_, _) => DispatcherQueue.TryEnqueue(OnPreviewPlaying);
        _player.Paused += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayButton);
        _player.Stopped += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayButton);
        _player.LengthChanged += Player_LengthChanged;
        _player.EndReached += (_, _) => DispatcherQueue.TryEnqueue(MarkPreviewEnded);
        VideoView.MediaPlayer = _player;
        ApplyPreviewVolume();
        ApplyPreviewRate();
        if (!string.IsNullOrWhiteSpace(_playWhenReady))
        {
            PlayFile(_playWhenReady);
        }
    }

    private void PlayFile(string path)
    {
        if (_libVlc is null || _player is null || !File.Exists(path))
        {
            return;
        }

        _previewEnded = false;
        _player.Stop();
        _previewMedia?.Dispose();
        _previewMedia = new Media(_libVlc, path, FromType.FromPath);
        _previewClock?.Start();
        _player.Play(_previewMedia);
        ApplyPreviewRate();
        UpdatePlayButton();
        UpdatePreviewClock();
    }

    private void StopPlayback()
    {
        if (_player is not null)
        {
            _player.Stop();
        }

        _previewMedia?.Dispose();
        _previewMedia = null;
        UpdatePlayButton();
    }

    private void DisposePlayer()
    {
        if (_player is null && _libVlc is null && _previewMedia is null)
        {
            _previewClock?.Stop();
            return;
        }

        _previewClock?.Stop();
        var player = _player;
        var library = _libVlc;
        _player = null;
        _libVlc = null;
        _holdPauseUntilFrame = false;
        _pauseAfterStart = false;
        if (player is not null)
        {
            player.LengthChanged -= Player_LengthChanged;
            player.TimeChanged -= HoldPausedFrame;
            try
            {
                player.Stop();
            }
            catch (Exception)
            {
                // The player can already be stopped when the page is leaving.
            }
        }

        _previewMedia?.Dispose();
        _previewMedia = null;
        if (VideoView is not null)
        {
            VideoView.MediaPlayer = null;
        }

        player?.Dispose();
        library?.Dispose();
        if (VideoHost.Child is not null)
        {
            VideoHost.Child = null;
        }

        UpdatePlayButton();
    }

    private void OnPreviewPlaying()
    {
        if (_player is null)
        {
            return;
        }

        if (_seekAfterStart is long position)
        {
            _player.Time = position;
            _seekAfterStart = null;
        }

        if (_pauseAfterStart)
        {
            _holdPauseUntilFrame = true;
            _player.TimeChanged -= HoldPausedFrame;
            _player.TimeChanged += HoldPausedFrame;
        }

        UpdatePlayButton();
    }

    private void HoldPausedFrame(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_player is null || !_holdPauseUntilFrame)
            {
                return;
            }

            _holdPauseUntilFrame = false;
            _pauseAfterStart = false;
            _player.TimeChanged -= HoldPausedFrame;
            _player.SetPause(true);
            _player.NextFrame();
            if (_player.IsPlaying)
            {
                _player.SetPause(true);
            }

            UpdatePlayButton();
            UpdatePreviewClock();
        });
    }

    private void UpdatePlayButton()
    {
        Playback.PlayIcon.Glyph = _player?.IsPlaying == true ? "\uE769" : "\uE768";
    }

    private void Player_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        => DispatcherQueue.TryEnqueue(() => ApplyPreviewDuration(e.Length));

    private void MarkPreviewEnded()
    {
        _previewEnded = true;
        if (_hasPreviewDuration)
        {
            Playback.PositionText.Text = FormatPreviewMs(_previewDurationMs);
            _updatingPreviewSeek = true;
            Playback.SeekSlider.Value = Playback.SeekSlider.Maximum;
            _updatingPreviewSeek = false;
        }

        UpdatePlayButton();
    }

    private void PreviewSeek_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_hasPreviewDuration)
        {
            _previewDragging = true;
        }
    }

    private void PreviewSeek_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewDragging)
        {
            return;
        }

        _previewDragging = false;
        if (_player is null || !_hasPreviewDuration || _previewDurationMs <= 0)
        {
            return;
        }

        var time = (long)(Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * _previewDurationMs);
        if (_previewTrimming)
        {
            time = Math.Clamp(time, _previewTrimStartMs, _previewTrimEndMs);
        }

        if (_previewEnded && _previewPath is not null)
        {
            PlayFile(_previewPath);
        }

        _player.Time = time;
        if (!_player.IsPlaying)
        {
            _player.Play();
        }

        UpdatePreviewClock();
    }

    private void PreviewSeek_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingPreviewSeek || !_previewDragging || !_hasPreviewDuration)
        {
            return;
        }

        Playback.PositionText.Text = FormatPreviewMs((long)(Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * _previewDurationMs));
    }

    private void UpdatePreviewClock()
    {
        if (_player is null)
        {
            return;
        }

        ApplyPreviewDuration(_player.Length);
        if (_previewTrimming && _player.IsPlaying && _previewTrimEndMs > _previewTrimStartMs && _player.Time >= _previewTrimEndMs - 80)
        {
            _player.Time = _previewTrimStartMs;
        }

        if (_previewDragging)
        {
            return;
        }

        var time = _previewEnded && _hasPreviewDuration ? _previewDurationMs : Math.Max(_player.Time, 0);
        Playback.PositionText.Text = FormatPreviewMs(time);
        if (!_hasPreviewDuration || _previewDurationMs <= 0)
        {
            return;
        }

        _updatingPreviewSeek = true;
        Playback.SeekSlider.Value = Math.Clamp(time / (double)_previewDurationMs * Playback.SeekSlider.Maximum, 0, Playback.SeekSlider.Maximum);
        _updatingPreviewSeek = false;
    }

    private void ApplyPreviewDuration(long durationMs)
    {
        if (_hasPreviewDuration || durationMs <= 0 || durationMs > MaxPreviewDurationMs || durationMs >= int.MaxValue)
        {
            return;
        }

        _hasPreviewDuration = true;
        _previewDurationMs = durationMs;
        Playback.SetHoverDuration(durationMs);
        Playback.DurationText.Text = FormatPreviewMs(durationMs);
        Playback.SeekSlider.IsEnabled = true;
        if (!_previewSaved)
        {
            TrimPreviewButton.IsEnabled = true;
        }

        if (_previewTrimming)
        {
            _previewTrimEndMs = durationMs;
            UpdatePreviewTrimHint();
        }
    }

    private void ResetPreviewDuration()
    {
        _hasPreviewDuration = false;
        _previewDurationMs = 0;
        _previewDragging = false;
        _previewEnded = false;
        Playback.SeekSlider.IsEnabled = false;
        Playback.SeekSlider.Value = 0;
        Playback.DurationText.Text = "--:--";
        Playback.PositionText.Text = "00:00";
        Playback.SetHoverDuration(0);
    }

    private bool HasPreviewTrim =>
        _previewTrimming && _previewDurationMs > 0 && (_previewTrimStartMs > 50 || _previewDurationMs - _previewTrimEndMs > 50);

    private void TrimPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewSaved || !_hasPreviewDuration || _previewDurationMs <= 0)
        {
            return;
        }

        if (_previewTrimming)
        {
            _previewTrimming = false;
            Playback.EndTrim();
            TrimPreviewButton.Content = "Trim";
            PreviewHint.Text = "Not saved. Watch it, trim the ends if you want, then Save. Nothing is in the library until you save.";
            return;
        }

        _previewTrimming = true;
        _previewTrimStartMs = 0;
        _previewTrimEndMs = _previewDurationMs;
        TrimPreviewButton.Content = "Clear trim";
        Playback.BeginTrim();
        UpdatePreviewTrimHint();
    }

    private void ApplyPreviewTrimFromBar()
    {
        if (!_previewTrimming || _previewDurationMs <= 0)
        {
            return;
        }

        _previewTrimStartMs = (long)(Playback.TrimStart * _previewDurationMs);
        _previewTrimEndMs = (long)(Playback.TrimEnd * _previewDurationMs);
        if (_player is not null && (_player.Time < _previewTrimStartMs || _player.Time > _previewTrimEndMs))
        {
            _player.Time = _previewTrimStartMs;
        }

        UpdatePreviewTrimHint();
        UpdatePreviewClock();
    }

    private void SeekPreviewFraction(double fraction)
    {
        if (_player is null || _previewDurationMs <= 0)
        {
            return;
        }

        var time = (long)(fraction * _previewDurationMs);
        _player.Time = _previewTrimming ? Math.Clamp(time, _previewTrimStartMs, _previewTrimEndMs) : time;
        UpdatePreviewClock();
    }

    private void UpdatePreviewTrimHint()
    {
        var keep = Math.Max(0, _previewTrimEndMs - _previewTrimStartMs);
        PreviewHint.Text = $"Keep {FormatPreviewMs(keep)}, from {FormatPreviewMs(_previewTrimStartMs)} to {FormatPreviewMs(_previewTrimEndMs)}. Save stores this cut.";
    }

    private static string FormatPreviewMs(long durationMs)
    {
        if (durationMs < 0 || durationMs > MaxPreviewDurationMs)
        {
            return "00:00";
        }

        var totalSeconds = durationMs / 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }

    private static string RenamePendingFile(string source, string fileName)
    {
        var directory = Path.GetDirectoryName(source);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return source;
        }

        var target = Path.Combine(directory, fileName);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        if (File.Exists(target))
        {
            target = AvailablePath(directory, fileName);
        }

        File.Move(source, target);
        return target;
    }

    private static string? NormalizeRecordingName(string value)
    {
        var name = value.Trim();
        foreach (var character in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(character, '_');
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? name : name + ".mp4";
    }

    private static string AvailablePath(string directory, string fileName)
    {
        var destination = Path.Combine(directory, fileName);
        if (!File.Exists(destination))
        {
            return destination;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 1; ; index++)
        {
            destination = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(destination))
            {
                return destination;
            }
        }
    }

    private void ShowRecordings()
    {
        AdoptLooseRecordings();
        FolderStrip.Children.Clear();
        foreach (var folder in App.MediaLibrary.GetFolders().Where(folder => !folder.IsSystem))
        {
            var chip = new Border
            {
                Padding = new Thickness(14, 8, 14, 8),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                AllowDrop = true,
                Tag = folder.Name,
                Child = new TextBlock { Text = folder.Name }
            };
            chip.DragOver += FolderChip_DragOver;
            chip.Drop += FolderChip_Drop;
            FolderStrip.Children.Add(chip);
        }

        var items = App.MediaLibrary.GetItems(RecordingsAlbum);
        EmptyRecordingsText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordingGrid.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecordingGrid.ItemsSource = items;
    }

    private void RecordingGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<MediaItem>().Select(item => item.FilePath);
        e.Data.SetText(string.Join('\n', paths));
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void RecordingGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ItemFromSource(e.OriginalSource) is MediaItem item)
        {
            Play(item);
        }
    }

    private void RecordingGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ItemFromSource(e.OriginalSource) is not MediaItem item)
        {
            return;
        }

        e.Handled = true;
        var rename = new MenuFlyoutItem { Text = "Rename" };
        rename.Click += async (_, _) => await RenameRecordingAsync(item);
        var delete = new MenuFlyoutItem { Text = "Delete" };
        delete.Click += async (_, _) => await DeleteRecordingAsync(item);
        var flyout = new MenuFlyout();
        flyout.Items.Add(rename);
        flyout.Items.Add(delete);
        flyout.ShowAt(RecordingGrid, e.GetPosition(RecordingGrid));
    }

    private static MediaItem? ItemFromSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is MediaCard { Item: { } cardItem })
            {
                return cardItem;
            }

            if (current is GridViewItem { Content: MediaItem item })
            {
                return item;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void AdoptLooseRecordings()
    {
        if (!Directory.Exists(RecordingsDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(RecordingsDirectory, "*.mp4").ToArray())
        {
            try
            {
                var saved = App.MediaLibrary.ImportMedia(path, RecordingsAlbum);
                if (!string.Equals(path, saved.FilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }
    }

    private void FolderChip_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.Text)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;
    }

    private async void FolderChip_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string folder || !e.DataView.Contains(StandardDataFormats.Text))
        {
            return;
        }

        var text = await e.DataView.GetTextAsync();
        var paths = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            App.MediaLibrary.MoveItems(paths, folder);
            StatusText.Text = $"Added to '{folder}'. The file stays in All media.";
            ShowRecordings();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task RenameRecordingAsync(MediaItem item)
    {
        var box = new TextBox { Text = Path.GetFileNameWithoutExtension(item.DisplayName) };
        var dialog = new ContentDialog
        {
            Title = "Rename",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            if (string.Equals(_previewPath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                StopPlayback();
            }

            var renamed = App.MediaLibrary.RenameItem(item.FilePath, box.Text);
            PlaybackBookmarks.Move(item.FilePath, renamed.FilePath);
            MediaFavorites.Move(item.FilePath, renamed.FilePath);
            ScreenshotTextIndex.Move(item.FilePath, renamed.FilePath);
            if (string.Equals(_previewPath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                ShowPreview(renamed.FilePath, saved: true);
            }

            StatusText.Text = $"Renamed to {renamed.DisplayName}.";
            ShowRecordings();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task DeleteRecordingAsync(MediaItem item)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete from library?",
            Content = $"Delete {item.DisplayName}? This removes the file from All media and every folder.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            if (string.Equals(_previewPath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                ClosePreview(deleteFile: false);
            }

            App.MediaLibrary.DeleteItems([item.FilePath]);
            StatusText.Text = "Recording deleted.";
            ShowRecordings();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void Play(MediaItem item)
    {
        if (_player is not null || _libVlc is not null)
        {
            DisposePlayer();
        }

        Frame.Navigate(typeof(VideoPlayerPage), item);
    }

    private sealed record MonitorChoice(MonitorRect Monitor, string Label);

    private sealed record WindowChoice(nint Handle, string Title);
}
