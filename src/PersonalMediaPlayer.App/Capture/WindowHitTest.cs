using System.Runtime.InteropServices;

namespace PersonalMediaPlayer.App.Capture;

internal static class WindowHitTest
{
    private const uint GaRoot = 2;

    public static nint RootFromPoint(int x, int y, nint exclude)
    {
        var hwnd = WindowFromPoint(new NativePoint { X = x, Y = y });
        if (hwnd == 0)
        {
            return 0;
        }

        var root = GetAncestor(hwnd, GaRoot);
        if (root == 0)
        {
            root = hwnd;
        }

        if (root == exclude || !IsWindowVisible(root) || IsIconic(root))
        {
            return 0;
        }

        return root;
    }

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
