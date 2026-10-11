using System.Diagnostics;
using PersonalMediaPlayer.App.Editing;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class TimelinePreviewCacheTests
{
    [Fact]
    public void SamplesStayCloseToThePointer()
    {
        Assert.Equal(100, TimelinePreviewCache.SlotMs(0));
        Assert.Equal(100, TimelinePreviewCache.SlotMs(-1));
        Assert.Equal(100, TimelinePreviewCache.SlotMs(double.NaN));
        Assert.Equal(100, TimelinePreviewCache.SlotMs(double.PositiveInfinity));
        Assert.Equal(100, TimelinePreviewCache.SlotMs(10));
        Assert.Equal(100, TimelinePreviewCache.SlotMs(6 * 60));
        Assert.Equal(285, TimelinePreviewCache.SlotMs(19 * 60));
        Assert.Equal(500, TimelinePreviewCache.SlotMs(2 * 60 * 60));
    }

    [Fact]
    public void FramesCloserThanTheSlotAreDropped()
    {
        Assert.True(TimelinePreviewCache.KeepFrame(0, -1, 2_000));
        Assert.False(TimelinePreviewCache.KeepFrame(1_999, 0, 2_000));
        Assert.True(TimelinePreviewCache.KeepFrame(2_000, 0, 2_000));
    }

    [Fact]
    public void AFarFrameIsNotUsed()
    {
        long[] times = [0, 4_000, 9_000];
        Assert.Equal(-1, TimelinePreviewCache.Choose([], 1_000, 200));
        Assert.Equal(-1, TimelinePreviewCache.Choose(times, -1, 200));
        Assert.Equal(-1, TimelinePreviewCache.Choose(times, 6_500, 200));
        Assert.Equal(-1, TimelinePreviewCache.Choose(times, 120_000, 200));
        Assert.Equal(0, TimelinePreviewCache.Choose(times, 50, 200));
        Assert.Equal(1, TimelinePreviewCache.Choose(times, 4_100, 200));
        Assert.Equal(-1, TimelinePreviewCache.Choose([20_000], 0, 200));
        Assert.Equal(0, TimelinePreviewCache.Choose([20_000], 20_100, 200));
    }

    [Fact]
    public void ARememberedMomentIsReused()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pmp-timeline-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "clip.bin");
        TimelinePreviewCache.Cancel();
        TimelinePreviewCache.ClearMemory();
        TimelinePreviewCache.StoreOverride = folder;
        try
        {
            File.WriteAllBytes(path, new byte[32]);
            var pixels = new TimelinePreviewFrame.PreviewPixels(2, 2,
            [
                10, 20, 30, 255,
                10, 20, 30, 255,
                40, 50, 60, 255,
                40, 50, 60, 255
            ]);
            TimelinePreviewCache.Remember(path, TimeSpan.FromSeconds(3), pixels);

            var near = TimelinePreviewCache.TryGet(path, TimeSpan.FromSeconds(3.05));
            Assert.NotNull(near);
            Assert.Equal(30, near!.Value.Bgra[2]);
            Assert.Null(TimelinePreviewCache.TryGet(path, TimeSpan.FromSeconds(8)));
        }
        finally
        {
            TimelinePreviewCache.Cancel();
            TimelinePreviewCache.ClearMemory();
            TimelinePreviewCache.StoreOverride = null;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ASavedStripAnswersAgainWithoutReadingTheVideo()
    {
        var ffmpeg = TimelinePreviewFrame.FindFfmpeg();
        if (ffmpeg is null)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "pmp-timeline-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var userRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalMediaPlayer",
            "timeline-cache");
        var userFolders = Directory.Exists(userRoot) ? Directory.GetDirectories(userRoot).Length : 0;
        var path = Path.Combine(folder, "clip.mp4");
        TimelinePreviewCache.Cancel();
        TimelinePreviewCache.ClearMemory();
        TimelinePreviewCache.StoreOverride = folder;
        try
        {
            Assert.True(await RenderClip(ffmpeg, path), "Could not render the preview clip.");
            await TimelinePreviewCache.BuildAsync(path, 4);

            var opening = TimelinePreviewCache.TryGet(path, TimeSpan.FromSeconds(0.2));
            var later = TimelinePreviewCache.TryGet(path, TimeSpan.FromSeconds(3));
            Assert.NotNull(opening);
            Assert.NotNull(later);
            var dark = Average(opening!.Value);
            var bright = Average(later!.Value);
            Assert.True(dark < 40, $"The opening preview is dark ({dark}).");
            Assert.True(bright > 180, $"A later preview is bright ({bright}).");
            Assert.Equal(255, later.Value.Bgra[3]);

            TimelinePreviewCache.Cancel();
            Assert.NotNull(TimelinePreviewCache.TryGet(path, TimeSpan.FromSeconds(3)));

            TimelinePreviewCache.ClearMemory();
            var reloaded = TimelinePreviewCache.TryGet(path, TimeSpan.FromSeconds(3));
            Assert.NotNull(reloaded);
            Assert.True(Average(reloaded!.Value) > 180, "The saved strip is read from disk.");

            File.AppendAllText(path, "x");
            Assert.Null(TimelinePreviewCache.TryGet(path, TimeSpan.FromSeconds(3)));
            Assert.Equal(userFolders, Directory.Exists(userRoot) ? Directory.GetDirectories(userRoot).Length : 0);
        }
        finally
        {
            TimelinePreviewCache.Cancel();
            TimelinePreviewCache.ClearMemory();
            TimelinePreviewCache.StoreOverride = null;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
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
        start.ArgumentList.Add("-nostdin");
        start.ArgumentList.Add("-loglevel");
        start.ArgumentList.Add("error");
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("lavfi");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add("color=c=black:s=160x90:r=25:d=2");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("lavfi");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add("color=c=white:s=160x90:r=25:d=2");
        start.ArgumentList.Add("-filter_complex");
        start.ArgumentList.Add("[0:v][1:v]concat=n=2:v=1:a=0,format=yuv420p");
        start.ArgumentList.Add("-c:v");
        start.ArgumentList.Add("libx264");
        start.ArgumentList.Add("-preset");
        start.ArgumentList.Add("ultrafast");
        start.ArgumentList.Add("-pix_fmt");
        start.ArgumentList.Add("yuv420p");
        start.ArgumentList.Add("-g");
        start.ArgumentList.Add("250");
        start.ArgumentList.Add("-keyint_min");
        start.ArgumentList.Add("250");
        start.ArgumentList.Add("-sc_threshold");
        start.ArgumentList.Add("0");
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
