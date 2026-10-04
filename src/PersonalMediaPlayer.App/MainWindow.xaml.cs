using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;
using PersonalMediaPlayer.App.Views;
using Windows.Graphics;

namespace PersonalMediaPlayer.App;

public sealed partial class MainWindow : Window
{
    private const int SwRestore = 9;

    private bool _paneOpenBeforeFullScreen = true;
    private readonly GlobalHotkeyService? _hotkeys;
    private int _handoffGeneration;
    private CancellationTokenSource? _handoffCheck;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        }

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1280, 840));
        AppWindow.Closing += AppWindow_Closing;
        Closed += MainWindow_Closed;
        try
        {
            _hotkeys = new GlobalHotkeyService(DispatcherQueue, OnCaptureHotkey);
        }
        catch
        {
            _hotkeys = null;
        }
    }

    private static void OnCaptureHotkey(int id)
    {
        var entry = CaptureHotkeys.All.FirstOrDefault(item => item.Id == id);
        if (entry.Id == 0)
        {
            return;
        }

        NavigationHelper.RequestCapture?.Invoke(entry.Kind);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _handoffGeneration++;
        _handoffCheck?.Cancel();
        _handoffCheck?.Dispose();
        _handoffCheck = null;
        App.CancelHandoffListen();
        _hotkeys?.Dispose();
        PlayQueue.Clear();
    }

    internal void ReceiveHandoff(string? argument)
    {
        BringToFront();
        if (string.IsNullOrWhiteSpace(argument) || !StreamHandoff.TryReadLaunch(argument, out var launch))
        {
            return;
        }

        var generation = ++_handoffGeneration;
        _handoffCheck?.Cancel();
        _handoffCheck?.Dispose();
        _handoffCheck = null;
        if (launch.Url is not Uri url)
        {
            ShowHandoff(StreamLink.EnterAddressMessage, InfoBarSeverity.Error);
            return;
        }

        _handoffCheck = new CancellationTokenSource();
        var token = _handoffCheck.Token;
        ShowHandoff(launch.Resolve ? "Opening…" : "Checking the link…", InfoBarSeverity.Informational);
        _ = FinishHandoffAsync(generation, url, launch.Referrer, launch.StartMs, launch.Resolve, token);
    }

    internal void OpenResolvedPage(Uri page, long? startMs)
    {
        if (Shell.TryKeepPlayingPage(page))
        {
            BringToFront();
            return;
        }

        BringToFront();
        var generation = ++_handoffGeneration;
        _handoffCheck?.Cancel();
        _handoffCheck?.Dispose();
        _handoffCheck = new CancellationTokenSource();
        ShowHandoff("Opening…", InfoBarSeverity.Informational);
        _ = FinishHandoffAsync(generation, page, null, startMs, true, _handoffCheck.Token);
    }

    internal void OpenSavedStream(SavedWord word)
    {
        if (word.TimeMs is long && StreamLink.TryNormalize(word.PageUrl, out var playing) && Shell.TryFocusPlayingPage(playing, word))
        {
            BringToFront();
            return;
        }

        if (word.TimeMs is not long time || !StreamLink.TryNormalize(word.PageUrl, out var page))
        {
            ShowHandoff("This word has no video address.", InfoBarSeverity.Warning);
            return;
        }

        BringToFront();
        var generation = ++_handoffGeneration;
        _handoffCheck?.Cancel();
        _handoffCheck?.Dispose();
        _handoffCheck = new CancellationTokenSource();
        ShowHandoff(word.ResolvePage ? "Opening…" : "Checking the link…", InfoBarSeverity.Informational);
        _ = FinishHandoffAsync(generation, page, null, time, word.ResolvePage, _handoffCheck.Token, word);
    }

    private async Task FinishHandoffAsync(
        int generation,
        Uri url,
        Uri? referrer,
        long? startMs,
        bool resolve,
        CancellationToken cancellationToken,
        SavedWord? focus = null)
    {
        try
        {
            if (resolve)
            {
                await OpenResolvedAsync(generation, url, startMs, cancellationToken, focus);
                return;
            }

            var result = await StreamLink.CheckAsync(url, cancellationToken);
            if (generation != _handoffGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (result.Status != StreamCheckStatus.Media)
            {
                ShowHandoff(result.Message, InfoBarSeverity.Error);
                return;
            }

            HandoffBar.IsOpen = false;
            var directName = focus?.SourceName is string source && !string.IsNullOrWhiteSpace(source)
                ? source.Trim()
                : StreamLink.DisplayName(result.Url);
            Shell.OpenStream(new StreamOpenRequest(result.Url, directName, referrer, startMs, Focus: focus));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (generation == _handoffGeneration)
            {
                var message = resolve && ex is InvalidOperationException ? ex.Message : StreamLink.OpenFailedMessage;
                ShowHandoff(message, InfoBarSeverity.Error);
            }
        }
    }

    private async Task OpenResolvedAsync(int generation, Uri page, long? startMs, CancellationToken cancellationToken, SavedWord? focus = null)
    {
        var progress = new Progress<string>(text =>
        {
            if (generation != _handoffGeneration || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (DispatcherQueue.HasThreadAccess)
            {
                ShowHandoff(text, InfoBarSeverity.Informational);
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation == _handoffGeneration)
                {
                    ShowHandoff(text, InfoBarSeverity.Informational);
                }
            });
        });
        var choice = await Task.Run(
            () => YoutubeDownloader.ResolvePlaybackAsync(page.AbsoluteUri, progress, cancellationToken),
            cancellationToken);
        if (generation != _handoffGeneration || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        HandoffBar.IsOpen = false;
        Shell.OpenStream(new StreamOpenRequest(
            choice.Media,
            choice.Title,
            page,
            startMs,
            page,
            choice.Audio,
            choice.Subtitles,
            choice.Quality,
            focus,
            choice.Thumbnail,
            choice.Chapters));
    }

    private void ShowHandoff(string message, InfoBarSeverity severity)
    {
        HandoffBar.Severity = severity;
        HandoffBar.Message = message;
        HandoffBar.IsOpen = true;
    }

    private void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }

        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && AttachThreadInput(foregroundThread, currentThread, true);
        SetForegroundWindow(hwnd);
        if (attached)
        {
            AttachThreadInput(foregroundThread, currentThread, false);
        }

        Activate();
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        Shell.RememberPlayer();
        if (ScreenshotSession.TryHandleHostClose())
        {
            args.Cancel = true;
            return;
        }

        if (Shell.ContentFrame.Content is RecordingsPage recordings && recordings.TryHandleHostClose())
        {
            args.Cancel = true;
            return;
        }

        if (Shell.ContentFrame.Content is MergePage merge && merge.TryHandleHostClose())
        {
            args.Cancel = true;
            return;
        }

        if (Shell.ContentFrame.Content is VideoEditorPage editor && editor.TryHandleHostClose())
        {
            args.Cancel = true;
            return;
        }

        if (Shell.ContentFrame.Content is CapturePage capture && capture.TryHandleHostClose())
        {
            args.Cancel = true;
            return;
        }

        if (Shell.ContentFrame.Content is MediaPreviewPage preview && preview.TryHandleHostClose())
        {
            args.Cancel = true;
            return;
        }

        DownloadQueueHub.PauseForExit();
    }

    internal void SyncNavigationSelection() => Shell.SyncNavSelection();

    internal bool ShowPlayer(object? parameter) => Shell.ShowPlayer(parameter);

    internal void ClosePlayer() => Shell.ClosePlayer();

    internal void ExpandPlayer() => Shell.ExpandPlayer();

    internal bool LeavePlayerFor(Type pageType, object? parameter, string? navTag)
        => Shell.LeavePlayerFor(pageType, parameter, navTag);

    internal void CompletePlayerLeave(bool back, Type? pageType, object? parameter)
        => Shell.CompletePlayerLeave(back, pageType, parameter);

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        Shell.TogglePane();
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        Shell.GoBack();
    }

    public bool IsFullScreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    public void SetFullScreen(bool enabled) => _ = SetFullScreenAsync(enabled);

    public Task SetFullScreenAsync(bool enabled, Action? whileCovered = null)
    {
        if (enabled == IsFullScreen)
        {
            whileCovered?.Invoke();
            return Task.CompletedTask;
        }

        if (enabled)
        {
            _paneOpenBeforeFullScreen = Shell.IsPaneOpen;
            whileCovered?.Invoke();
            TitleBarHost.Visibility = Visibility.Collapsed;
            TitleBarHost.Height = 0;
            Shell.SetPaneVisible(false);
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            RestoreTitleBar();
            Shell.EnsurePaneAvailable();
            Shell.IsPaneOpen = _paneOpenBeforeFullScreen;
            whileCovered?.Invoke();
        }

        return Task.CompletedTask;
    }

    private void RestoreTitleBar()
    {
        TitleBarHost.Visibility = Visibility.Visible;
        TitleBarHost.Height = 48;
        TitleBarHost.Opacity = 1;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }
}
