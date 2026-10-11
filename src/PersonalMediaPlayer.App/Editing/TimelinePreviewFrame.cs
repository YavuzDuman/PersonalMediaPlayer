using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PersonalMediaPlayer.App.Editing;

// Windows paints a black frame when it cannot decode the video, including AV1.
// ffmpeg reads the moment under the pointer, which is the picture the player shows.
internal static class TimelinePreviewFrame
{
    internal readonly record struct PreviewPixels(int Width, int Height, byte[] Bgra);

    internal static async Task<PreviewPixels?> GrabAsync(string path, TimeSpan time, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var png = await ReadPngAsync(path, time, cancellationToken);
        if (png is null || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return FromEncoded(png);
    }

    internal static PreviewPixels? FromEncoded(byte[] encoded) => ToPixels(encoded);

    internal static string? FindFfmpeg()
    {
        var bundled = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalMediaPlayer",
            "tools",
            "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<byte[]?> ReadPngAsync(string path, TimeSpan time, CancellationToken cancellationToken)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            return null;
        }

        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        // Seek to one second before the moment, then decode forward. A keyframe-only
        // jump stays on an earlier picture, which is often the dark opening.
        var lead = time > TimeSpan.FromSeconds(1) ? time - TimeSpan.FromSeconds(1) : TimeSpan.Zero;
        var fine = time - lead;
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-loglevel");
        start.ArgumentList.Add("error");
        start.ArgumentList.Add("-ss");
        start.ArgumentList.Add(Seconds(lead));
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(path);
        start.ArgumentList.Add("-ss");
        start.ArgumentList.Add(Seconds(fine));
        start.ArgumentList.Add("-map");
        start.ArgumentList.Add("0:v:0");
        start.ArgumentList.Add("-frames:v");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add("-an");
        start.ArgumentList.Add("-vf");
        start.ArgumentList.Add("scale=320:-2");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("image2pipe");
        start.ArgumentList.Add("-vcodec");
        start.ArgumentList.Add("png");
        start.ArgumentList.Add("pipe:1");

        using var process = Process.Start(start);
        if (process is null)
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var cancel = linked.Token.Register(() => TryKill(process));
        var errors = process.StandardError.ReadToEndAsync();
        using var copy = new MemoryStream();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(copy, linked.Token);
            await process.WaitForExitAsync(linked.Token);
            await errors;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            TryKill(process);
            return null;
        }

        if (linked.Token.IsCancellationRequested || process.ExitCode != 0)
        {
            return null;
        }

        var png = copy.ToArray();
        return IsPng(png) ? png : null;
    }

    private static PreviewPixels? ToPixels(byte[] png)
    {
        try
        {
            using var input = new MemoryStream(png);
            using var image = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: false);
            if (image.Width < 2 || image.Height < 2)
            {
                return null;
            }

            using var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(image, 0, 0, image.Width, image.Height);
            }

            var width = bitmap.Width;
            var height = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[width * height * 4];
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * width * 4, width * 4);
                }

                for (var index = 3; index < pixels.Length; index += 4)
                {
                    pixels[index] = 255;
                }

                return new PreviewPixels(width, height, pixels);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static string Seconds(TimeSpan time)
        => time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool IsPng(byte[] png)
        => png.Length > 32
           && png[0] == 0x89
           && png[1] == 0x50
           && png[2] == 0x4E
           && png[3] == 0x47;

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // The process already ended.
        }
    }
}
