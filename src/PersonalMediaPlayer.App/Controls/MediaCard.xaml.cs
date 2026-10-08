using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core.Models;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class MediaCard : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item),
        typeof(MediaItem),
        typeof(MediaCard),
        new PropertyMetadata(null, OnItemChanged));

    public MediaCard()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DownloadPoster.Saved -= PosterSaved;
            DownloadPoster.Saved += PosterSaved;
        };
        Unloaded += (_, _) => DownloadPoster.Saved -= PosterSaved;
    }

    private void PosterSaved(string path)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => PosterSaved(path));
            return;
        }

        if (Item is null || string.IsNullOrWhiteSpace(Item.FilePath))
        {
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(Item.FilePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (string.Equals(full, path, StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshThumbnailAsync();
        }
    }

    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public string ImportedOn => Item is null
        ? string.Empty
        : Item.IsMissing
            ? "Missing"
            : Item.ImportedAt.ToLocalTime().ToString("g");

    public event PointerEventHandler? ItemPressed;

    public void SetSelectionMark(bool visible) =>
        SelectionMark.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void RefreshFavorite() => UpdateFavoriteIcon();

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        UpdateFavoriteIcon();
        FavoriteButton.Visibility = Item is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
        => ItemPressed?.Invoke(this, e);

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        => FavoriteButton.Visibility = Visibility.Collapsed;

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (Item is null)
        {
            return;
        }

        MediaFavorites.Toggle(Item.FilePath);
        UpdateFavoriteIcon();
        FavoriteButton.Visibility = Visibility.Visible;
    }

    private void UpdateFavoriteIcon()
    {
        var favorite = Item is not null && MediaFavorites.Contains(Item.FilePath);
        FavoriteIcon.Glyph = favorite ? "\uEB52" : "\uEB51";
    }

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaCard card)
        {
            card.Bindings.Update();
            _ = card.RefreshThumbnailAsync();
        }
    }

    private async Task RefreshThumbnailAsync()
    {
        if (Item is null)
        {
            ThumbImage.Source = null;
            return;
        }

        DurationBadge.Visibility = Visibility.Collapsed;
        DurationText.Text = string.Empty;
        SelectionMark.Visibility = Visibility.Collapsed;
        FavoriteButton.Visibility = Visibility.Collapsed;
        UpdateFavoriteIcon();
        if (Item.IsMissing || !File.Exists(Item.FilePath))
        {
            ThumbImage.Source = null;
            return;
        }

        if (!Item.IsVideo)
        {
            var image = new BitmapImage
            {
                UriSource = new Uri(Item.FilePath, UriKind.Absolute),
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                DecodePixelWidth = 440
            };
            ThumbImage.Source = image;
            return;
        }

        var path = Item.FilePath;
        var thumbnailTask = VideoThumbnail.LoadAsync(path, 440);
        var durationTask = TryGetVideoDurationAsync(path);
        ThumbImage.Source = await thumbnailTask;
        if (!string.Equals(Item?.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var duration = await durationTask;
        if (duration is not TimeSpan time || time <= TimeSpan.Zero)
        {
            return;
        }

        DurationText.Text = time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
        DurationBadge.Visibility = Visibility.Visible;
    }

    private static async Task<TimeSpan?> TryGetVideoDurationAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var properties = await file.Properties.GetVideoPropertiesAsync();
            return properties.Duration;
        }
        catch
        {
            return null;
        }
    }

}
