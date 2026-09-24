using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.ViewModels;

public sealed partial class MediaPreviewViewModel : ObservableObject
{
    [ObservableProperty]
    private MediaItem? item;

    [ObservableProperty]
    private BitmapImage? previewImage;

    [ObservableProperty]
    private bool hasMarkup;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity statusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool hasStatus;

    public string Title => Item?.DisplayName ?? "Preview";

    public string ImportedOn => Item is null
        ? string.Empty
        : $"Added {Item.ImportedAt.ToLocalTime():g}";

    public async Task SetItemAsync(MediaItem? mediaItem)
    {
        Item = mediaItem;
        PreviewImage = mediaItem is null ? null : await ImageLoader.LoadAsync(mediaItem.FilePath);
        HasMarkup = false;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ImportedOn));
    }

    public void ShowStatus(string message, InfoBarSeverity severity)
    {
        HasStatus = false;
        StatusMessage = message;
        StatusSeverity = severity;
        HasStatus = !string.IsNullOrWhiteSpace(message);
    }
}
