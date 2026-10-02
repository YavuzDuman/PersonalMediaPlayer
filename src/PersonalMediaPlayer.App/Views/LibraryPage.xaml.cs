using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Controls;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.ViewModels;
using PersonalMediaPlayer.Core.Models;
using MediaCard = PersonalMediaPlayer.App.Controls.MediaCard;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class LibraryPage : Page
{
    private readonly DispatcherTimer _slideTimer;
    private readonly CollectionViewSource _duplicateSource = new() { IsSourceGrouped = true };
    private List<MediaItem> _slides = [];
    private int _slideIndex;
    private int _linkGeneration;
    private CancellationTokenSource? _linkCheck;
    private MediaItem? _selectionAnchor;
    private List<MediaItem>? _pendingSelection;
    private bool _applyingSelection;

    public LibraryPage()
    {
        ViewModel = new LibraryViewModel(App.MediaLibrary);
        InitializeComponent();
        MediaGrid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(MediaGrid_PointerReleased), true);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LibraryViewModel.ShowingDuplicates))
            {
                UseDuplicateGroups(ViewModel.ShowingDuplicates);
            }
        };
        _slideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _slideTimer.Tick += (_, _) => ShowSlide(_slideIndex + 1);
    }

    public LibraryViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _linkGeneration++;
        _linkCheck?.Cancel();
        if (Slideshow.Visibility == Visibility.Visible)
        {
            CloseSlideshow();
        }
    }

    private async void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        var address = new TextBox { PlaceholderText = "https://…", MinWidth = 420 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "Paste a video address. It plays here and is not saved.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(address);
        var dialog = new ContentDialog
        {
            Title = "Open link",
            Content = panel,
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!StreamLink.TryNormalize(address.Text, out var url))
        {
            ViewModel.ShowStatus(StreamLink.EnterAddressMessage, InfoBarSeverity.Error);
            return;
        }

        var generation = ++_linkGeneration;
        _linkCheck?.Cancel();
        _linkCheck = new CancellationTokenSource();
        var token = _linkCheck.Token;
        ViewModel.ShowStatus("Checking the link…", InfoBarSeverity.Informational);
        try
        {
            var result = await StreamLink.CheckAsync(url, token);
            if (generation != _linkGeneration || token.IsCancellationRequested)
            {
                return;
            }

            if (result.Status != StreamCheckStatus.Media)
            {
                ViewModel.ShowStatus(result.Message, InfoBarSeverity.Error);
                return;
            }

            Frame.Navigate(typeof(VideoPlayerPage), new StreamOpenRequest(result.Url, StreamLink.DisplayName(result.Url)));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (generation == _linkGeneration)
            {
                ViewModel.ShowStatus(StreamLink.OpenFailedMessage, InfoBarSeverity.Error);
            }
        }
    }

    private void UseDuplicateGroups(bool grouped)
    {
        if (grouped)
        {
            if (!ReferenceEquals(_duplicateSource.Source, ViewModel.DuplicateGroups))
            {
                _duplicateSource.Source = ViewModel.DuplicateGroups;
            }

            MediaGrid.ItemsSource = _duplicateSource.View;
            return;
        }

        if (!ReferenceEquals(MediaGrid.ItemsSource, ViewModel.Items))
        {
            MediaGrid.ItemsSource = ViewModel.Items;
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason is AutoSuggestionBoxTextChangeReason.UserInput
            or AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
        {
            ViewModel.SearchText = sender.Text;
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        => ViewModel.SearchText = args.QueryText;

    private void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.A || !IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.Menu))
        {
            return;
        }

        if (Slideshow.Visibility == Visibility.Visible || IsTextInput(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (XamlRoot is not null
            && FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused
            && IsTextInput(focused))
        {
            return;
        }

        var visible = ViewModel.Items.ToList();
        _pendingSelection = null;
        ApplySelection(visible);
        if (visible.Count > 0)
        {
            _selectionAnchor = visible[0];
        }

        e.Handled = true;
    }

    private void MediaGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSelection)
        {
            return;
        }

        SyncSelectionChrome();
    }

    private void MediaGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not GridViewItem container || CardIn(container) is not MediaCard card)
        {
            return;
        }

        card.ItemPressed -= Card_ItemPressed;
        if (args.InRecycleQueue)
        {
            card.SetSelectionMark(false);
            return;
        }

        card.ItemPressed += Card_ItemPressed;
        var multiple = sender.SelectedItems.Count > 1;
        card.SetSelectionMark(multiple && args.Item is MediaItem item && sender.SelectedItems.Contains(item));
    }

    private void Card_ItemPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not MediaCard card || card.Item is not MediaItem item)
        {
            return;
        }

        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!IsKeyDown(VirtualKey.Shift))
        {
            _selectionAnchor = item;
            _pendingSelection = null;
            return;
        }

        var selection = SelectionFromAnchor(item, IsKeyDown(VirtualKey.Control));
        if (selection.Count == 0)
        {
            return;
        }

        e.Handled = true;
        _pendingSelection = selection;
        ApplySelection(selection);
    }

    private void MediaGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pendingSelection is null)
        {
            return;
        }

        var pending = _pendingSelection;
        _pendingSelection = null;
        e.Handled = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsLoaded)
            {
                ApplySelection(pending);
            }
        });
    }

    private void ApplySelection(IReadOnlyList<MediaItem> items)
    {
        _applyingSelection = true;
        try
        {
            MediaGrid.SelectedItems.Clear();
            foreach (var item in items)
            {
                MediaGrid.SelectedItems.Add(item);
            }
        }
        finally
        {
            _applyingSelection = false;
        }

        SyncSelectionChrome();
    }

    private void SyncSelectionChrome()
    {
        ViewModel.SetSelection(SelectedItems());
        UpdateSelectionMarks();
        MediaGrid.Padding = new Thickness(0, 0, 0, ViewModel.HasSelection ? 72 : 0);
    }

    private List<MediaItem> SelectionFromAnchor(MediaItem item, bool extend)
    {
        var visible = ViewModel.Items.ToList();
        var end = IndexOfItem(visible, item);
        if (end < 0)
        {
            return extend ? SelectedItems().ToList() : [];
        }

        var start = IndexOfItem(visible, _selectionAnchor);
        if (start < 0)
        {
            start = 0;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        var range = visible.GetRange(start, end - start + 1);
        if (!extend)
        {
            return range;
        }

        var combined = SelectedItems().ToList();
        foreach (var entry in range)
        {
            if (!combined.Contains(entry))
            {
                combined.Add(entry);
            }
        }

        return combined;
    }

    private static int IndexOfItem(IReadOnlyList<MediaItem> items, MediaItem? target)
    {
        if (target is null)
        {
            return -1;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], target))
            {
                return index;
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (string.Equals(items[index].FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsKeyDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static bool IsTextInput(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox or AutoSuggestBox or RichEditBox or PasswordBox or NumberBox or ComboBox)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateSelectionMarks()
    {
        var multiple = MediaGrid.SelectedItems.Count > 1;
        foreach (var entry in MediaGrid.Items)
        {
            if (MediaGrid.ContainerFromItem(entry) is not GridViewItem container || CardIn(container) is not MediaCard card)
            {
                continue;
            }

            card.SetSelectionMark(multiple && MediaGrid.SelectedItems.Contains(entry));
        }
    }

    private static MediaCard? CardIn(GridViewItem container)
    {
        if (container.ContentTemplateRoot is MediaCard direct)
        {
            return direct;
        }

        var count = VisualTreeHelper.GetChildrenCount(container);
        for (var index = 0; index < count; index++)
        {
            var found = FindCard(VisualTreeHelper.GetChild(container, index));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static MediaCard? FindCard(DependencyObject node)
    {
        if (node is MediaCard card)
        {
            return card;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < count; index++)
        {
            var found = FindCard(VisualTreeHelper.GetChild(node, index));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void MediaGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsKeyDown(VirtualKey.Shift) || IsKeyDown(VirtualKey.Control))
        {
            return;
        }

        OpenSelected();
    }

    private void MediaGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = ItemFromSource(e.OriginalSource);
        if (item is null)
        {
            return;
        }

        _selectionAnchor = item;
        if (!MediaGrid.SelectedItems.Contains(item))
        {
            MediaGrid.SelectedItems.Clear();
            MediaGrid.SelectedItems.Add(item);
        }

        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        var flyout = BuildItemFlyout(selected);
        if (e.OriginalSource is FrameworkElement target)
        {
            flyout.ShowAt(target, new FlyoutShowOptions { Position = e.GetPosition(target) });
        }
    }

    private MenuFlyout BuildItemFlyout(IReadOnlyList<MediaItem> selected)
    {
        var flyout = new MenuFlyout();
        if (ViewModel.SelectedFolder?.IsRecentlyDeleted == true)
        {
            var restore = new MenuFlyoutItem { Text = selected.Count == 1 ? "Restore" : $"Restore {selected.Count} items" };
            restore.Click += async (_, _) =>
            {
                await ViewModel.RestoreDeletedAsync(selected);
                MediaGrid.SelectedItems.Clear();
            };
            flyout.Items.Add(restore);
            var purge = new MenuFlyoutItem { Text = selected.Count == 1 ? "Delete permanently" : $"Delete {selected.Count} permanently" };
            purge.Click += Delete_Click;
            flyout.Items.Add(purge);
            return flyout;
        }

        var one = selected.Count == 1 ? selected[0] : null;

        if (one is not null)
        {
            var open = new MenuFlyoutItem { Text = "Open" };
            open.Click += (_, _) => OpenSelected();
            flyout.Items.Add(open);

            if (one.IsMissing)
            {
                var locate = new MenuFlyoutItem { Text = "Locate" };
                locate.Click += async (_, _) => await LocateSelectionAsync(one);
                flyout.Items.Add(locate);
            }
            else
            {
                if (!one.IsVideo)
                {
                    var copy = new MenuFlyoutItem { Text = "Copy" };
                    copy.Click += async (_, _) => await CopySelectionAsync();
                    flyout.Items.Add(copy);
                }

                var rename = new MenuFlyoutItem { Text = "Rename" };
                rename.Click += async (_, _) => await RenameItemAsync(one);
                flyout.Items.Add(rename);
            }
        }

        var add = new MenuFlyoutSubItem { Text = selected.Count == 1 ? "Add to" : $"Add {selected.Count} to" };
        var create = new MenuFlyoutItem { Text = "New folder…" };
        create.Click += async (_, _) => await AddSelectionToNewFolderAsync();
        add.Items.Add(create);
        add.Items.Add(new MenuFlyoutSeparator());
        var root = new MenuFlyoutItem { Text = "Remove from folders" };
        root.Click += async (_, _) => await MoveSelectionAsync(null);
        add.Items.Add(root);
        foreach (var folder in UserFolders(excludeCurrent: false))
        {
            var name = folder.Name;
            var entry = new MenuFlyoutItem { Text = name };
            entry.Click += async (_, _) => await MoveSelectionAsync(name);
            add.Items.Add(entry);
        }

        flyout.Items.Add(add);

        var relocate = new MenuFlyoutSubItem { Text = selected.Count == 1 ? "Move to" : $"Move {selected.Count} to" };
        var createMove = new MenuFlyoutItem { Text = "New folder…" };
        createMove.Click += async (_, _) => await MoveSelectionToNewFolderAsync();
        relocate.Items.Add(createMove);
        var destinations = UserFolders(excludeCurrent: true).ToArray();
        if (destinations.Length > 0)
        {
            relocate.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (var folder in destinations)
        {
            var name = folder.Name;
            var entry = new MenuFlyoutItem { Text = name };
            entry.Click += async (_, _) => await MoveSelectionToAsync(name);
            relocate.Items.Add(entry);
        }

        flyout.Items.Add(relocate);
        AddOrganizeFlyout(flyout, selected);
        flyout.Items.Add(new MenuFlyoutSeparator());

        var inAllMedia = IsPermanentDeleteView();
        var favorites = ViewModel.SelectedFolder?.IsFavorites == true;
        var linkedOnly = inAllMedia && selected.All(item => item.IsLinked);
        var delete = new MenuFlyoutItem
        {
            Text = favorites
                ? (selected.Count == 1 ? "Remove from Favorites" : $"Remove {selected.Count} from Favorites")
                : linkedOnly
                    ? (selected.Count == 1 ? "Remove from library" : $"Remove {selected.Count} from library")
                    : selected.Count == 1
                        ? (inAllMedia ? "Delete" : "Remove from folder")
                        : (inAllMedia ? $"Delete {selected.Count} items" : $"Remove {selected.Count} items")
        };
        delete.Click += Delete_Click;
        flyout.Items.Add(delete);
        return flyout;
    }

    private static MediaItem? ItemFromSource(object source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (current is MediaCard card && card.Item is not null)
            {
                return card.Item;
            }

            if (current is GridViewItem container)
            {
                if (container.Content is MediaItem media)
                {
                    return media;
                }

                if (container.Content is MediaCard nested && nested.Item is not null)
                {
                    return nested.Item;
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void FolderList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var folder = FolderFromSource(e.OriginalSource);
        if (folder is null || folder.IsAll || folder.IsSystem)
        {
            return;
        }

        FolderList.SelectedItem = folder;
        var flyout = new MenuFlyout();

        var rename = new MenuFlyoutItem { Text = "Rename" };
        rename.Click += async (_, _) => await RenameFolderAsync(folder);
        flyout.Items.Add(rename);

        var delete = new MenuFlyoutItem { Text = "Delete folder" };
        delete.Click += async (_, _) => await DeleteFolderAsync(folder);
        flyout.Items.Add(delete);

        if (e.OriginalSource is FrameworkElement target)
        {
            flyout.ShowAt(target, new FlyoutShowOptions { Position = e.GetPosition(target) });
        }
    }

    private static LibraryFolder? FolderFromSource(object source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ListViewItem container && container.Content is LibraryFolder folder)
            {
                return folder;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private async Task RenameItemAsync(MediaItem item)
    {
        var box = new TextBox { Text = Path.GetFileNameWithoutExtension(item.DisplayName) };
        var dialog = new ContentDialog
        {
            Title = "Rename",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            await ViewModel.RenameItemAsync(item.FilePath, box.Text);
        }
    }

    private async Task RenameFolderAsync(LibraryFolder folder)
    {
        var box = new TextBox { Text = folder.Name };
        var dialog = new ContentDialog
        {
            Title = "Rename folder",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            await ViewModel.RenameFolderAsync(folder.Name, box.Text);
        }
    }

    private async Task DeleteFolderAsync(LibraryFolder folder)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete folder?",
            Content = folder.FileCount == 0
                ? $"Delete empty folder '{folder.Name}'?"
                : $"Delete folder '{folder.Name}'? The {folder.FileCount} item{(folder.FileCount == 1 ? "" : "s")} stay in All media.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteFolderAsync(folder.Name);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void Slideshow_Click(object sender, RoutedEventArgs e)
    {
        _slides = ViewModel.Items.Where(item => !item.IsVideo && File.Exists(item.FilePath)).ToList();
        if (_slides.Count == 0)
        {
            ViewModel.ShowStatus("This view has no photos to play.", InfoBarSeverity.Informational);
            return;
        }

        var selected = SelectedItems().LastOrDefault(item => !item.IsVideo);
        _slideIndex = selected is null ? 0 : Math.Max(0, _slides.FindIndex(item => item.Id == selected.Id));
        Slideshow.Visibility = Visibility.Visible;
        ShowSlide(_slideIndex);
        _slideTimer.Start();
        if (App.MainAppWindow is MainWindow window)
        {
            window.SetFullScreen(true);
        }

        Slideshow.Focus(FocusState.Programmatic);
    }

    private void SlideshowNext_Click(object sender, RoutedEventArgs e) => ShowSlide(_slideIndex + 1);

    private void SlideshowPrev_Click(object sender, RoutedEventArgs e) => ShowSlide(_slideIndex - 1);

    private void SlideshowClose_Click(object sender, RoutedEventArgs e) => CloseSlideshow();

    private void Slideshow_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Right)
        {
            ShowSlide(_slideIndex + 1);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Left)
        {
            ShowSlide(_slideIndex - 1);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseSlideshow();
            e.Handled = true;
        }
    }

    private void ShowSlide(int index)
    {
        if (_slides.Count == 0)
        {
            CloseSlideshow();
            return;
        }

        _slideIndex = (index % _slides.Count + _slides.Count) % _slides.Count;
        var item = _slides[_slideIndex];
        SlideshowImage.Source = new BitmapImage(new Uri(item.FilePath));
        SlideshowCaption.Text = $"{item.DisplayName}   {_slideIndex + 1} of {_slides.Count}   Left and Right to move, Esc to close";
        _slideTimer.Stop();
        _slideTimer.Start();
    }

    private void CloseSlideshow()
    {
        _slideTimer.Stop();
        SlideshowImage.Source = null;
        Slideshow.Visibility = Visibility.Collapsed;
        if (App.MainAppWindow is MainWindow window)
        {
            window.SetFullScreen(false);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        var inAllMedia = IsPermanentDeleteView();
        var recentlyDeleted = ViewModel.SelectedFolder?.IsRecentlyDeleted == true;
        var favorites = ViewModel.SelectedFolder?.IsFavorites == true;
        var linked = selected.Count(item => item.IsLinked);
        string title;
        string content;
        string confirm;
        if (recentlyDeleted)
        {
            title = "Delete forever?";
            content = selected.Count == 1
                ? $"Delete {selected[0].DisplayName} forever? This cannot be undone."
                : $"Delete {selected.Count} items forever? This cannot be undone.";
            confirm = "Delete forever";
        }
        else if (favorites)
        {
            title = "Remove from Favorites?";
            content = selected.Count == 1
                ? $"Remove {selected[0].DisplayName} from Favorites? The file stays in the library."
                : $"Remove {selected.Count} items from Favorites? The files stay in the library.";
            confirm = "Remove";
        }
        else if (!inAllMedia)
        {
            title = "Remove from folder?";
            content = selected.Count == 1
                ? $"Remove {selected[0].DisplayName} from this folder? It stays in All media."
                : $"Remove {selected.Count} items from this folder? They stay in All media.";
            confirm = "Remove";
        }
        else if (linked == selected.Count)
        {
            title = "Remove from library?";
            content = selected.Count == 1
                ? $"Remove {selected[0].DisplayName} from the library? The original file stays where it is."
                : $"Remove {selected.Count} items from the library? The original files stay where they are.";
            confirm = "Remove";
        }
        else if (linked > 0)
        {
            title = "Delete from library?";
            content = "Copies go to Recently deleted for 7 days. Linked files stay on disk and are only removed from the library.";
            confirm = "Delete";
        }
        else
        {
            title = "Delete from library?";
            content = selected.Count == 1
                ? $"Delete {selected[0].DisplayName}? You can restore it from Recently deleted for 7 days."
                : $"Delete {selected.Count} items? You can restore them from Recently deleted for 7 days.";
            confirm = "Delete";
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = confirm,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteAsync(selected);
            MediaGrid.SelectedItems.Clear();
        }
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { PlaceholderText = "Folder name" };
        var dialog = new ContentDialog
        {
            Title = "New folder",
            Content = box,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            await ViewModel.CreateFolderAsync(box.Text);
        }
    }

    private async void AddTo_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        var names = UserFolders(excludeCurrent: false).Select(folder => folder.Name).ToList();
        if (names.Count == 0)
        {
            await ShowNoticeAsync("Add to folder", "Create a folder first. Add keeps the item in the folder you are viewing.");
            return;
        }

        var combo = new ComboBox
        {
            ItemsSource = names,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var dialog = new ContentDialog
        {
            Title = "Add to folder",
            Content = combo,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (combo.SelectedItem is string target)
        {
            await MoveSelectionAsync(target);
        }
    }

    private async void MoveTo_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        var names = UserFolders(excludeCurrent: true).Select(folder => folder.Name).ToList();
        if (names.Count == 0)
        {
            await ShowNoticeAsync("Move to folder", "Create another folder first. Move takes items out of the folder you are viewing.");
            return;
        }

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = MoveHelpText(selected, "the folder you choose"), TextWrapping = TextWrapping.Wrap });
        var combo = new ComboBox
        {
            ItemsSource = names,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(combo);
        var dialog = new ContentDialog
        {
            Title = "Move to folder",
            Content = panel,
            PrimaryButtonText = "Move",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || combo.SelectedItem is not string target)
        {
            return;
        }

        await ViewModel.MoveToAsync(selected, target);
        MediaGrid.SelectedItems.Clear();
    }

    private async Task AddSelectionToNewFolderAsync()
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        var box = new TextBox { PlaceholderText = "Folder name" };
        var dialog = new ContentDialog
        {
            Title = selected.Count == 1 ? "Add to a new folder" : $"Add {selected.Count} items to a new folder",
            Content = box,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        await ViewModel.AddSelectionToNewFolderAsync(box.Text, selected);
        MediaGrid.SelectedItems.Clear();
    }

    private async Task MoveSelectionToNewFolderAsync()
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        var box = new TextBox { PlaceholderText = "Folder name" };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = MoveHelpText(selected, "the new folder"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        var dialog = new ContentDialog
        {
            Title = selected.Count == 1 ? "Move to a new folder" : $"Move {selected.Count} items to a new folder",
            Content = panel,
            PrimaryButtonText = "Move",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        await ViewModel.MoveSelectionToNewFolderAsync(box.Text, selected);
        MediaGrid.SelectedItems.Clear();
    }

    private async Task MoveSelectionToAsync(string folderName)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        if (ViewModel.MoveClearsOtherFolders(selected))
        {
            var dialog = new ContentDialog
            {
                Title = $"Move to {folderName}?",
                Content = new TextBlock { Text = MoveHelpText(selected, $"'{folderName}'"), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "Move",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await ViewModel.MoveToAsync(selected, folderName);
        MediaGrid.SelectedItems.Clear();
    }

    private string MoveHelpText(IReadOnlyList<MediaItem> selected, string target)
    {
        var one = selected.Count == 1;
        var file = one ? "The file stays" : "Files stay";
        if (ViewModel.LeavesCurrentFolder)
        {
            var subject = one ? "It leaves" : "They leave";
            var join = one ? "joins" : "join";
            return $"{subject} '{ViewModel.CurrentFolderName}' and {join} {target}. Other folders stay. {file} in All media.";
        }

        if (ViewModel.MoveClearsOtherFolders(selected))
        {
            var subject = one ? "It leaves" : "They leave";
            var join = one ? "joins" : "join";
            return $"{subject} every other folder and {join} {target}. Recordings and Screenshots stay. {file} in All media.";
        }

        return one
            ? $"It joins {target} and stays in All media."
            : $"They join {target} and stay in All media.";
    }

    private IEnumerable<LibraryFolder> UserFolders(bool excludeCurrent)
    {
        var current = ViewModel.CurrentFolderName;
        foreach (var folder in ViewModel.Folders)
        {
            if (folder.IsAll || folder.IsSystem)
            {
                continue;
            }

            if (excludeCurrent && folder.Name.Equals(current, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return folder;
        }
    }

    private async Task ShowNoticeAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private bool IsPermanentDeleteView()
        => string.IsNullOrEmpty(ViewModel.CurrentFolderName)
           || string.Equals(ViewModel.CurrentFolderName, LibraryFolder.Screenshots, StringComparison.OrdinalIgnoreCase)
           || string.Equals(ViewModel.CurrentFolderName, LibraryFolder.Recordings, StringComparison.OrdinalIgnoreCase);

    private async Task MoveSelectionAsync(string? folderName)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        await ViewModel.MoveAsync(selected, folderName);
        MediaGrid.SelectedItems.Clear();
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var files = items.OfType<StorageFile>().ToArray();
        if (files.Length > 0)
        {
            await ViewModel.ImportStorageFilesAsync(files);
        }
    }

    private void MediaGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<MediaItem>().Select(item => item.FilePath).ToArray();
        e.Data.SetText(string.Join('\n', paths));
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void FolderList_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var folder = FolderAt(e.GetPosition(FolderList));
            if (folder?.IsSystem == true)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private async void FolderList_Drop(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(FolderList);
        var folder = FolderAt(point);
        if (folder?.IsSystem == true)
        {
            return;
        }

        var target = folder is null || folder.IsAll ? null : folder.Name;

        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            var text = await e.DataView.GetTextAsync();
            var paths = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var items = ViewModel.Items.Where(item => paths.Contains(item.FilePath)).ToArray();
            if (items.Length > 0)
            {
                await ViewModel.MoveAsync(items, target);
            }
        }
        else if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var files = items.OfType<StorageFile>().ToArray();
            if (files.Length > 0)
            {
                if (target is not null)
                {
                    ViewModel.SelectedFolder = ViewModel.Folders.FirstOrDefault(f => f.Name == target);
                }

                await ViewModel.ImportStorageFilesAsync(files);
            }
        }
    }

    private LibraryFolder? FolderAt(Windows.Foundation.Point point)
    {
        foreach (var folder in ViewModel.Folders)
        {
            if (FolderList.ContainerFromItem(folder) is UIElement element)
            {
                var transform = element.TransformToVisual(FolderList);
                var bounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualSize.X, element.ActualSize.Y));
                if (bounds.Contains(point))
                {
                    return folder;
                }
            }
        }

        return FolderList.SelectedItem as LibraryFolder;
    }

    private async Task CopySelectionAsync()
    {
        var item = SelectedItems().LastOrDefault(media => !media.IsVideo) ?? ViewModel.DetailsItem;
        if (item is null || item.IsVideo)
        {
            ViewModel.ShowStatus("Select an image to copy.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            await ClipboardHelper.CopyImageFileAsync(item.FilePath);
            ViewModel.ShowStatus("Copied to clipboard.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task LocateSelectionAsync(MediaItem item)
    {
        await ViewModel.LocateAsync(item);
        MediaGrid.SelectedItems.Clear();
    }

    private async void Locate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.DetailsItem is not { IsMissing: true } item)
        {
            return;
        }

        await LocateSelectionAsync(item);
    }

    private void OpenSelected()
    {
        var item = SelectedItems().LastOrDefault() ?? ViewModel.DetailsItem;
        if (item is null)
        {
            return;
        }

        if (item.IsMissing)
        {
            ViewModel.ShowStatus("This file is missing. Locate it to keep saved words, bookmarks, and the playback position.", InfoBarSeverity.Warning);
            return;
        }

        if (item.IsVideo)
        {
            Frame.Navigate(typeof(VideoPlayerPage), item);
            return;
        }

        var term = ViewModel.SearchText.Trim();
        var highlight = !string.IsNullOrWhiteSpace(term) && ScreenshotTextIndex.Matches(item.FilePath, term)
            ? term
            : null;
        Frame.Navigate(typeof(MediaPreviewPage), highlight is null ? item : new PreviewRequest(item, highlight));
    }

    private IReadOnlyList<MediaItem> SelectedItems()
        => MediaGrid.SelectedItems.OfType<MediaItem>().ToArray();

    private void AddOrganizeFlyout(MenuFlyout flyout, IReadOnlyList<MediaItem> selected)
    {
        if (ViewModel.SelectedFolder?.IsRecentlyDeleted == true)
        {
            return;
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var playlist = new MenuFlyoutItem { Text = "Add to playlist" };
        playlist.Click += async (_, _) => await AddSelectionToPlaylistAsync();
        flyout.Items.Add(playlist);

        var addFavorites = new MenuFlyoutItem
        {
            Text = selected.Count == 1 ? "Add to favorites" : $"Add {selected.Count} to favorites"
        };
        addFavorites.Click += (_, _) => AddSelectionToFavorites();
        flyout.Items.Add(addFavorites);

        var removeFavorites = new MenuFlyoutItem
        {
            Text = selected.Count == 1 ? "Remove from favorites" : $"Remove {selected.Count} from favorites"
        };
        removeFavorites.Click += (_, _) => RemoveSelectionFromFavorites();
        flyout.Items.Add(removeFavorites);
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _pendingSelection = null;
        _selectionAnchor = null;
        MediaGrid.SelectedItems.Clear();
    }

    private async void AddToPlaylist_Click(object sender, RoutedEventArgs e)
        => await AddSelectionToPlaylistAsync();

    private void AddFavorites_Click(object sender, RoutedEventArgs e)
        => AddSelectionToFavorites();

    private void RemoveFavorites_Click(object sender, RoutedEventArgs e)
        => RemoveSelectionFromFavorites();

    private void AddSelectionToFavorites()
    {
        if (ViewModel.SelectedFolder?.IsRecentlyDeleted == true)
        {
            return;
        }

        var paths = SelectedItems().Select(item => item.FilePath).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        var added = MediaFavorites.AddMany(paths);
        RefreshFavoriteIcons();
        ViewModel.ShowStatus(
            added == 0
                ? "Those files are already favorites."
                : added == 1 ? "Added 1 file to favorites." : $"Added {added} files to favorites.",
            added == 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Success);
    }

    private void RemoveSelectionFromFavorites()
    {
        if (ViewModel.SelectedFolder?.IsRecentlyDeleted == true)
        {
            return;
        }

        var paths = SelectedItems().Select(item => item.FilePath).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        var removed = MediaFavorites.RemoveMany(paths);
        RefreshFavoriteIcons();
        ViewModel.ShowStatus(
            removed == 0
                ? "Those files are not favorites."
                : removed == 1 ? "Removed 1 file from favorites." : $"Removed {removed} files from favorites.",
            removed == 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Success);
    }

    private void RefreshFavoriteIcons()
    {
        foreach (var entry in ViewModel.Items)
        {
            if (MediaGrid.ContainerFromItem(entry) is GridViewItem container && CardIn(container) is MediaCard card)
            {
                card.RefreshFavorite();
            }
        }
    }

    private async Task AddSelectionToPlaylistAsync()
    {
        if (ViewModel.SelectedFolder?.IsRecentlyDeleted == true)
        {
            return;
        }

        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            return;
        }

        var candidates = selected
            .Where(item => item.IsVideo && !item.IsMissing && !string.IsNullOrWhiteSpace(item.FilePath))
            .Select(item => item.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var videos = candidates.Length == 0
            ? []
            : await Task.Run(() => candidates.Where(File.Exists).ToArray());
        if (videos.Length == 0)
        {
            ViewModel.ShowStatus("Choose a video that is on this PC. Photos are not added to a playlist.", InfoBarSeverity.Warning);
            return;
        }

        var choices = Playlists.All().Select(list => new PlaylistPick(list.Name, list.Id, false)).ToList();
        choices.Add(new PlaylistPick("New playlist…", null, true));
        var combo = new ComboBox
        {
            DisplayMemberPath = nameof(PlaylistPick.Name),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = choices,
            MinWidth = 320
        };
        var nameBox = new TextBox { PlaceholderText = "Playlist name" };
        void UpdateNameBox()
        {
            var create = combo.SelectedItem is PlaylistPick { CreateNew: true };
            nameBox.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        }

        combo.SelectionChanged += (_, _) => UpdateNameBox();
        combo.SelectedIndex = 0;
        UpdateNameBox();

        var leftOut = selected.Any(item => !videos.Contains(item.FilePath, StringComparer.OrdinalIgnoreCase));
        var note = videos.Length == 1 ? "1 video selected." : $"{videos.Length} videos selected.";
        if (leftOut)
        {
            note += " Photos and missing files stay out.";
        }

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Playlist" });
        panel.Children.Add(combo);
        panel.Children.Add(nameBox);
        var dialog = new ContentDialog
        {
            Title = "Add to playlist",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            if (combo.SelectedItem is not PlaylistPick choice)
            {
                return;
            }

            var id = choice.Id;
            if (choice.CreateNew)
            {
                var created = Playlists.Create(nameBox.Text);
                if (created is null)
                {
                    ViewModel.ShowStatus("Type a playlist name that is not already used.", InfoBarSeverity.Warning);
                    return;
                }

                id = created.Id;
            }

            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var added = Playlists.Add(id, videos);
            var message = added == 0
                ? "Those videos are already in this playlist."
                : added == 1 ? "Added 1 video." : $"Added {added} videos.";
            if (leftOut)
            {
                message += " Photos and missing files were left out.";
            }

            ViewModel.ShowStatus(message, added == 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Success);
        }
        catch (IOException)
        {
            ViewModel.ShowStatus("Could not add those videos.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ViewModel.ShowStatus("Could not add those videos.", InfoBarSeverity.Error);
        }
    }

    private sealed record PlaylistPick(string Name, string? Id, bool CreateNew);
}
