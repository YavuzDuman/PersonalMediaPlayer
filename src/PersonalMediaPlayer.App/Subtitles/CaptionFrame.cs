using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace PersonalMediaPlayer.App.Subtitles;

internal readonly record struct CaptionSurface(
    double PictureWidth,
    double PictureHeight,
    double VideoX,
    double VideoY,
    double VideoWidth,
    double VideoHeight);

// Where the words sit on the picture, in device-independent pixels. The Save button is not included.
internal readonly record struct CaptionAnchor(double X, double Y, double Width, double Height);

// ReserveSaveButton stays true unless the caller says the Save button is hidden.
// Caption drawing does not read LearningMode, so a test that turns the mode off
// cannot change wrap for another test running at the same time.
internal sealed record CaptionPaint(
    string[] Words,
    bool[] Highlight,
    SubtitleStyle Style,
    CaptionSurface Surface,
    CaptionAnchor? Anchor = null,
    bool ReserveSaveButton = true);

// Paints the app subtitle onto a video frame. Words already in the picture stay.
internal static class CaptionFrame
{
    private const float PadX = 12f;
    private const float PadY = 6f;
    private const float Radius = 8f;
    private const float WordGap = 3f;
    private const float MarkPad = 4f;
    private const float RowGap = 2f;
    private const float MarkRadius = 4f;
    private const float Edge = 8f;
    internal const double SaveButtonReserve = 120;

    internal static byte[] Apply(byte[] png, CaptionPaint? paint)
    {
        if (paint is null || !CanPaint(paint))
        {
            return png;
        }

        try
        {
            return Draw(png, paint);
        }
        catch (Exception)
        {
            return png;
        }
    }

    private static bool CanPaint(CaptionPaint paint)
    {
        if (paint.Words is not { Length: > 0 })
        {
            return false;
        }

        var surface = paint.Surface;
        return surface.PictureWidth >= 2
            && surface.PictureHeight >= 2
            && surface.VideoWidth >= 2
            && surface.VideoHeight >= 2
            && double.IsFinite(surface.PictureWidth)
            && double.IsFinite(surface.PictureHeight)
            && double.IsFinite(surface.VideoX)
            && double.IsFinite(surface.VideoY)
            && double.IsFinite(surface.VideoWidth)
            && double.IsFinite(surface.VideoHeight);
    }

    private static byte[] Draw(byte[] png, CaptionPaint paint)
    {
        using var input = new MemoryStream(png);
        using var source = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: false);
        if (source.Width < 2 || source.Height < 2)
        {
            return png;
        }

        var layout = Measure(paint, source.Width, source.Height);
        if (layout.Rows.Count == 0 || layout.Plate.Width < 1 || layout.Plate.Height < 1)
        {
            return png;
        }

