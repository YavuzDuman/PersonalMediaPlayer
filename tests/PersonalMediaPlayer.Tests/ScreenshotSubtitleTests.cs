using System.Drawing;
using System.Drawing.Imaging;
using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class ScreenshotSubtitleTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;

    public ScreenshotSubtitleTests()
    {
        Monitor.Enter(Gate);
        _store = Path.Combine(Path.GetTempPath(), "pmp-screenshot-subtitles-" + Guid.NewGuid().ToString("N") + ".txt");
        ScreenshotSubtitles.StoreOverride = _store;
    }

    public void Dispose()
    {
        ScreenshotSubtitles.StoreOverride = null;
        if (File.Exists(_store))
        {
            File.Delete(_store);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void TheChoiceStartsOffAndStays()
    {
        Assert.False(ScreenshotSubtitles.Include);
        Assert.False(File.Exists(_store));
        ScreenshotSubtitles.Save(false);
        Assert.False(File.Exists(_store));

        ScreenshotSubtitles.Save(true);
        Assert.True(ScreenshotSubtitles.Include);
        Assert.Equal("true", File.ReadAllText(_store));
        ScreenshotSubtitles.StoreOverride = _store;
        Assert.True(ScreenshotSubtitles.Include);

        File.WriteAllText(_store, "nope");
        ScreenshotSubtitles.StoreOverride = _store;
        Assert.False(ScreenshotSubtitles.Include);
        ScreenshotSubtitles.Save(false);
        Assert.Equal("nope", File.ReadAllText(_store).Trim());

        ScreenshotSubtitles.Save(true);
        Assert.Equal("true", File.ReadAllText(_store).Trim());
        ScreenshotSubtitles.Save(false);
        ScreenshotSubtitles.StoreOverride = _store;
        Assert.False(ScreenshotSubtitles.Include);
        Assert.Equal("false", File.ReadAllText(_store).Trim());
    }

    [Fact]
    public void AnEmptyLineLeavesThePictureAlone()
    {
        var png = Picture(640, 360, Color.Magenta, burn: true);
        var same = CaptionFrame.Apply(png, null);
        Assert.Same(png, same);
        var empty = CaptionFrame.Apply(png, Paint([" "], SubtitleSpot.Bottom, full()));
        Assert.Same(png, empty);
        AssertRgb(png, 4, 4, 255, 255, 255);
    }

    [Fact]
    public void TheSubtitleSitsInTheChosenPlaceAndKeepsBurnedInText()
    {
        var surface = new CaptionSurface(640, 720, 0, 0, 640, 720);
        var png = Picture(640, 720, Color.Magenta, burn: true);
        var bottom = CaptionFrame.Apply(png, Paint(["Hello"], SubtitleSpot.Bottom, surface));
        var middle = CaptionFrame.Apply(png, Paint(["Hello"], SubtitleSpot.Middle, surface));
        var top = CaptionFrame.Apply(png, Paint(["Hello"], SubtitleSpot.Top, surface));

        AssertRgb(bottom, 4, 4, 255, 255, 255);
        AssertRgb(bottom, 320, 300, 255, 0, 255);
        AssertRgb(top, 320, 340, 255, 0, 255);
        Assert.True(ChangedCentroid(png, bottom) > ChangedCentroid(png, middle));
        Assert.True(ChangedCentroid(png, middle) > ChangedCentroid(png, top));
        Assert.True(HasColor(bottom, redMin: 220, greenMin: 220, blueMin: 220));
    }

    [Fact]
    public void ALargerFrameKeepsTheSameGapAboveTheBottom()
    {
        var png = Picture(1280, 720, Color.Magenta, burn: false);
        var painted = CaptionFrame.Apply(png, Paint(["Hello"], SubtitleSpot.Bottom, full()));
        AssertRgb(painted, 640, 680, 255, 0, 255);
        var centroid = ChangedCentroid(png, painted);
        Assert.InRange(centroid, 250, 420);
    }

    [Fact]
    public void ALineBelowTheVideoMovesOntoThePicture()
    {
        var png = Picture(400, 80, Color.Magenta, burn: false);
        var surface = new CaptionSurface(400, 400, 0, 0, 400, 80);
        var painted = CaptionFrame.Apply(png, Paint(["Hello"], SubtitleSpot.Bottom, surface));
        AssertRgb(painted, 8, 4, 255, 0, 255);
        Assert.True(ChangedCentroid(png, painted) > 40);
    }

    [Fact]
    public void LetterboxingKeepsTheLineOverTheVideo()
    {
        var png = Picture(400, 200, Color.Magenta, burn: false);
        var surface = new CaptionSurface(400, 300, 0, 50, 400, 200);
        var painted = CaptionFrame.Apply(png, Paint(["Hello"], SubtitleSpot.Bottom, surface));
        AssertRgb(painted, 200, 190, 255, 0, 255);
        var centroid = ChangedCentroid(png, painted);
        Assert.InRange(centroid, 40, 150);
    }

    [Fact]
    public void WordsStayApartWhenTheHostIsWiderThanThePicture()
    {
        var png = Picture(640, 360, Color.Magenta, burn: false);
        var surface = new CaptionSurface(400, 180, 0, 0, 400, 180);
        var painted = CaptionFrame.Apply(
            png,
            new CaptionPaint(
                ["Hello", "world"],
                [false, false],
                new SubtitleStyle(0, SubtitleSize.Large, SubtitleTextColor.White, SubtitlePlate.None, SubtitleSpot.Middle),
                surface));
        Assert.NotSame(png, painted);
        Assert.True(HasWordGap(painted));
    }

    [Fact]
    public void TheLineStaysWhereTheWordsAre()
    {
        var png = Picture(640, 360, Color.Magenta, burn: false);
        var surface = full();
        var centered = CaptionFrame.Apply(png, Paint(["Hello"], SubtitleSpot.Middle, surface));
        var anchored = CaptionFrame.Apply(
            png,
            Paint(["Hello"], SubtitleSpot.Middle, surface) with
            {
                Anchor = new CaptionAnchor(36, 150, 120, 28)
            });
        Assert.True(ChangedCentroidX(png, anchored) < ChangedCentroidX(png, centered) - 40);
        Assert.InRange(LeftmostInk(anchored), 24, 52);
    }

    [Fact]
    public void AMarkedWordUsesTheYellowChipAndAClearPlateStaysClear()
    {
        var png = Picture(640, 360, Color.Magenta, burn: false);
        var marked = CaptionFrame.Apply(png, Paint(["Hello", "there"], SubtitleSpot.Bottom, full(), [false, true]));
        Assert.True(HasColor(marked, redMin: 200, greenMin: 150, blueMax: 80));

        var clear = CaptionFrame.Apply(
            png,
            new CaptionPaint(
                ["Hello"],
                [false],
                new SubtitleStyle(0, SubtitleSize.Medium, SubtitleTextColor.White, SubtitlePlate.None, SubtitleSpot.Bottom),
                full()));
        Assert.False(HasColor(clear, redMax: 80, greenMax: 80, blueMax: 80));
        Assert.True(HasColor(clear, redMin: 220, greenMin: 220, blueMin: 220));
    }

    private static CaptionPaint Paint(string[] words, SubtitleSpot spot, CaptionSurface surface, bool[]? highlight = null)
        => new(
            words,
            highlight ?? new bool[words.Length],
            new SubtitleStyle(0, SubtitleSize.Medium, SubtitleTextColor.White, SubtitlePlate.Dark, spot),
            surface);

    private static CaptionSurface full() => new(640, 360, 0, 0, 640, 360);

    private static byte[] Picture(int width, int height, Color color, bool burn)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);
            if (burn)
            {
                graphics.FillRectangle(Brushes.White, 0, 0, 12, 12);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void AssertRgb(byte[] png, int x, int y, int red, int green, int blue)
    {
        var pixel = Pixel(png, x, y);
        Assert.Equal(red, pixel.R);
        Assert.Equal(green, pixel.G);
        Assert.Equal(blue, pixel.B);
    }

    private static Color Pixel(byte[] png, int x, int y)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        return bitmap.GetPixel(x, y);
    }

    private static double ChangedCentroidX(byte[] before, byte[] after)
        => ChangedCentroid(before, after, horizontal: true);

    private static int LeftmostInk(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        var best = int.MaxValue;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R > 210 && pixel.G > 210 && pixel.B > 210 && x < best)
                {
                    best = x;
                }
            }
        }

        return best;
    }

    private static double ChangedCentroid(byte[] before, byte[] after)
        => ChangedCentroid(before, after, horizontal: false);

    private static double ChangedCentroid(byte[] before, byte[] after, bool horizontal)
    {
        using var leftStream = new MemoryStream(before);
        using var rightStream = new MemoryStream(after);
        using var left = new Bitmap(leftStream);
        using var right = new Bitmap(rightStream);
        double sum = 0;
        var count = 0;
        for (var y = 0; y < left.Height; y += 2)
        {
            for (var x = 0; x < left.Width; x += 4)
            {
                var a = left.GetPixel(x, y);
                var b = right.GetPixel(x, y);
                if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 40)
                {
                    sum += horizontal ? x : y;
                    count++;
                }
            }
        }

        Assert.True(count > 0);
        return sum / count;
    }

    private static bool HasWordGap(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            var runs = 0;
            var ink = false;
            var gapAfterInk = false;
            var separated = false;
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var text = pixel.R > 210 && pixel.G > 210 && pixel.B > 210;
                var paper = pixel.R > 180 && pixel.B > 180 && pixel.G < 90;
                if (text)
                {
                    if (!ink)
                    {
                        runs++;
                        ink = true;
                        if (gapAfterInk)
                        {
                            separated = true;
                        }
                    }
                }
                else
                {
                    if (ink && paper)
                    {
                        gapAfterInk = true;
                    }

                    ink = false;
                }
            }

            if (runs >= 2 && separated)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasColor(byte[] png, int redMin = 0, int greenMin = 0, int blueMin = 0, int redMax = 255, int greenMax = 255, int blueMax = 255)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R >= redMin && pixel.G >= greenMin && pixel.B >= blueMin
                    && pixel.R <= redMax && pixel.G <= greenMax && pixel.B <= blueMax)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
