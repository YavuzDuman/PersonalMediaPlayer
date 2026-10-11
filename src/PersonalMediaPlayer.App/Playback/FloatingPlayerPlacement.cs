namespace PersonalMediaPlayer.App.Playback;

// Where the always-on-top player sits: the mini player plus the title bar,
// in the bottom-right of the work area.
internal static class FloatingPlayerPlacement
{
    internal const int WidthDips = 360;

    internal const int TitleDips = 48;

    internal const int PlayerDips = 320;

    internal const int MarginDips = 16;

    internal readonly record struct Spot(int X, int Y, int Width, int Height);

    internal static Spot Place(int workX, int workY, int workWidth, int workHeight, double scale)
    {
        if (scale < 1 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            scale = 1;
        }

        var width = Pixels(WidthDips, scale);
        var height = Pixels(TitleDips + PlayerDips, scale);
        var margin = Pixels(MarginDips, scale);
        if (workWidth > margin + 80)
        {
            width = Math.Min(width, workWidth - margin);
        }

        if (workHeight > margin + 80)
        {
            height = Math.Min(height, workHeight - margin);
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var x = workX + Math.Max(0, workWidth - width - margin);
        var y = workY + Math.Max(0, workHeight - height - margin);
        return new Spot(x, y, width, height);
    }

    private static int Pixels(int dips, double scale) => (int)Math.Ceiling(dips * scale);
}
