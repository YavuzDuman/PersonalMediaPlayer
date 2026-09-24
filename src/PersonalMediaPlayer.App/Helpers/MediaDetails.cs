using PersonalMediaPlayer.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PersonalMediaPlayer.App.Helpers;

internal static class MediaDetails
{
    public static async Task<string> DescribeAsync(MediaItem item)
    {
        if (item.IsVideo)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                var properties = await file.Properties.GetVideoPropertiesAsync();
                var duration = properties.Duration;
                var durationText = duration > TimeSpan.Zero
                    ? $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}"
                    : "Unknown";
                var size = properties.Width > 0 && properties.Height > 0
                    ? $"{properties.Width} × {properties.Height}"
                    : "Unknown";
                return $"Duration: {durationText}\nResolution: {size}";
            }
            catch
            {
                return "Video details unavailable";
            }
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(item.FilePath);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return $"Resolution: {decoder.PixelWidth} × {decoder.PixelHeight}";
        }
        catch
        {
            return "Image details unavailable";
        }
    }
}
