using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WinColor = Windows.UI.Color;

namespace PersonalMediaPlayer.App.Editing;

public enum AnnotationTool
{
    None,
    Pen,
    Highlight,
    Eraser,
    Arrow,
    Rectangle,
    Text,
    Step,
    Blur,
    Redact,
    Crop,
    ReadArea
}

public abstract class ScreenshotMark
{
    public WinColor Color { get; init; }

    public float Thickness { get; init; }

    public abstract ScreenshotMark Clone();
}

public sealed class PenMark : ScreenshotMark
{
    public List<Point> Points { get; } = [];

    public override ScreenshotMark Clone()
    {
        var copy = new PenMark { Color = Color, Thickness = Thickness };
        copy.Points.AddRange(Points);
        return copy;
    }
}

public sealed class HighlightMark : ScreenshotMark
{
    public List<Point> Points { get; } = [];

    public override ScreenshotMark Clone()
    {
        var copy = new HighlightMark { Color = Color, Thickness = Thickness };
        copy.Points.AddRange(Points);
        return copy;
    }
}

public sealed class StepMark : ScreenshotMark
{
    public Point Center { get; init; }

    public int Number { get; init; }

    public float Diameter { get; init; }

    public override ScreenshotMark Clone()
        => new StepMark { Color = Color, Thickness = Thickness, Center = Center, Number = Number, Diameter = Diameter };
}

public sealed class ArrowMark : ScreenshotMark
{
    public Point Start { get; init; }

    public Point End { get; init; }

    public override ScreenshotMark Clone()
        => new ArrowMark { Color = Color, Thickness = Thickness, Start = Start, End = End };
}

public sealed class RectMark : ScreenshotMark
{
    public Point Start { get; init; }

    public Point End { get; init; }

    public override ScreenshotMark Clone()
        => new RectMark { Color = Color, Thickness = Thickness, Start = Start, End = End };
}

public sealed class BlurMark : ScreenshotMark
{
    public Point Start { get; init; }

    public Point End { get; init; }

    public override ScreenshotMark Clone()
        => new BlurMark { Color = Color, Thickness = Thickness, Start = Start, End = End };
}

public sealed class RedactMark : ScreenshotMark
{
    public Point Start { get; init; }

    public Point End { get; init; }

    public override ScreenshotMark Clone()
        => new RedactMark { Color = Color, Thickness = Thickness, Start = Start, End = End };
}

public sealed class TextMark : ScreenshotMark
{
    public Point Position { get; set; }

    public string Text { get; init; } = string.Empty;

    public float FontSize { get; set; }

    public override ScreenshotMark Clone()
        => new TextMark { Color = Color, Thickness = Thickness, Position = Position, Text = Text, FontSize = FontSize };
}

public static class ScreenshotAnnotator
{
    public static byte[] Flatten(byte[] pngBytes, IReadOnlyList<ScreenshotMark> marks)
    {
        using var source = new MemoryStream(pngBytes, writable: false);
        using var image = new Bitmap(source);
        using var canvas = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.DrawImage(image, 0, 0, image.Width, image.Height);
        }

        ApplyMarks(canvas, marks);

        using var output = new MemoryStream();
        canvas.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    public static void ApplyMarks(Bitmap canvas, IEnumerable<ScreenshotMark> marks)
    {
        Graphics? graphics = null;
        try
        {
            foreach (var mark in marks)
            {
                if (mark is BlurMark blur)
                {
                    graphics?.Dispose();
                    graphics = null;
                    BlurRegion(canvas, Bounds(blur.Start, blur.End));
                    continue;
                }

                graphics ??= CreateGraphics(canvas);
                Draw(graphics, mark);
            }
        }
        finally
        {
            graphics?.Dispose();
        }
    }

    private static Graphics CreateGraphics(Bitmap canvas)
    {
        var graphics = Graphics.FromImage(canvas);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        return graphics;
    }

