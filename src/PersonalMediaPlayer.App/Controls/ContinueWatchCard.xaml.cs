using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class ContinueWatchCard : UserControl
{
    public ContinueWatchCard()
    {
        InitializeComponent();
    }

    public event EventHandler? Chosen;

    public void Show(string title, string place, double fraction, string? filePath)
    {
        Title.Text = title;
        Place.Text = place;
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
    }

    private async Task LoadThumbAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 440);
            if (thumb is null || thumb.Size == 0)
            {
                return;
            }

            var image = new BitmapImage();
            await image.SetSourceAsync(thumb);
            Thumb.Source = image;
            PlayMark.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        }
        catch (Exception)
        {
            Thumb.Source = null;
        }
    }

    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        Chosen?.Invoke(this, EventArgs.Empty);
    }
}
