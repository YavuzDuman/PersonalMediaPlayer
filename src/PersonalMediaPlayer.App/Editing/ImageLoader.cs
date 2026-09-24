using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace PersonalMediaPlayer.App.Editing;

internal static class ImageLoader
{
    public static async Task<BitmapImage> LoadAsync(string path)
        => await LoadAsync(await File.ReadAllBytesAsync(path));

    public static async Task<BitmapImage> LoadAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
        }

        stream.Seek(0);
        var image = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache };
        await image.SetSourceAsync(stream);
        return image;
    }
}
