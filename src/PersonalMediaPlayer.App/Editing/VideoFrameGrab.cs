using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using LibVLCSharp.Shared;
using Microsoft.Graphics.Canvas;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;
using WinRtPlayer = Windows.Media.Playback.MediaPlayer;

namespace PersonalMediaPlayer.App.Editing;

internal static class VideoFrameGrab
{
    public static async Task<byte[]?> CaptureDisplayedAsync(VlcPlayer player)
    {
        // The other reader seeks the file and can land on a nearby frame.
        // This asks the player for the picture it is already showing.
        var folder = Path.Combine(Path.GetTempPath(), "PersonalMediaPlayer");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "frame-" + Guid.NewGuid().ToString("N") + ".png");
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnTaken(object? sender, MediaPlayerSnapshotTakenEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.Filename))
            {
                done.TrySetResult(args.Filename);
            }
        }

        player.SnapshotTaken += OnTaken;
        var written = path;
        try
        {
            if (!player.TakeSnapshot(0, path, 0, 0))
            {
                return null;
            }

            var completed = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            if (completed != done.Task)
            {
                return null;
            }

            written = await done.Task;
            var png = await File.ReadAllBytesAsync(written);
            return IsVisiblePng(png) ? png : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            player.SnapshotTaken -= OnTaken;
            TryDelete(path);
            if (!string.Equals(written, path, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(written);
            }
        }
    }

    public static async Task<byte[]> GrabPngAsync(string path, TimeSpan time)
    {
        try
        {
            var nearest = await GrabNearestAsync(path, time);
            if (nearest is { Length: > 32 })
            {
                return nearest;
            }

        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Some files cannot be opened as a composition. The frame server reads those.
        }

        try
        {
            return await GrabFrameServerAsync(path, time);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var ffmpeg = await GrabWithFfmpegAsync(path, time);
            if (ffmpeg is { Length: > 32 })
            {
                return ffmpeg;
            }

            throw;
        }
    }

    // Windows cannot decode every file the player can show. ffmpeg reads one picture then.
    private static async Task<byte[]?> GrabWithFfmpegAsync(string path, TimeSpan time)
    {
        var ffmpeg = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalMediaPlayer",
            "tools",
            "ffmpeg.exe");
        if (!File.Exists(ffmpeg) || !File.Exists(path))
        {
            return null;
        }

        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        try
        {
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
            start.ArgumentList.Add(lead.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(path);
            start.ArgumentList.Add("-ss");
            start.ArgumentList.Add(fine.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            start.ArgumentList.Add("-frames:v");
            start.ArgumentList.Add("1");
            start.ArgumentList.Add("-an");
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

            var errors = process.StandardError.ReadToEndAsync();
            using var copy = new MemoryStream();
            var output = process.StandardOutput.BaseStream.CopyToAsync(copy);
            var exited = process.WaitForExitAsync();
            var finished = await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(15)));
            if (finished != exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                }

                return null;
            }

            await output;
            await errors;
            var png = copy.ToArray();
            return IsVisiblePng(png) ? png : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<byte[]?> GrabNearestAsync(string path, TimeSpan time)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(file);
        var properties = clip.GetVideoEncodingProperties();
        var width = (int)properties.Width;
        var height = (int)properties.Height;
        if (width < 2 || height < 2)
        {
            return null;
        }

        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        var duration = clip.OriginalDuration;
        if (duration > TimeSpan.FromMilliseconds(40) && time >= duration)
        {
            time = duration - TimeSpan.FromMilliseconds(1);
        }

        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        using var thumb = await composition.GetThumbnailAsync(time, width, height, VideoFramePrecision.NearestFrame);
        using var managed = thumb.AsStreamForRead();
        using var copy = new MemoryStream();
        await managed.CopyToAsync(copy);
        return ToPng(copy.ToArray());
    }

    private static async Task<byte[]> GrabFrameServerAsync(string path, TimeSpan time)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var player = new WinRtPlayer
        {
            AutoPlay = false,
            IsMuted = true,
            IsVideoFrameServerEnabled = true
        };

        try
        {
            var opened = new TaskCompletionSource();
            player.MediaOpened += (_, _) => opened.TrySetResult();
            player.MediaFailed += (_, args) => opened.TrySetException(new InvalidOperationException(args.ErrorMessage));
            player.Source = MediaSource.CreateFromStorageFile(file);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(8));

            var width = player.PlaybackSession.NaturalVideoWidth;
            var height = player.PlaybackSession.NaturalVideoHeight;
            if (width < 2 || height < 2)
            {
                throw new InvalidOperationException("This moment could not be captured.");
            }

            if (time < TimeSpan.Zero)
            {
                time = TimeSpan.Zero;
            }

            var duration = player.PlaybackSession.NaturalDuration;
            if (duration > TimeSpan.Zero && time > duration)
            {
                time = duration;
            }

            using var target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), width, height, 96);
            var session = player.PlaybackSession;
            var seekDone = new TaskCompletionSource();
            session.SeekCompleted += (_, _) => seekDone.TrySetResult();
            var frame = new TaskCompletionSource();
            var targetMs = time.TotalMilliseconds;
            player.VideoFrameAvailable += (_, _) =>
            {
                if (frame.Task.IsCompleted || !seekDone.Task.IsCompleted)
                {
                    return;
                }

                var positionMs = session.Position.TotalMilliseconds;
                // A seek can present the previous keyframe first. Keep going until
                // the clock is within one frame of the requested moment.
                if (targetMs > 80 && positionMs + 40 < targetMs)
                {
                    return;
                }

                try
                {
                    player.CopyFrameToVideoSurface(target);
                    player.Pause();
                    frame.TrySetResult();
                }
                catch (Exception ex)
                {
                    frame.TrySetException(ex);
                }
            };

            session.Position = time;
            if (time < TimeSpan.FromMilliseconds(50))
            {
                seekDone.TrySetResult();
            }

            await seekDone.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var presented = await Task.WhenAny(frame.Task, Task.Delay(TimeSpan.FromMilliseconds(350)));
            if (presented != frame.Task)
            {
                player.Play();
            }

            await frame.Task.WaitAsync(TimeSpan.FromSeconds(8));
            player.Pause();

            using var stream = new InMemoryRandomAccessStream();
            await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            stream.Seek(0);
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            var png = new byte[stream.Size];
            reader.ReadBytes(png);
            return png;
        }
        finally
        {
            player.Dispose();
        }
    }

    private static byte[]? ToPng(byte[] encoded)
    {
        try
        {
            using var input = new MemoryStream(encoded);
            using var image = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: false);
            if (image.Width < 2 || image.Height < 2)
            {
                return null;
            }

            using var output = new MemoryStream();
            image.Save(output, ImageFormat.Png);
            var png = output.ToArray();
            return IsPng(png) ? png : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsVisiblePng(byte[] png)
    {
        if (!IsPng(png))
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(png);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            if (image.Width < 2 || image.Height < 2)
            {
                return false;
            }

            using var bitmap = new Bitmap(image);
            var stepX = Math.Max(1, bitmap.Width / 8);
            var stepY = Math.Max(1, bitmap.Height / 8);
            for (var y = stepY / 2; y < bitmap.Height; y += stepY)
            {
                for (var x = stepX / 2; x < bitmap.Width; x += stepX)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R > 3 || pixel.G > 3 || pixel.B > 3)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsPng(byte[] png)
        => png.Length > 32
           && png[0] == 0x89
           && png[1] == 0x50
           && png[2] == 0x4E
           && png[3] == 0x47;

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // The next capture uses its own file name.
        }
    }
}

internal sealed record PendingScreenshot(byte[] Png, int Width, int Height, string Name);
