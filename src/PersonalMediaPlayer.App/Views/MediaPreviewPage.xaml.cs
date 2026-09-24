using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.ViewModels;
using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class MediaPreviewPage : Page
{
    private bool _allowLeave;
    private bool _saving;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private bool _pendingIsBack;

    public MediaPreviewPage()
    {
        ViewModel = new MediaPreviewViewModel();
        InitializeComponent();
        Markup.MarksChanged += (_, _) => ViewModel.HasMarkup = Markup.HasEdits;
    }

    public MediaPreviewViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _allowLeave = false;
        string? highlight = null;
        MediaItem? item = e.Parameter switch
        {
            PreviewRequest request => App.MediaLibrary.GetById(request.Item.Id) ?? request.Item,
            MediaItem media => App.MediaLibrary.GetById(media.Id) ?? media,
            string id => App.MediaLibrary.GetById(id),
            _ => null
        };
        if (e.Parameter is PreviewRequest preview)
        {
            highlight = preview.HighlightTerm;
        }

        await LoadItemAsync(item);
        if (item is not null && !string.IsNullOrWhiteSpace(highlight))
        {
            await HighlightSearchAsync(item, highlight);
        }
    }

    private async Task HighlightSearchAsync(MediaItem item, string term)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(item.FilePath);
            var boxes = await ScreenshotOcr.FindWordsAsync(bytes, term);
            Markup.ShowSearchHighlights(boxes);
        }
        catch
        {
            Markup.ShowSearchHighlights([]);
        }
    }

    internal bool PrepareToLeave(Type? pageType, object? parameter, bool back)
    {
        _pendingPageType = pageType;
        _pendingParameter = parameter;
        _pendingIsBack = back;
        if (!Markup.HasEdits)
        {
            return true;
        }

        ShowLeavePrompt();
        return false;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (_allowLeave || !Markup.HasEdits)
        {
            return;
        }

        e.Cancel = true;
        _pendingPageType = e.SourcePageType;
        _pendingParameter = e.Parameter;
        _pendingIsBack = e.NavigationMode == NavigationMode.Back;
        if (LeavePrompt.Visibility != Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(ShowLeavePrompt);
        }
    }

    private void ShowLeavePrompt() => LeavePrompt.Visibility = Visibility.Visible;

    private void LeavePromptStay_Click(object sender, RoutedEventArgs e)
        => LeavePrompt.Visibility = Visibility.Collapsed;

    private void LeavePromptConfirm_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
        _allowLeave = true;
        ContinueNavigation();
    }

    private void ContinueNavigation()
    {
        var moved = false;
        if (_pendingIsBack && Frame.CanGoBack)
        {
            Frame.GoBack();
            moved = true;
        }
        else if (_pendingPageType is not null && Frame.Navigate(_pendingPageType, _pendingParameter))
        {
            Frame.BackStack.Clear();
            moved = true;
        }

        if (moved && App.MainAppWindow is MainWindow window)
        {
            window.SyncNavigationSelection();
        }
    }

    private async Task LoadItemAsync(MediaItem? item)
    {
        await ViewModel.SetItemAsync(item);
        Markup.CanEdit = item is not null;
        var hasOriginal = item is not null && App.MediaLibrary.HasOriginal(item.FilePath);
        RestoreButton.Visibility = hasOriginal ? Visibility.Visible : Visibility.Collapsed;
        CompareButton.Visibility = hasOriginal ? Visibility.Visible : Visibility.Collapsed;
        CompareLabel.Text = "Compare";
        if (ViewModel.PreviewImage is { } image && item is not null)
        {
            var source = await File.ReadAllBytesAsync(item.FilePath);
            Markup.Load(image, Math.Max(1, image.PixelWidth), Math.Max(1, image.PixelHeight), source);
        }
        else
        {
            Markup.Clear();
        }
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item is null)
        {
            return;
        }

        try
        {
            var source = await File.ReadAllBytesAsync(ViewModel.Item.FilePath);
            await ClipboardHelper.CopyPngAsync(Markup.Flatten(source));
            ViewModel.ShowStatus("Copied to clipboard.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (Markup.IsComparing)
        {
            Markup.EndCompare();
            CompareLabel.Text = "Compare";
            return;
        }

        if (ViewModel.Item is not { } item)
        {
            return;
        }

        try
        {
            var path = App.MediaLibrary.TryGetOriginalPath(item.FilePath);
            if (path is null)
            {
                ViewModel.ShowStatus("This photo has no saved original.", InfoBarSeverity.Informational);
                return;
            }

            var before = await ImageLoader.LoadAsync(path);
            Markup.BeginCompare(before, Math.Max(1, before.PixelWidth), Math.Max(1, before.PixelHeight));
            CompareLabel.Text = "Close compare";
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item is not { } item)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Restore original?",
            Content = "This replaces the current photo with the untouched copy. Marks that are not saved are discarded.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            App.MediaLibrary.RestoreOriginal(item.FilePath);
            await LoadItemAsync(App.MediaLibrary.GetById(item.Id) ?? item);
            ViewModel.ShowStatus("Original restored.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item is { } item)
        {
            Frame.Navigate(typeof(PhotoEditorPage), item);
        }
    }

    private async void SaveMarkup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item is null || !Markup.HasEdits || _saving)
        {
            return;
        }

        _saving = true;

        var dialog = new ContentDialog
        {
            Title = "Save marked photo",
            Content = "Save as a new image, or overwrite this one? The original file is kept if you overwrite.",
            PrimaryButtonText = "Save as new",
            SecondaryButtonText = "Overwrite",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        try
        {
            var result = await dialog.ShowAsync();
            if (result is ContentDialogResult.None)
            {
                return;
            }

            var previousPath = ViewModel.Item.FilePath;
            var original = await File.ReadAllBytesAsync(previousPath);
            var flattened = Markup.Flatten(original);
            await using var stream = new MemoryStream(flattened);
            var stem = Path.GetFileNameWithoutExtension(ViewModel.Item.DisplayName);
            var overwrite = result != ContentDialogResult.Primary;
            MediaItem saved;
            if (!overwrite)
            {
                saved = App.MediaLibrary.SaveEditedAsNew(stream, $"{stem} marked.png");
            }
            else
            {
                saved = App.MediaLibrary.OverwriteEdited(previousPath, stream, $"{stem}.png");
            }

            await ScreenshotTextIndex.StoreAsync(saved.FilePath, flattened);
            if (overwrite && !string.Equals(previousPath, saved.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                ScreenshotTextIndex.Remove(previousPath);
            }

            await LoadItemAsync(saved);
            ViewModel.ShowStatus($"Saved marks: {saved.DisplayName}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _saving = false;
        }
    }
}
