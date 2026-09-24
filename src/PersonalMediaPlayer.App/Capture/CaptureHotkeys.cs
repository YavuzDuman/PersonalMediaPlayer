namespace PersonalMediaPlayer.App.Capture;

internal static class CaptureHotkeys
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModNoRepeat = 0x4000;

    public const uint Modifiers = ModControl | ModAlt | ModShift | ModNoRepeat;

    public static IReadOnlyList<Entry> All { get; } =
    [
        new(1, 0x41, CaptureKind.SelectedArea, "Ctrl+Alt+Shift+A", "Selected area"),
        new(2, 0x57, CaptureKind.SingleWindow, "Ctrl+Alt+Shift+W", "Single window"),
        new(3, 0x4D, CaptureKind.SingleMonitor, "Ctrl+Alt+Shift+M", "Single monitor"),
        new(4, 0x44, CaptureKind.AllMonitors, "Ctrl+Alt+Shift+D", "All monitors"),
        new(5, 0x46, CaptureKind.Fullscreen, "Ctrl+Alt+Shift+F", "Fullscreen")
    ];

    internal readonly record struct Entry(int Id, uint VirtualKey, CaptureKind Kind, string Shortcut, string Mode);
}
