using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Views;
using Windows.Graphics;

namespace PersonalMediaPlayer.App;

public sealed partial class MainWindow : Window
{
    private bool _paneOpenBeforeFullScreen = true;
    private readonly GlobalHotkeyService? _hotkeys;

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
        => _hotkeys?.Dispose();

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (ScreenshotSession.TryHandleHostClose())
        {
            args.Cancel = true;
            return;
        }

        if (Shell.ContentFrame.Content is RecordingsPage recordings && recordings.TryHandleHostClose())
        {
            args.Cancel = true;
        }
    }

    internal void SyncNavigationSelection() => Shell.SyncNavSelection();

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