        using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.PageUnit = GraphicsUnit.Pixel;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.DrawImage(source, 0, 0, bitmap.Width, bitmap.Height);
            PaintPlate(graphics, paint.Style, layout);
            PaintWords(graphics, paint, layout);
        }

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        var encoded = output.ToArray();
        return encoded.Length > 32 ? encoded : png;
    }

    private static LineLayout Measure(CaptionPaint paint, int pixelWidth, int pixelHeight)
    {
        // The bitmap is the video picture. The player keeps that shape, so a wider
        // or taller host has bars. Map through the picture itself or the words overlap.
        var surface = FittedVideo(paint.Surface, pixelWidth, pixelHeight);
        var scaleX = pixelWidth / surface.VideoWidth;
        var scaleY = pixelHeight / surface.VideoHeight;
        using var canvas = new Bitmap(32, 32, PixelFormat.Format24bppRgb);
        using var measure = Graphics.FromImage(canvas);
        measure.PageUnit = GraphicsUnit.Pixel;
        measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = CreateFont(SubtitleLook.FontSize(paint.Style.Size));
        using var format = TextFormat();
        var limit = WrapLimit(surface.PictureWidth, paint.ReserveSaveButton);
        var rows = new List<RowLayout>();
        RowLayout? row = null;
        var used = 0f;
        for (var index = 0; index < paint.Words.Length; index++)
        {
            var text = paint.Words[index];
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var marked = index < paint.Highlight.Length && paint.Highlight[index];
            var textSize = measure.MeasureString(text, font, int.MaxValue, format);
            var width = Math.Max(1f, textSize.Width) + (WordGap * 2) + (marked ? MarkPad * 2 : 0);
            var height = Math.Max(1f, textSize.Height);
            if (row is null || (used > 0 && used + width > limit))
            {
                row = new RowLayout();
                rows.Add(row);
                used = 0;
            }

            row.Words.Add(new WordLayout(text, marked, used, width, textSize.Width, height));
            used += width;
            row.Width = used;
            row.Height = Math.Max(row.Height, height);
        }

        var textWidth = 0f;
        var textHeight = 0f;
        foreach (var line in rows)
        {
            textWidth = Math.Max(textWidth, line.Width);
            if (textHeight > 0)
            {
                textHeight += RowGap;
            }

            line.Y = textHeight;
            textHeight += line.Height;
        }

        var plate = new RectangleF(0, 0, textWidth + (PadX * 2), textHeight + (PadY * 2));
        if (rows.Count == 0)
        {
            return new LineLayout(plate, rows, scaleX, scaleY, surface);
        }

        if (paint.Anchor is { } anchor && anchor.Width >= 1 && anchor.Height >= 1
            && double.IsFinite(anchor.X) && double.IsFinite(anchor.Y))
        {
            // The Save button sits to the right of the words, so they are left of the
            // plate center. A freshly centered line would move them right.
            plate.X = (float)(anchor.X - PadX);
            plate.Y = (float)(anchor.Y - PadY);
        }
        else
        {
            plate.X = (float)((surface.PictureWidth - plate.Width) / 2);
            plate.Y = paint.Style.Spot switch
            {
                SubtitleSpot.Top => (float)SubtitleLook.TopGap,
                SubtitleSpot.Middle => (float)((surface.PictureHeight - plate.Height) / 2),
                _ => (float)(surface.PictureHeight - SubtitleLook.BottomGap - plate.Height)
            };
            plate = KeepOnVideo(plate, surface);
        }
        return new LineLayout(plate, rows, scaleX, scaleY, surface);
    }

    private static CaptionSurface FittedVideo(CaptionSurface surface, int pixelWidth, int pixelHeight)
    {
        var dipAspect = surface.VideoWidth / surface.VideoHeight;
        var pixAspect = pixelWidth / (double)pixelHeight;
        if (!double.IsFinite(dipAspect) || !double.IsFinite(pixAspect) || pixAspect <= 0
            || Math.Abs(dipAspect - pixAspect) / pixAspect < 0.02)
        {
            return surface;
        }

        double x;
        double y;
        double width;
        double height;
        if (dipAspect > pixAspect)
        {
            height = surface.VideoHeight;
            width = height * pixAspect;
            x = surface.VideoX + ((surface.VideoWidth - width) / 2);
            y = surface.VideoY;
        }
        else
        {
            width = surface.VideoWidth;
            height = width / pixAspect;
            x = surface.VideoX;
            y = surface.VideoY + ((surface.VideoHeight - height) / 2);
        }

        if (width < 2 || height < 2)
        {
            return surface;
        }

        return surface with
        {
            VideoX = x,
            VideoY = y,
            VideoWidth = width,
            VideoHeight = height
        };
    }

    private static RectangleF KeepOnVideo(RectangleF plate, CaptionSurface surface)
    {
        var left = (float)surface.VideoX;
        var top = (float)surface.VideoY;
        var right = (float)(surface.VideoX + surface.VideoWidth);
        var bottom = (float)(surface.VideoY + surface.VideoHeight);
        if (right - left > (Edge * 2) + plate.Width)
        {
            left += Edge;
            right -= Edge;
        }

        if (bottom - top > (Edge * 2) + plate.Height)
        {
            top += Edge;
            bottom -= Edge;
        }

        if (plate.X < left)
        {
            plate.X = left;
        }

        if (plate.Y < top)
        {
            plate.Y = top;
        }

        if (plate.Right > right)
        {
            plate.X = right - plate.Width;
        }

        if (plate.Bottom > bottom)
        {
            plate.Y = bottom - plate.Height;
        }

        return plate;
    }

    private static void PaintPlate(Graphics graphics, SubtitleStyle style, LineLayout layout)
    {
        if (style.Plate == SubtitlePlate.None)
        {
            return;
        }

        var rect = Map(layout.Plate, layout);
        using var path = Round(rect, Radius * (float)layout.ScaleY);
        using var brush = new SolidBrush(ColorOf(SubtitleLook.PlateArgb(style.Plate)));
        graphics.FillPath(brush, path);
    }

    private static void PaintWords(Graphics graphics, CaptionPaint paint, LineLayout layout)
    {
        using var font = CreateFont(SubtitleLook.FontSize(paint.Style.Size) * (float)layout.ScaleY);
        using var format = TextFormat();
        using var textBrush = new SolidBrush(ColorOf(SubtitleLook.TextArgb(paint.Style.Color)));
        using var markBrush = new SolidBrush(ColorOf(SubtitleLook.TextArgb(SubtitleTextColor.Yellow)));
        using var markText = new SolidBrush(Color.FromArgb(255, 28, 28, 28));
        var originX = layout.Plate.X + PadX;
        var originY = layout.Plate.Y + PadY;
        foreach (var row in layout.Rows)
        {
            var rowX = originX + ((layout.Plate.Width - (PadX * 2) - row.Width) / 2f);
            foreach (var word in row.Words)
            {
                var x = rowX + word.X;
                var y = originY + row.Y;
                if (word.Marked)
                {
                    var chip = Map(new RectangleF(x + WordGap, y, word.Width - (WordGap * 2), row.Height), layout);
                    using var path = Round(chip, MarkRadius * (float)layout.ScaleY);
                    graphics.FillPath(path: path, brush: markBrush);
                }

                var text = Map(new RectangleF(x + WordGap + (word.Marked ? MarkPad : 0), y, word.TextWidth, word.TextHeight), layout);
                graphics.DrawString(word.Text, font, word.Marked ? markText : textBrush, text.X, text.Y, format);
            }
        }
    }

    private static RectangleF Map(RectangleF dip, LineLayout layout)
    {
        var surface = layout.Surface;
        var x = (float)((dip.X - surface.VideoX) * layout.ScaleX);
        var y = (float)((dip.Y - surface.VideoY) * layout.ScaleY);
        var width = (float)(dip.Width * layout.ScaleX);
        var height = (float)(dip.Height * layout.ScaleY);
        return new RectangleF(x, y, width, height);
    }

    private static float WrapLimit(double pictureWidth, bool reserveSaveButton)
    {
        var reserve = reserveSaveButton ? SaveButtonReserve : 0;
        var max = pictureWidth - (SubtitleLook.SideMargin * 2) - reserve;
        if (double.IsNaN(max) || max < 120)
        {
            return float.PositiveInfinity;
        }

        return (float)max;
    }

    private static StringFormat TextFormat()
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        format.Trimming = StringTrimming.None;
        format.Alignment = StringAlignment.Near;
        format.LineAlignment = StringAlignment.Near;
        return format;
    }

    private static Font CreateFont(float size)
    {
        var pixels = Math.Max(1f, size);
        try
        {
            return new Font("Segoe UI Semibold", pixels, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch (Exception)
        {
            return new Font(FontFamily.GenericSansSerif, pixels, FontStyle.Bold, GraphicsUnit.Pixel);
        }
    }

    private static GraphicsPath Round(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(Math.Max(0f, radius) * 2f, Math.Min(rect.Width, rect.Height));
        if (diameter < 1f)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ColorOf(uint argb)
        => Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));

    private sealed class LineLayout
    {
        internal LineLayout(RectangleF plate, List<RowLayout> rows, double scaleX, double scaleY, CaptionSurface surface)
        {
            Plate = plate;
            Rows = rows;
            ScaleX = scaleX;
            ScaleY = scaleY;
            Surface = surface;
        }

        internal RectangleF Plate { get; }

        internal List<RowLayout> Rows { get; }

        internal double ScaleX { get; }

        internal double ScaleY { get; }

        internal CaptionSurface Surface { get; }
    }

    private sealed class RowLayout
    {
        internal float Width { get; set; }

        internal float Height { get; set; }

        internal float Y { get; set; }

        internal List<WordLayout> Words { get; } = [];
    }

    private sealed record WordLayout(string Text, bool Marked, float X, float Width, float TextWidth, float TextHeight);
}
