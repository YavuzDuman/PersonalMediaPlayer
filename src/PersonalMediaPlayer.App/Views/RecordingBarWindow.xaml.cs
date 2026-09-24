using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.Helpers;
using Windows.Graphics;
using WinRT.Interop;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class RecordingBarWindow : Window
{
    private const int BarWidthDips = 820;
    private const int BarHeightDips = 88;
    private readonly RecordingsPage _page;
    private bool _allowClose;
    private bool _prompting;
    private bool _notified;

    public RecordingBarWindow(RecordingsPage page)
    {
        _page = page;
        InitializeComponent();
        ThemeSettings.Apply(this, ThemeSettings.Load());
        AppWindow.Closing += Bar_Closing;
        Closed += (_, _) => NotifyClosed(discard: false);
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.SetBorderAndTitleBar(true, true);
        AppWindow.SetPresenter(presenter);
        var scale = DpiScale();
        AppWindow.ResizeClient(new SizeInt32(
            (int)Math.Ceiling(BarWidthDips * scale),
            (int)Math.Ceiling(BarHeightDips * scale)));
        AppWindow.Title = "Recording";
    }

    private double DpiScale()
    {
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        return dpi >= 96 ? dpi / 96.0 : 1;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    public void CloseFromPage()
    {
        if (_notified)
        {
            return;
        }

        _allowClose = true;
        _notified = true;
        Close();
    }

    private void Bar_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        if (_page.IsAwaitingAreaStart)
        {
            _allowClose = true;
            NotifyClosed(discard: true);
            Close();
            return;
        }

        if (_prompting)
        {
            return;
        }

        _prompting = true;
        _ = ConfirmDiscardAsync();
    }

    private async Task ConfirmDiscardAsync()
    {
        try
        {
            if (Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Discard recording?",
                Content = "This recording is not saved. Discard it?",
                PrimaryButtonText = "Discard",
                CloseButtonText = "Keep recording",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            _allowClose = true;
            NotifyClosed(discard: true);
            Close();
        }
        finally
        {
            _prompting = false;
        }
    }

    private void NotifyClosed(bool discard)
    {
        if (_notified)
        {
            return;
        }

        _notified = true;
        _page.NotifyBarClosed(discard);
    }

    public void SetElapsed(string text) => TimerText.Text = text;

    public void SetDetail(string detail)
    {
        DetailText.Text = string.IsNullOrWhiteSpace(detail) ? "Recording" : detail;
    }

    public void SetBusy(string label)
    {
        PauseLabel.Text = label;
        PauseIcon.Glyph = "\uE768";
        PauseButton.IsEnabled = false;
        RestartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        SetLive(recording: false, paused: false);
    }

    public void SetAwaitingStart(string detail)
    {
        PauseLabel.Text = "Start";
        PauseIcon.Glyph = "\uE768";
        PauseButton.IsEnabled = true;
        RestartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        DetailText.Text = string.IsNullOrWhiteSpace(detail) ? "Ready" : detail;
        SetLive(recording: false, paused: false);
    }

    public void SetPaused(bool paused)
    {
        PauseLabel.Text = paused ? "Resume" : "Pause";
        PauseIcon.Glyph = paused ? "\uE768" : "\uE769";
        PauseButton.IsEnabled = true;
        RestartButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        SetLive(recording: true, paused: paused);
    }

    private void SetLive(bool recording, bool paused)
    {
        LiveDot.Fill = new SolidColorBrush(recording
            ? (paused ? Windows.UI.Color.FromArgb(255, 244, 180, 0) : Windows.UI.Color.FromArgb(255, 229, 72, 77))
            : Windows.UI.Color.FromArgb(255, 255, 255, 255));
        LiveDot.Opacity = recording ? 1 : 0.45;
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _page.TogglePause();

    private void Restart_Click(object sender, RoutedEventArgs e) => _page.RestartRecording();

    private void Stop_Click(object sender, RoutedEventArgs e) => _page.StopRecording();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _page.CancelRecording();
}
