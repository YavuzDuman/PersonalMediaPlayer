using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalMediaPlayer.App.Capture;
using Windows.Graphics;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class ScreenshotOverlayWindow : Window
{
    private readonly ScreenshotSession _session;
    private readonly MonitorRect _monitor;

    internal ScreenshotOverlayWindow(ScreenshotSession session, MonitorRect monitor)
    {
        _session = session;
        _monitor = monitor;
        InitializeComponent();

        SystemBackdrop = null;
        ExtendsContentIntoTitleBar = false;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        CoverMonitor();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                CoverMonitor();
            }
        };

        HintText.Text = string.IsNullOrWhiteSpace(session.Hint)
            ? session.OverlayKind switch
            {
                CaptureOverlayKind.Monitor => "Click a monitor to capture it. Esc or Alt+F4 cancels.",
                CaptureOverlayKind.Window => "Click a window to capture it. Esc or Alt+F4 cancels.",
                _ => "Drag to select an area on any screen. Esc or Alt+F4 cancels."
            }
            : session.Hint;

        if (session.Desktop is not null && session.OverlayKind is not CaptureOverlayKind.Window)
        {
            using var slice = session.Desktop.Clone(
                new Rectangle(monitor.X - session.VirtualDesktop.X, monitor.Y - session.VirtualDesktop.Y, monitor.Width, monitor.Height),
                PixelFormat.Format32bppArgb);
            BackgroundImage.Source = ToImage(slice);
        }
        else
        {
            BackgroundImage.Visibility = Visibility.Collapsed;
            Root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0, 0, 0));
        }

        _session.Changed += UpdateSelection;
        AppWindow.Closing += AppWindow_Closing;
        Closed += Overlay_Closed;
        Root.KeyDown += Root_KeyDown;
        Root.Loaded += (_, _) => Root.Focus(FocusState.Programmatic);
    }

    private void CoverMonitor()
    {
        AppWindow.MoveAndResize(new RectInt32(
            _monitor.X,
            _monitor.Y,
            Math.Max(1, _monitor.Width),
            Math.Max(1, _monitor.Height)));
        DisableCaptionDrag();
        NativeMethods.MakePopupCover(WindowNative.GetWindowHandle(this), _monitor);
    }

    private void DisableCaptionDrag()
    {
        try
        {
            var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            source.ClearRegionRects(NonClientRegionKind.Caption);
            source.SetRegionRects(NonClientRegionKind.Caption, []);
        }
        catch (Exception)
        {
            // Caption regions are not always available until the HWND is fully created.
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _session.NotifyOverlayClosing(this);
    }

    private void Overlay_Closed(object sender, WindowEventArgs args)
    {
        _session.Changed -= UpdateSelection;
        _session.NotifyOverlayClosing(this);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _session.Cancel();
            e.Handled = true;
        }
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = ToVirtual(e.GetCurrentPoint(Root).Position);
        switch (_session.OverlayKind)
        {
            case CaptureOverlayKind.Monitor:
                _session.CompleteMonitor(_monitor);
                return;
            case CaptureOverlayKind.Window:
                _session.CompleteWindowPick(point.X, point.Y);
                return;
            default:
                Root.CapturePointer(e.Pointer);
                _session.Begin(point.X, point.Y);
                return;
        }
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_session.OverlayKind != CaptureOverlayKind.Region || !_session.Selecting)
        {
            return;
        }

        var point = ToVirtual(e.GetCurrentPoint(Root).Position);
        _session.Move(point.X, point.Y);
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Root.ReleasePointerCaptures();
        if (_session.OverlayKind == CaptureOverlayKind.Region)
        {
            _session.End();
        }
    }

    private System.Drawing.Point ToVirtual(Windows.Foundation.Point dip)
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        return new System.Drawing.Point(
            _monitor.X + (int)Math.Round(dip.X * scale),
            _monitor.Y + (int)Math.Round(dip.Y * scale));
    }

    private void UpdateSelection()
    {
        var selection = _session.Selection;
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        var local = new Rectangle(
            selection.X - _monitor.X,
            selection.Y - _monitor.Y,
            selection.Width,
            selection.Height);
        var visible = Rectangle.Intersect(local, new Rectangle(0, 0, _monitor.Width, _monitor.Height));
        if (!_session.Selecting || visible.Width < 1 || visible.Height < 1)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, visible.X / scale);
        Canvas.SetTop(SelectionRect, visible.Y / scale);
        SelectionRect.Width = visible.Width / scale;
        SelectionRect.Height = visible.Height / scale;
    }

    private static BitmapImage ToImage(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        memory.Position = 0;
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(memory.ToArray());
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        }

        stream.Seek(0);
        var image = new BitmapImage();
        image.SetSource(stream);
        return image;
    }

    private static class NativeMethods
    {
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const uint WsCaption = 0x00C00000;
        private const uint WsThickFrame = 0x00040000;
        private const uint WsMinimizeBox = 0x00020000;
        private const uint WsMaximizeBox = 0x00010000;
        private const uint WsSysMenu = 0x00080000;
        private const uint WsBorder = 0x00800000;
        private const uint WsDlgFrame = 0x00400000;
        private const uint WsPopup = 0x80000000;
        private const uint WsVisible = 0x10000000;
        private const uint WsExTopmost = 0x00000008;
        private const uint WsExToolWindow = 0x00000080;
        private const uint WsExAppWindow = 0x00040000;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;

        public static void MakePopupCover(nint hwnd, MonitorRect monitor)
        {
            var style = (uint)(long)GetLong(hwnd, GwlStyle);
            style &= ~(WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu | WsBorder | WsDlgFrame);
            style |= WsPopup | WsVisible;
            SetLong(hwnd, GwlStyle, unchecked((nint)style));

            var ex = (uint)(long)GetLong(hwnd, GwlExStyle);
            ex |= WsExTopmost | WsExToolWindow;
            ex &= ~WsExAppWindow;
            SetLong(hwnd, GwlExStyle, unchecked((nint)ex));

            SetWindowPos(
                hwnd,
                -1,
                monitor.X,
                monitor.Y,
                Math.Max(1, monitor.Width),
                Math.Max(1, monitor.Height),
                SwpFrameChanged | SwpShowWindow);
        }

        private static nint GetLong(nint hwnd, int index) =>
            Environment.Is64BitProcess ? GetWindowLongPtr(hwnd, index) : GetWindowLong(hwnd, index);

        private static void SetLong(nint hwnd, int index, nint value)
        {
            if (Environment.Is64BitProcess)
            {
                SetWindowLongPtr(hwnd, index, value);
            }
            else
            {
                SetWindowLong(hwnd, index, (int)value);
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
