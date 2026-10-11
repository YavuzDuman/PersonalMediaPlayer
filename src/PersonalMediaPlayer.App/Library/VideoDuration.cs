using Windows.Storage;

namespace PersonalMediaPlayer.App.Library;

internal static class VideoDuration
{
    // A file Windows cannot read has no length. The rest of the library still sorts.
    internal static async Task<TimeSpan?> ReadAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            var properties = await file.Properties.GetVideoPropertiesAsync();
            var duration = properties.Duration;
            return duration > TimeSpan.Zero ? duration : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
