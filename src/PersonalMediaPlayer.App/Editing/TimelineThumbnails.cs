using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PersonalMediaPlayer.App.Editing;

internal sealed class TimelineThumbnails : IDisposable
{
    private MediaPlayer? _player;
    private string? _path;
    private int _generation;

    public void Cancel() => _generation++;

    public void Dispose()
    {
        _generation++;
        _player?.Dispose();
        _player = null;
        _path = null;
    }

    public async Task<WriteableBitmap?> GrabAsync(string path, TimeSpan time)
    {
        var generation = _generation;
        if (!await EnsureAsync(path, generation))
        {
            return null;
        }

        var player = _player;
        if (player is null || generation != _generation)
        {
            return null;
        }

        var width = player.PlaybackSession.NaturalVideoWidth;
        var height = player.PlaybackSession.NaturalVideoHeight;
        if (width < 2 || height < 2)
        {
            return null;
        }

        var scale = 320d / Math.Max(width, height);
        var targetWidth = Math.Max(2, (int)(width * Math.Min(1, scale)));
        var targetHeight = Math.Max(2, (int)(height * Math.Min(1, scale)));
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        var duration = player.PlaybackSession.NaturalDuration;
        if (duration > TimeSpan.Zero && time > duration)
        {
            time = duration;
        }

        using var target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), targetWidth, targetHeight, 96);
        var session = player.PlaybackSession;
        var seekDone = new TaskCompletionSource();
        void OnSeek(MediaPlaybackSession sender, object args) => seekDone.TrySetResult();
        session.SeekCompleted += OnSeek;
        try
        {
            session.Position = time;
            if (time < TimeSpan.FromMilliseconds(50))
            {
                seekDone.TrySetResult();
            }

            await seekDone.Task.WaitAsync(TimeSpan.FromSeconds(4));
            if (generation != _generation)
            {
                return null;
            }

            var frame = new TaskCompletionSource();
            void OnFrame(MediaPlayer sender, object args)
            {
                if (frame.Task.IsCompleted || generation != _generation)
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
            }

            player.VideoFrameAvailable += OnFrame;
            try
            {
                player.Play();
                await frame.Task.WaitAsync(TimeSpan.FromSeconds(4));
            }
            finally
            {
                player.VideoFrameAvailable -= OnFrame;
            }
        }
        finally
        {
            session.SeekCompleted -= OnSeek;
        }

        if (generation != _generation)
        {
            return null;
        }

        var pixels = target.GetPixelBytes();
        if (pixels.Length < targetWidth * targetHeight * 4)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(targetWidth, targetHeight);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, targetWidth * targetHeight * 4);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    private async Task<bool> EnsureAsync(string path, int generation)
    {
        if (_player is not null && string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
        {
            return generation == _generation;
        }

        _player?.Dispose();
        _player = null;
        _path = path;
        var player = new MediaPlayer
        {
            AutoPlay = false,
            IsMuted = true,
            IsVideoFrameServerEnabled = true
        };
        var opened = new TaskCompletionSource();
        player.MediaOpened += (_, _) => opened.TrySetResult();
        player.MediaFailed += (_, args) => opened.TrySetException(new InvalidOperationException(args.ErrorMessage));
        var file = await StorageFile.GetFileFromPathAsync(path);
        if (generation != _generation)
        {
            player.Dispose();
            return false;
        }

        player.Source = MediaSource.CreateFromStorageFile(file);
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(8));
        if (generation != _generation)
        {
            player.Dispose();
            return false;
        }

        _player = player;
        return true;
    }
}
