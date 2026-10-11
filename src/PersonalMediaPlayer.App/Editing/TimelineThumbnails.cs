using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PersonalMediaPlayer.App.Editing;

internal sealed class TimelineThumbnails : IDisposable
{
    private CancellationTokenSource? _cancel;
    private int _generation;

    public void Cancel()
    {
        _generation++;
        _cancel?.Cancel();
    }

    public void ReleaseFile() => Cancel();

    public void Dispose()
    {
        Cancel();
        _cancel?.Dispose();
        _cancel = null;
    }

    public async Task<WriteableBitmap?> GrabAsync(string path, TimeSpan time)
    {
        var generation = _generation;
        var previous = _cancel;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        previous?.Cancel();
        previous?.Dispose();
        TimelinePreviewFrame.PreviewPixels? frame;
        try
        {
            // A saved frame is used only when it is the moment under the pointer.
            // Otherwise read that moment, then keep it for the next hover.
            frame = TimelinePreviewCache.TryGet(path, time);
            if (frame is null && !token.IsCancellationRequested)
            {
                frame = await TimelinePreviewFrame.GrabAsync(path, time, token);
                if (frame is not null)
                {
                    TimelinePreviewCache.Remember(path, time, frame.Value);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        if (frame is null || generation != _generation)
        {
            return null;
        }

        var picture = frame.Value;
        var bitmap = new WriteableBitmap(picture.Width, picture.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(picture.Bgra, 0, picture.Bgra.Length);
        }

        bitmap.Invalidate();
        return bitmap;
    }
}
