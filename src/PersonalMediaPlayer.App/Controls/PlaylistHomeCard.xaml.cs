using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PersonalMediaPlayer.App.Controls;

public readonly record struct PlaylistCoverShot(string? FilePath, string? ImageUrl);

public sealed partial class PlaylistHomeCard : UserControl
{
    private bool _playClick;

    public PlaylistHomeCard()
    {
        InitializeComponent();
    }

    public event EventHandler? PlayChosen;

    public event EventHandler? OpenChosen;

    public void Show(string name, string detail, bool canPlay, IReadOnlyList<PlaylistCoverShot> shots)
    {
        Title.Text = name;
        Count.Text = detail;
        ToolTipService.SetToolTip(this, name + Environment.NewLine + detail);
        var showPlay = canPlay ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Visibility = showPlay;
        PlayShade.Visibility = showPlay;
        EmptyMark.Visibility = shots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BuildMosaic(shots);
    }

    private void BuildMosaic(IReadOnlyList<PlaylistCoverShot> shots)
    {
        Mosaic.Children.Clear();
        Mosaic.ColumnDefinitions.Clear();
        Mosaic.RowDefinitions.Clear();
        if (shots.Count == 0)
        {
            return;
        }

        var columns = shots.Count == 1 ? 1 : 2;
        var rows = shots.Count <= 2 ? 1 : 2;
        for (var column = 0; column < columns; column++)
        {
            Mosaic.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var row = 0; row < rows; row++)
        {
            Mosaic.RowDefinitions.Add(new RowDefinition());
        }

        Mosaic.ColumnSpacing = shots.Count > 1 ? 2 : 0;
        Mosaic.RowSpacing = shots.Count > 2 ? 2 : 0;
        for (var index = 0; index < shots.Count; index++)
        {
            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false
            };
            image.ImageFailed += (_, _) => image.Source = null;
            PlaceShot(image, index, shots.Count);
            Mosaic.Children.Add(image);
            var shot = shots[index];
            if (!string.IsNullOrWhiteSpace(shot.FilePath))
            {
                _ = LoadFileAsync(image, shot.FilePath);
            }
            else if (!string.IsNullOrWhiteSpace(shot.ImageUrl))
            {
                LoadRemote(image, shot.ImageUrl);
            }
        }
    }

    private static void PlaceShot(Image image, int index, int count)
    {
        if (count == 3 && index == 0)
        {
            Grid.SetRow(image, 0);
            Grid.SetColumn(image, 0);
            Grid.SetRowSpan(image, 2);
            return;
        }

        if (count == 3)
        {
            Grid.SetColumn(image, 1);
            Grid.SetRow(image, index - 1);
            return;
        }

        if (count == 2)
        {
            Grid.SetColumn(image, index);
            return;
        }

        Grid.SetColumn(image, index % 2);
        Grid.SetRow(image, index / 2);
    }

    private static void LoadRemote(Image target, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var imageUri))
        {
            return;
        }

        try
        {
            target.Source = new BitmapImage
            {
                DecodePixelWidth = 320,
                UriSource = imageUri
            };
        }
        catch (Exception)
        {
            target.Source = null;
        }
    }

    private static async Task LoadFileAsync(Image target, string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 240);
            if (thumb is null || thumb.Size == 0)
            {
                return;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumb);
            target.Source = bitmap;
        }
        catch (Exception)
        {
            target.Source = null;
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        _playClick = true;
        PlayChosen?.Invoke(this, EventArgs.Empty);
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_playClick || (e.OriginalSource is DependencyObject source && IsInside(source, PlayButton)))
        {
            _playClick = false;
            return;
        }

        e.Handled = true;
        OpenChosen?.Invoke(this, EventArgs.Empty);
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
