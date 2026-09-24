using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using DrawingImage = System.Drawing.Image;

namespace PersonalMediaPlayer.App.Editing;

internal static class PhotoRenderer
{
    public static async Task<RenderedPhoto> RenderAsync(
        string sourcePath,
        int width,
        int height,
        double rotationDegrees,
        CancellationToken cancellationToken = default)
    {
        width = Math.Clamp(width, 1, 16384);
        height = Math.Clamp(height, 1, 16384);
        rotationDegrees %= 360;
        if (rotationDegrees < 0)
        {
            rotationDegrees += 360;
        }

        var sourceBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        using var source = await LoadBitmapAsync(sourceBytes);
        using var resized = Resize(source, width, height);
        using var rotated = Rotate(resized, rotationDegrees);

        var keepOriginalFormat = IsRightAngle(rotationDegrees);
        var extension = Path.GetExtension(sourcePath);
        var format = keepOriginalFormat ? FormatFromExtension(extension) : ImageFormat.Png;
        var outputExtension = keepOriginalFormat ? extension : ".png";

        var output = new MemoryStream();
        SaveImage(rotated, output, format);
        output.Position = 0;
        return new RenderedPhoto(output, outputExtension);
    }

    private static async Task<Bitmap> LoadBitmapAsync(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = DrawingImage.FromStream(stream);
            return new Bitmap(image);
        }
        catch (Exception)
        {
            return await LoadBitmapWithDecoderAsync(bytes);
        }
    }

    private static async Task<Bitmap> LoadBitmapWithDecoderAsync(byte[] bytes)
    {
        using var ras = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
        }

        ras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ras);
        var software = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        var length = software.PixelWidth * software.PixelHeight * 4;
        var pixels = new byte[length];
        software.CopyToBuffer(pixels.AsBuffer());

        var bitmap = new Bitmap(software.PixelWidth, software.PixelHeight, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.WriteOnly,
            bitmap.PixelFormat);
        try
        {
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        if (source.Width == width && source.Height == height)
        {
            return new Bitmap(source);
        }

        var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        result.SetResolution(source.HorizontalResolution, source.VerticalResolution);
        using var graphics = Graphics.FromImage(result);
        ApplyQuality(graphics, sourceOver: false);
        graphics.DrawImage(source, 0, 0, width, height);
        return result;
    }

    private static Bitmap Rotate(Bitmap source, double angle)
    {
        if (Math.Abs(angle) < 0.01)
        {
            return new Bitmap(source);
        }

        if (Math.Abs(angle - 90) < 0.01)
        {
            var copy = new Bitmap(source);
            copy.RotateFlip(RotateFlipType.Rotate90FlipNone);
            return copy;
        }

        if (Math.Abs(angle - 180) < 0.01)
        {
            var copy = new Bitmap(source);
            copy.RotateFlip(RotateFlipType.Rotate180FlipNone);
            return copy;
        }

        if (Math.Abs(angle - 270) < 0.01)
        {
            var copy = new Bitmap(source);
            copy.RotateFlip(RotateFlipType.Rotate270FlipNone);
            return copy;
        }

        var radians = angle * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var newWidth = (int)Math.Ceiling(source.Width * cos + source.Height * sin);
        var newHeight = (int)Math.Ceiling(source.Width * sin + source.Height * cos);

        var result = new Bitmap(Math.Max(1, newWidth), Math.Max(1, newHeight), PixelFormat.Format32bppPArgb);
        result.SetResolution(source.HorizontalResolution, source.VerticalResolution);
        using var graphics = Graphics.FromImage(result);
        ApplyQuality(graphics, sourceOver: true);
        graphics.Clear(Color.Transparent);
        graphics.TranslateTransform(result.Width / 2f, result.Height / 2f);
        graphics.RotateTransform((float)angle);
        graphics.TranslateTransform(-source.Width / 2f, -source.Height / 2f);
        graphics.DrawImage(source, 0, 0);
        return result;
    }

    private static void ApplyQuality(Graphics graphics, bool sourceOver)
    {
        graphics.CompositingMode = sourceOver ? CompositingMode.SourceOver : CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    private static void SaveImage(Bitmap bitmap, Stream output, ImageFormat format)
    {
        if (format.Equals(ImageFormat.Jpeg))
        {
            using var rgb = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(rgb);
            graphics.Clear(Color.White);
            graphics.DrawImage(bitmap, 0, 0);
            rgb.Save(output, ImageFormat.Jpeg);
            return;
        }

        bitmap.Save(output, format);
    }

    private static bool IsRightAngle(double angle)
        => Math.Abs(angle % 90) < 0.01;

    private static ImageFormat FormatFromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ImageFormat.Jpeg,
        ".bmp" => ImageFormat.Bmp,
        ".gif" => ImageFormat.Gif,
        _ => ImageFormat.Png
    };
}

internal sealed record RenderedPhoto(MemoryStream Stream, string Extension) : IDisposable
{
    public void Dispose() => Stream.Dispose();
}
