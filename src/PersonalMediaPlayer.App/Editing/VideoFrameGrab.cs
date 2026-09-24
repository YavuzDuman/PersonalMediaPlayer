using Microsoft.Graphics.Canvas;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PersonalMediaPlayer.App.Editing;

internal static class VideoFrameGrab
{
    public static async Task<byte[]> GrabPngAsync(string path, TimeSpan time)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var player = new MediaPlayer
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
            session.Position = time;
            if (time < TimeSpan.FromMilliseconds(50))
            {
                seekDone.TrySetResult();
            }

            await seekDone.Task.WaitAsync(TimeSpan.FromSeconds(8));

            var frame = new TaskCompletionSource();
            player.VideoFrameAvailable += (_, _) =>
            {
                if (frame.Task.IsCompleted)
                {
                    return;
                }

                var position = session.Position;
                if (time > TimeSpan.FromMilliseconds(250) && position + TimeSpan.FromMilliseconds(200) < time)
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

            player.Play();
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
}

internal sealed record PendingScreenshot(byte[] Png, int Width, int Height, string Name);
