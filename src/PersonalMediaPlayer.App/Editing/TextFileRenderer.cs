using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text;

namespace PersonalMediaPlayer.App.Editing;

internal static class TextFileRenderer
{
    public const int PageWidth = 1200;
    public const int MaxHeight = 24000;
    private const int Padding = 32;
    private const int FontSize = 16;

    public static Bitmap Render(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The text file no longer exists.", path);
        }

        var (raw, truncatedBySize) = ReadText(path);
        raw = raw.Replace("\t", "    ").Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = "(empty file)";
        }

        using var font = CreateFont();
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.Word,
            FormatFlags = StringFormatFlags.LineLimit
        };

        var contentWidth = PageWidth - (Padding * 2);
        var maxContentHeight = MaxHeight - (Padding * 2);
        float measuredHeight;
        int charsFitted;
        using (var probe = new Bitmap(1, 1))
        using (var graphics = Graphics.FromImage(probe))
        {
            Configure(graphics);
            var size = graphics.MeasureString(raw, font, new SizeF(contentWidth, maxContentHeight), format, out charsFitted, out _);
            measuredHeight = size.Height;
        }

        var truncated = truncatedBySize || charsFitted < raw.Length;
        var footer = truncated ? "\n\n… truncated (file is longer than the image limit)." : string.Empty;
        var body = truncated ? raw[..Math.Max(0, charsFitted)].TrimEnd() + footer : raw;
        var height = (int)Math.Clamp(Math.Ceiling(measuredHeight) + (Padding * 2) + (truncated ? 48 : 0), Padding * 3, MaxHeight);

        var bitmap = new Bitmap(PageWidth, height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            Configure(graphics);
            graphics.Clear(Color.White);
            using var brush = new SolidBrush(Color.FromArgb(255, 32, 32, 32));
            graphics.DrawString(
                body,
                font,
                brush,
                new RectangleF(Padding, Padding, contentWidth, height - (Padding * 2)),
                format);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private const int MaxCharacters = 2_000_000;

    private static (string Text, bool Truncated) ReadText(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[MaxCharacters];
        var read = reader.Read(buffer, 0, buffer.Length);
        return (new string(buffer, 0, read), !reader.EndOfStream);
    }

    private static Font CreateFont()
    {
        try
        {
            return new Font("Consolas", FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(FontFamily.GenericMonospace, FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }

    private static void Configure(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }
}
