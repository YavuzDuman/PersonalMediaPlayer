using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PersonalMediaPlayer.App.Converters;

public sealed class PathToImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return DependencyProperty.UnsetValue;
        }

        var image = new BitmapImage
        {
            UriSource = new Uri(path, UriKind.Absolute),
            CreateOptions = BitmapCreateOptions.IgnoreImageCache
        };

        if (parameter is string text && int.TryParse(text, out var width) && width > 0)
        {
            image.DecodePixelWidth = width;
        }

        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
