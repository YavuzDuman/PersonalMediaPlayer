using System.Drawing;
using System.Runtime.InteropServices;

namespace PersonalMediaPlayer.App.Capture;

internal readonly record struct MonitorRect(int X, int Y, int Width, int Height, nint Handle)
{
    public Rectangle ToRectangle() => new(X, Y, Width, Height);
}

internal static class ScreenGeometry
{
    public static Rectangle VirtualDesktop { get; private set; }

    public static IReadOnlyList<MonitorRect> Monitors { get; private set; } = [];

    public static void Refresh()
    {
        var monitors = new List<MonitorRect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr _, ref NativeRect rect, IntPtr _) =>
            {
                monitors.Add(new MonitorRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, hMonitor));
                return true;
            }, IntPtr.Zero);

        var x = GetSystemMetrics(76);
        var y = GetSystemMetrics(77);
        var w = GetSystemMetrics(78);
        var h = GetSystemMetrics(79);
        VirtualDesktop = new Rectangle(x, y, w, h);
        Monitors = monitors.Count > 0 ? monitors : [new MonitorRect(x, y, w, h, IntPtr.Zero)];
    }

    public static NativePoint CursorPosition()
    {
        GetCursorPos(out var point);
        return point;
    }

    public static MonitorRect FromPoint(int x, int y)
    {
        if (Monitors.Count == 0)
        {
            Refresh();
        }

        var handle = MonitorFromPoint(new NativePoint { X = x, Y = y }, 2);
        foreach (var monitor in Monitors)
        {
            if (monitor.Handle == handle)
            {
                return monitor;
            }
        }

        foreach (var monitor in Monitors)
        {
            if (x >= monitor.X && x < monitor.X + monitor.Width && y >= monitor.Y && y < monitor.Y + monitor.Height)
            {
                return monitor;
            }
        }

        return Monitors[0];
    }

    public static MonitorRect FromCursor()
    {
        var point = CursorPosition();
        return FromPoint(point.X, point.Y);
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref NativeRect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }
}
