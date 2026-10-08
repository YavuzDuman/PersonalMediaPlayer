using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class ContinueWatchCard : UserControl
{
    private bool _playClick;

    public ContinueWatchCard()
    {
        InitializeComponent();
    }

    public event EventHandler? Chosen;

    public void Show(string title, string place, double fraction, string? filePath, string? imageUrl = null, bool showPlay = true)
    {
        Title.Text = title;
        Place.Text = place;
        var play = showPlay ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Visibility = play;
        PlayShade.Visibility = play;
        ToolTipService.SetToolTip(this, title + Environment.NewLine + place);
        if (fraction > 0 && fraction < 1)
        {
            Progress.Value = fraction * 100;
            Progress.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            Progress.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _ = LoadThumbAsync(filePath);
        }
        else if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            LoadRemoteThumb(imageUrl);
        }
    }

    private void LoadRemoteThumb(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var imageUri))
        {
            return;
        }

        try
        {
            Thumb.Source = new BitmapImage
            {
                DecodePixelWidth = 440,
                UriSource = imageUri
            };
        }
        catch (Exception)
        {
            Thumb.Source = null;
        }
    }

    private void Thumb_Failed(object sender, Microsoft.UI.Xaml.ExceptionRoutedEventArgs e)
    {
        Thumb.Source = null;
    }

    private async Task LoadThumbAsync(string path)
    {
        try
        {
            var image = await VideoThumbnail.LoadAsync(path, 440);
            if (image is not null)
            {
                Thumb.Source = image;
            }
        }
        catch (Exception)
        {
            Thumb.Source = null;
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        _playClick = true;
        Chosen?.Invoke(this, EventArgs.Empty);
    }

    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_playClick || (e.OriginalSource is DependencyObject source && IsInside(source, PlayButton)))
        {
            _playClick = false;
            return;
        }

        e.Handled = true;
        Chosen?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsInside(DependencyObject source, DependencyObject ancestor)
    {
        for (DependencyObject? node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
