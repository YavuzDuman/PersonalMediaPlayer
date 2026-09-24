using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace PersonalMediaPlayer.App.Capture;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HwndMessage = -3;
    private static readonly string ClassName = "PersonalMediaPlayerHotkeyWnd";

    private readonly DispatcherQueue _dispatcher;
    private readonly Action<int> _onHotkey;
    private readonly WndProc _wndProc;
    private nint _hwnd;
    private bool _classRegistered;
    private bool _disposed;

    public GlobalHotkeyService(DispatcherQueue dispatcher, Action<int> onHotkey)
    {
        _dispatcher = dispatcher;
        _onHotkey = onHotkey;
        _wndProc = WindowProc;
        _hwnd = CreateMessageWindow();
        foreach (var entry in CaptureHotkeys.All)
        {
            RegisterHotKey(_hwnd, entry.Id, CaptureHotkeys.Modifiers, entry.VirtualKey);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd != 0)
        {
            foreach (var entry in CaptureHotkeys.All)
            {
                UnregisterHotKey(_hwnd, entry.Id);
            }

            DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        if (_classRegistered)
        {
            UnregisterClass(ClassName, GetModuleHandle(null));
        }
    }

    private nint CreateMessageWindow()
    {
        var wndClass = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName
        };
        var atom = RegisterClass(ref wndClass);
        _classRegistered = atom != 0;
        var hwnd = CreateWindowEx(
            0,
            ClassName,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            0,
            wndClass.hInstance,
            0);
        if (hwnd == 0)
        {
            throw new InvalidOperationException("Could not create the screenshot hotkey window.");
        }

        return hwnd;
    }

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmHotkey)
        {
            var id = (int)wParam;
            _dispatcher.TryEnqueue(() => _onHotkey(id));
            return 0;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WndClass lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
