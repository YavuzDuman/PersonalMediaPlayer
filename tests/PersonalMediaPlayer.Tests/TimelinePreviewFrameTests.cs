using System.Diagnostics;
using PersonalMediaPlayer.App.Editing;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class TimelinePreviewFrameTests
{
    [Fact]
    public async Task ALaterMomentIsNotTheDarkOpening()
    {
        var ffmpeg = TimelinePreviewFrame.FindFfmpeg();
        if (ffmpeg is null)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "pmp-timeline-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "clip.mp4");
        try
        {
            Assert.True(await RenderClip(ffmpeg, path), "Could not render the preview clip.");

            var opening = await TimelinePreviewFrame.GrabAsync(path, TimeSpan.FromSeconds(0.2), CancellationToken.None);
            var later = await TimelinePreviewFrame.GrabAsync(path, TimeSpan.FromSeconds(1.5), CancellationToken.None);

            Assert.NotNull(opening);
            Assert.NotNull(later);
            Assert.True(Average(opening!.Value) < 40, "The opening of the clip is black.");
            Assert.True(Average(later!.Value) > 180, "A later moment stays bright.");
            Assert.Equal(255, later.Value.Bgra[3]);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TheTopOfThePictureStaysOnTop()
    {
        var ffmpeg = TimelinePreviewFrame.FindFfmpeg();
        if (ffmpeg is null)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "pmp-timeline-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "stacked.mp4");
        try
        {
            Assert.True(await RenderStacked(ffmpeg, path), "Could not render the stacked clip.");
            var frame = await TimelinePreviewFrame.GrabAsync(path, TimeSpan.FromSeconds(0.2), CancellationToken.None);
            Assert.NotNull(frame);
            var top = Band(frame!.Value, 0, frame.Value.Height / 5);
            var bottom = Band(frame.Value, frame.Value.Height * 4 / 5, frame.Value.Height);
            Assert.True(top > 180, "The white band stays at the top.");
            Assert.True(bottom < 40, "The black band stays at the bottom.");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static int Band(TimelinePreviewFrame.PreviewPixels pixels, int startRow, int endRow)
    {
        long sum = 0;
        var count = 0;
        var width = pixels.Width;
        for (var y = startRow; y < endRow; y++)
        {
            for (var x = 0; x < width; x += 4)
            {
                var index = (y * width + x) * 4;
                sum += pixels.Bgra[index] + pixels.Bgra[index + 1] + pixels.Bgra[index + 2];
                count++;
            }
        }

        return (int)(sum / Math.Max(1, count) / 3);
    }

    private static async Task<bool> RenderStacked(string ffmpeg, string path)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-loglevel");
        start.ArgumentList.Add("error");
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("lavfi");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add("color=c=white:s=160x45:r=25:d=1");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("lavfi");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add("color=c=black:s=160x45:r=25:d=1");
        start.ArgumentList.Add("-filter_complex");
        start.ArgumentList.Add("[0:v][1:v]vstack=inputs=2,format=yuv420p");
        start.ArgumentList.Add("-c:v");
        start.ArgumentList.Add("libx264");
        start.ArgumentList.Add("-preset");
        start.ArgumentList.Add("ultrafast");
        start.ArgumentList.Add("-pix_fmt");
        start.ArgumentList.Add("yuv420p");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start);
        if (process is null)
        {
            return false;
        }

        _ = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 && File.Exists(path) && new FileInfo(path).Length > 32;
    }

    private static int Average(TimelinePreviewFrame.PreviewPixels pixels)
    {
        long sum = 0;
        var count = 0;
        for (var index = 0; index + 2 < pixels.Bgra.Length; index += 16)
        {
            sum += pixels.Bgra[index] + pixels.Bgra[index + 1] + pixels.Bgra[index + 2];
            count++;
        }

        return (int)(sum / Math.Max(1, count) / 3);
    }

    private static async Task<bool> RenderClip(string ffmpeg, string path)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-loglevel");
        start.ArgumentList.Add("error");
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("lavfi");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add("color=c=black:s=160x90:r=25:d=1");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("lavfi");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add("color=c=white:s=160x90:r=25:d=1");
        start.ArgumentList.Add("-filter_complex");
        start.ArgumentList.Add("[0:v][1:v]concat=n=2:v=1:a=0,format=yuv420p");
        start.ArgumentList.Add("-c:v");
        start.ArgumentList.Add("libx264");
        start.ArgumentList.Add("-preset");
        start.ArgumentList.Add("ultrafast");
        start.ArgumentList.Add("-pix_fmt");
        start.ArgumentList.Add("yuv420p");
        start.ArgumentList.Add(path);

        using var process = Process.Start(start);
        if (process is null)
        {
            return false;
        }

        _ = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 && File.Exists(path) && new FileInfo(path).Length > 32;
    }
}
