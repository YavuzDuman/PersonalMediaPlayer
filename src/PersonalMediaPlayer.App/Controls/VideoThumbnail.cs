using Microsoft.UI.Xaml.Media.Imaging;
using PersonalMediaPlayer.App.Download;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PersonalMediaPlayer.App.Controls;

internal static class VideoThumbnail
{
    internal static async Task<BitmapImage?> LoadAsync(string path, int decodeWidth)
    {
        var poster = DownloadPoster.FindCached(path);
        if (poster is null && DownloadHistory.PageFor(path) is string page)
        {
            poster = await DownloadPoster.EnsureAsync(path, page);
        }

        if (poster is not null)
        {
            try
            {
                return new BitmapImage
                {
                    DecodePixelWidth = decodeWidth,
                    UriSource = new Uri(poster),
                    CreateOptions = BitmapCreateOptions.IgnoreImageCache
                };
            }
            catch (Exception)
            {
            }
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)Math.Max(decodeWidth, 1));
            if (thumb is null || thumb.Size == 0)
            {
                return null;
            }

            var image = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache };
            await image.SetSourceAsync(thumb);
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
