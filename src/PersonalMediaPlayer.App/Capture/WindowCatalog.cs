using System.Runtime.InteropServices;
using System.Text;

namespace PersonalMediaPlayer.App.Capture;

internal static class WindowCatalog
{
    public static IReadOnlyList<WindowEntry> List(params nint[] exclude)
    {
        var skip = exclude.ToHashSet();
        var windows = new List<WindowEntry>();
        EnumWindows((hwnd, _) =>
        {
            if (skip.Contains(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd))
            {
                return true;
            }

            if ((GetWindowLong(hwnd, -20) & 0x00000080) != 0)
            {
                return true;
            }

            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return true;
            }

            var title = new StringBuilder(length + 1);
            GetWindowText(hwnd, title, title.Capacity);
            var text = title.ToString().Trim();
            if (text.Length == 0)
            {
                return true;
            }

            windows.Add(new WindowEntry(hwnd, text));
            return true;
        }, 0);
        return windows;
    }

    internal readonly record struct WindowEntry(nint Handle, string Title);

    private delegate bool EnumProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hwnd);
}