    private static void Draw(Graphics graphics, ScreenshotMark mark)
    {
        var color = Color.FromArgb(mark.Color.A, mark.Color.R, mark.Color.G, mark.Color.B);
        using var pen = new Pen(color, Math.Max(1, mark.Thickness))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        switch (mark)
        {
            case PenMark stroke when stroke.Points.Count >= 2:
                graphics.DrawLines(pen, stroke.Points.ToArray());
                break;
            case HighlightMark highlight when highlight.Points.Count >= 2:
                using (var marker = new Pen(Color.FromArgb(110, color.R, color.G, color.B), Math.Max(8, highlight.Thickness))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                })
                {
                    graphics.DrawLines(marker, highlight.Points.ToArray());
                }

                break;
            case StepMark step:
                var radius = Math.Max(14f, step.Diameter / 2f);
                var circle = new RectangleF(step.Center.X - radius, step.Center.Y - radius, radius * 2, radius * 2);
                using (var fill = new SolidBrush(Color.FromArgb(255, color.R, color.G, color.B)))
                using (var font = new Font("Segoe UI", Math.Max(12, radius * 0.95f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var ink = new SolidBrush(StepInk(color)))
                {
                    graphics.FillEllipse(fill, circle);
                    var label = step.Number.ToString();
                    var size = graphics.MeasureString(label, font);
                    graphics.DrawString(label, font, ink, step.Center.X - (size.Width / 2f), step.Center.Y - (size.Height / 2f));
                }

                break;
            case ArrowMark arrow:
                using (var arrowPen = (Pen)pen.Clone())
                {
                    arrowPen.CustomEndCap = new AdjustableArrowCap(5, 5, isFilled: true);
                    graphics.DrawLine(arrowPen, arrow.Start, arrow.End);
                }

                break;
            case RectMark rect:
                var bounds = Bounds(rect.Start, rect.End);
                if (bounds.Width >= 1 && bounds.Height >= 1)
                {
                    graphics.DrawRectangle(pen, bounds);
                }

                break;
            case RedactMark redact:
                var cover = Bounds(redact.Start, redact.End);
                if (cover.Width >= 1 && cover.Height >= 1)
                {
                    using var fill = new SolidBrush(Color.FromArgb(255, color.R, color.G, color.B));
                    graphics.FillRectangle(fill, cover);
                }

                break;
            case TextMark text when !string.IsNullOrWhiteSpace(text.Text):
                using (var font = new Font("Segoe UI", Math.Max(10, text.FontSize), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(color))
                {
                    graphics.DrawString(text.Text, font, brush, text.Position);
                }

                break;
        }
    }

    private static Color StepInk(Color fill)
    {
        var luminance = (0.299 * fill.R) + (0.587 * fill.G) + (0.114 * fill.B);
        return luminance > 160 ? Color.Black : Color.White;
    }

    public static Rectangle Bounds(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        return new Rectangle(x, y, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
    }

    public readonly record struct CroppedImage(byte[] Png, int Width, int Height);

    public static CroppedImage Crop(byte[] pngBytes, Rectangle crop)
    {
        using var sourceStream = new MemoryStream(pngBytes, writable: false);
        using var source = new Bitmap(sourceStream);
        crop.Intersect(new Rectangle(0, 0, source.Width, source.Height));
        if (crop.Width < 4 || crop.Height < 4)
        {
            throw new InvalidOperationException("Select a larger area to trim.");
        }

        if (crop.X == 0 && crop.Y == 0 && crop.Width == source.Width && crop.Height == source.Height)
        {
            throw new InvalidOperationException("That selection is the whole image.");
        }

        using var trimmed = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(trimmed))
        {
            graphics.DrawImage(source, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
        }

        using var output = new MemoryStream();
        trimmed.Save(output, ImageFormat.Png);
        return new CroppedImage(output.ToArray(), trimmed.Width, trimmed.Height);
    }

    public static void BlurRegion(Bitmap bitmap, Rectangle bounds)
    {
        bounds.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        var radius = Math.Clamp(Math.Max(bounds.Width, bounds.Height) / 18, 10, 32);
        BoxBlur(bitmap, bounds, radius);
        BoxBlur(bitmap, bounds, radius);
        BoxBlur(bitmap, bounds, Math.Max(6, radius / 2));
    }

    public static Bitmap CreateBlurredPatch(Bitmap source, Rectangle bounds)
    {
        bounds.Intersect(new Rectangle(0, 0, source.Width, source.Height));
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            bounds = new Rectangle(0, 0, 1, 1);
        }

        var patch = source.Clone(bounds, PixelFormat.Format32bppArgb);
        BlurRegion(patch, new Rectangle(0, 0, patch.Width, patch.Height));
        return patch;
    }

    private static void BoxBlur(Bitmap bitmap, Rectangle bounds, int radius)
    {
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var length = stride * bounds.Height;
            var src = new byte[length];
            var dst = new byte[length];
            Marshal.Copy(data.Scan0, src, 0, length);
            BlurAxis(src, dst, bounds.Width, bounds.Height, stride, radius, horizontal: true);
            BlurAxis(dst, src, bounds.Width, bounds.Height, stride, radius, horizontal: false);
            Marshal.Copy(src, 0, data.Scan0, length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void BlurAxis(byte[] src, byte[] dst, int width, int height, int stride, int radius, bool horizontal)
    {
        var window = (radius * 2) + 1;
        if (horizontal)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * stride;
                int b = 0, g = 0, r = 0, a = 0;
                for (var i = -radius; i <= radius; i++)
                {
                    var x = Math.Clamp(i, 0, width - 1);
                    var p = row + (x * 4);
                    b += src[p];
                    g += src[p + 1];
                    r += src[p + 2];
                    a += src[p + 3];
                }

                for (var x = 0; x < width; x++)
                {
                    var p = row + (x * 4);
                    dst[p] = (byte)(b / window);
                    dst[p + 1] = (byte)(g / window);
                    dst[p + 2] = (byte)(r / window);
                    dst[p + 3] = (byte)(a / window);
                    var leave = Math.Clamp(x - radius, 0, width - 1);
                    var enter = Math.Clamp(x + radius + 1, 0, width - 1);
                    var lp = row + (leave * 4);
                    var ep = row + (enter * 4);
                    b += src[ep] - src[lp];
                    g += src[ep + 1] - src[lp + 1];
                    r += src[ep + 2] - src[lp + 2];
                    a += src[ep + 3] - src[lp + 3];
                }
            }

            return;
        }

        for (var x = 0; x < width; x++)
        {
            int b = 0, g = 0, r = 0, a = 0;
            for (var i = -radius; i <= radius; i++)
            {
                var y = Math.Clamp(i, 0, height - 1);
                var p = (y * stride) + (x * 4);
                b += src[p];
                g += src[p + 1];
                r += src[p + 2];
                a += src[p + 3];
            }

            for (var y = 0; y < height; y++)
            {
                var p = (y * stride) + (x * 4);
                dst[p] = (byte)(b / window);
                dst[p + 1] = (byte)(g / window);
                dst[p + 2] = (byte)(r / window);
                dst[p + 3] = (byte)(a / window);
                var leave = Math.Clamp(y - radius, 0, height - 1);
                var enter = Math.Clamp(y + radius + 1, 0, height - 1);
                var lp = (leave * stride) + (x * 4);
                var ep = (enter * stride) + (x * 4);
                b += src[ep] - src[lp];
                g += src[ep + 1] - src[lp + 1];
                r += src[ep + 2] - src[lp + 2];
                a += src[ep + 3] - src[lp + 3];
            }
        }
    }
}
