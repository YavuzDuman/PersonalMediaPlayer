using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalMediaPlayer.Core.Models;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class FolderRow : UserControl
{
    public static readonly DependencyProperty FolderProperty = DependencyProperty.Register(
        nameof(Folder),
        typeof(LibraryFolder),
        typeof(FolderRow),
        new PropertyMetadata(null, OnFolderChanged));

    private readonly Brush _emptyCoverBrush;
    private int _request;

    public FolderRow()
    {
        InitializeComponent();
        _emptyCoverBrush = CoverHost.Background;
    }

    public LibraryFolder? Folder
    {
        get => (LibraryFolder?)GetValue(FolderProperty);
        set => SetValue(FolderProperty, value);
    }

    private static void OnFolderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FolderRow row)
        {
            row.NameText.Text = row.Folder?.DisplayName ?? string.Empty;
            row.CountText.Text = row.Folder?.CountLabel ?? string.Empty;
            _ = row.RefreshCoverAsync();
        }
    }

    private async Task RefreshCoverAsync()
    {
        var request = ++_request;
        var folder = Folder;
        CoverHost.Background = _emptyCoverBrush;
        Placeholder.Visibility = Visibility.Visible;
        var path = folder?.CoverPath;
        var hasCover = !string.IsNullOrWhiteSpace(path);
        PlayMark.Visibility = hasCover && folder!.CoverIsVideo ? Visibility.Visible : Visibility.Collapsed;
        if (!hasCover || folder is null)
        {
            return;
        }

        if (!folder.CoverIsVideo)
        {
            var failed = false;
            var image = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                DecodePixelWidth = 192
            };
            image.ImageFailed += (_, _) =>
            {
                failed = true;
                if (request == _request)
                {
                    CoverHost.Background = _emptyCoverBrush;
                    Placeholder.Visibility = Visibility.Visible;
                }
            };
            image.UriSource = new Uri(path!, UriKind.Absolute);
            if (request != _request || failed)
            {
                return;
            }

            ShowCover(image);
            return;
        }

        var thumb = await TryLoadVideoThumbnailAsync(path!);
        if (request != _request || thumb is null)
        {
            return;
        }

        ShowCover(thumb);
    }

    private void ShowCover(ImageSource source)
    {
        CoverHost.Background = new ImageBrush
        {
            ImageSource = source,
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
        Placeholder.Visibility = Visibility.Collapsed;
    }

    private static async Task<BitmapImage?> TryLoadVideoThumbnailAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 160);
            if (thumb is null || thumb.Size == 0)
            {
                return null;
            }

            var image = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache };
            await image.SetSourceAsync(thumb);
            return image;
        }
        catch
        {
            return null;
        }
    }
}
