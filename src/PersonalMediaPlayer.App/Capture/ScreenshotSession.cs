using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Xaml;
using PersonalMediaPlayer.App.Views;
using WinRT.Interop;

namespace PersonalMediaPlayer.App.Capture;

internal sealed class ScreenshotSession
{
    private readonly List<ScreenshotOverlayWindow> _overlays = [];
    private readonly Window _host;
    private readonly Action<Bitmap>? _onComplete;
    private readonly Action<System.Drawing.Point>? _onWindowPicked;
    private readonly Action<Rectangle>? _onRegion;
    private readonly Action _onCancel;
    private readonly bool _clipToStartMonitor;
    private ScreenshotOverlayWindow? _closingOverlay;
    private bool _finished;
    private static CancellationTokenSource? _prepareAbort;

    public ScreenshotSession(
        Window host,
        Bitmap? desktop,
        Rectangle virtualDesktop,
        IReadOnlyList<MonitorRect> monitors,
        CaptureOverlayKind overlayKind,
        Action<Bitmap>? onComplete,
        Action onCancel,
        Action<System.Drawing.Point>? onWindowPicked = null,
        Action<Rectangle>? onRegion = null,
        string? hint = null,
        bool clipToStartMonitor = false)
    {
        _host = host;
        Desktop = desktop;
        VirtualDesktop = virtualDesktop;
        Monitors = monitors;
        OverlayKind = overlayKind;
        Hint = hint;
        _onComplete = onComplete;
        _onCancel = onCancel;
        _onWindowPicked = onWindowPicked;
        _onRegion = onRegion;
        _clipToStartMonitor = clipToStartMonitor;
        Current = this;
    }

    public static ScreenshotSession? Current { get; private set; }

    public static void BeginPrepare(CancellationTokenSource abort) => _prepareAbort = abort;

    public static void EndPrepare() => _prepareAbort = null;

    public static bool TryHandleHostClose()
    {
        if (Current is { } session)
        {
            session.Cancel();
            return true;
        }

        if (_prepareAbort is { } abort)
        {
            abort.Cancel();
            return true;
        }

        return false;
    }

    public Bitmap? Desktop { get; }

    public CaptureOverlayKind OverlayKind { get; }

    public string? Hint { get; }

    public Rectangle VirtualDesktop { get; }

    public IReadOnlyList<MonitorRect> Monitors { get; }

    public bool Selecting { get; private set; }

    public int StartX { get; private set; }

    public int StartY { get; private set; }

    public int CurrentX { get; private set; }

    public int CurrentY { get; private set; }

    public event Action? Changed;

    public Rectangle Selection
    {
        get
        {
            var x = Math.Min(StartX, CurrentX);
            var y = Math.Min(StartY, CurrentY);
            var w = Math.Abs(CurrentX - StartX);
            var h = Math.Abs(CurrentY - StartY);
            return new Rectangle(x, y, w, h);
        }
    }

    public void ShowOverlays()
    {
        try
        {
            foreach (var monitor in Monitors)
            {
                var overlay = new ScreenshotOverlayWindow(this, monitor);
                _overlays.Add(overlay);
                overlay.Activate();
            }
        }
        catch
        {
            Cancel();
            throw;
        }
    }

    public void Begin(int virtualX, int virtualY)
    {
        Selecting = true;
        StartX = CurrentX = virtualX;
        StartY = CurrentY = virtualY;
        Changed?.Invoke();
    }

    public void Move(int virtualX, int virtualY)
    {
        if (!Selecting)
        {
            return;
        }

        if (_clipToStartMonitor)
        {
            var monitor = ScreenGeometry.FromPoint(StartX, StartY);
            var right = monitor.X + Math.Max(1, monitor.Width) - 1;
            var bottom = monitor.Y + Math.Max(1, monitor.Height) - 1;
            virtualX = Math.Clamp(virtualX, monitor.X, right);
            virtualY = Math.Clamp(virtualY, monitor.Y, bottom);
        }

        CurrentX = virtualX;
        CurrentY = virtualY;
        Changed?.Invoke();
    }

    public void End()
    {
        if (!Selecting || OverlayKind != CaptureOverlayKind.Region)
        {
            return;
        }

        Selecting = false;
        var selection = Rectangle.Intersect(Selection, VirtualDesktop);
        if (selection.Width < 4 || selection.Height < 4)
        {
            Changed?.Invoke();
            return;
        }

        if (_onRegion is not null)
        {
            var chosen = selection;
            Finish(() => _onRegion(chosen), restoreHost: false);
            return;
        }

        var crop = new Rectangle(
            selection.X - VirtualDesktop.X,
            selection.Y - VirtualDesktop.Y,
            selection.Width,
            selection.Height);
        if (Desktop is null)
        {
            return;
        }

        crop.Intersect(new Rectangle(0, 0, Desktop.Width, Desktop.Height));
        if (crop.Width < 4 || crop.Height < 4)
        {
            return;
        }

        try
        {
            var result = Desktop.Clone(crop, PixelFormat.Format32bppArgb);
            Finish(() => _onComplete?.Invoke(result), restoreHost: true);
        }
        catch
        {
            Cancel();
            throw;
        }
    }

    public void Cancel() => Finish(_onCancel, restoreHost: true);

    public void CompleteMonitor(MonitorRect monitor)
    {
        if (OverlayKind != CaptureOverlayKind.Monitor || Desktop is null)
        {
            return;
        }

        var crop = new Rectangle(
            monitor.X - VirtualDesktop.X,
            monitor.Y - VirtualDesktop.Y,
            monitor.Width,
            monitor.Height);
        crop.Intersect(new Rectangle(0, 0, Desktop.Width, Desktop.Height));
        if (crop.Width < 4 || crop.Height < 4)
        {
            return;
        }

        try
        {
            var result = Desktop.Clone(crop, PixelFormat.Format32bppArgb);
            Finish(() => _onComplete?.Invoke(result), restoreHost: true);
        }
        catch
        {
            Cancel();
            throw;
        }
    }

    public void CompleteWindowPick(int virtualX, int virtualY)
    {
        if (OverlayKind != CaptureOverlayKind.Window)
        {
            return;
        }

        Finish(() => _onWindowPicked?.Invoke(new System.Drawing.Point(virtualX, virtualY)), restoreHost: false);
    }

    public void NotifyOverlayClosing(ScreenshotOverlayWindow overlay)
    {
        if (_finished)
        {
            return;
        }

        _closingOverlay = overlay;
        Cancel();
    }

    private void Finish(Action next, bool restoreHost)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        if (restoreHost)
        {
            ShowWindow(_host);
        }

        foreach (var overlay in _overlays.ToArray())
        {
            if (ReferenceEquals(overlay, _closingOverlay))
            {
                continue;
            }

            try
            {
                overlay.Close();
            }
            catch
            {
                // Already closing (Alt+F4 / Esc).
            }
        }

        _overlays.Clear();
        try
        {
            next();
        }
        finally
        {
            Desktop?.Dispose();
        }
    }

    public static void HideWindow(Window window)
    {
        window.AppWindow.Hide();
        NativeShowWindow(WindowNative.GetWindowHandle(window), 0);
    }

    public static void ShowWindow(Window window)
    {
        NativeShowWindow(WindowNative.GetWindowHandle(window), 5);
        window.AppWindow.Show();
        window.Activate();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool NativeShowWindow(IntPtr hWnd, int nCmdShow);
}
