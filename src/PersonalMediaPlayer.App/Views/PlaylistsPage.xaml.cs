using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Controls;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core;
using PersonalMediaPlayer.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class PlaylistsPage : Page
{
    private readonly SolidColorBrush _clear = new(Microsoft.UI.Colors.Transparent);
    private string? _selectedId;
    private string? _searchForId;
    private string _videoSearch = string.Empty;
    private bool _settingSearch;
    private bool _unwatchedOnly;
    private bool _showingUndo;
    private int _closeSuppress;
    private int? _dragFrom;

    public PlaylistsPage()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyPageWidth(ActualWidth);
        StatusBar.Closed += StatusBar_Closed;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(ShowLists);
        PlaylistChanges.Changed += OnPlaylistChangesChanged;
        Playlists.QueueCopied += OnQueueCopied;
        Unloaded += (_, _) =>
        {
            PlaylistChanges.Changed -= OnPlaylistChangesChanged;
            Playlists.QueueCopied -= OnQueueCopied;
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var lists = Playlists.All();
        if (e.Parameter is string requested && lists.Any(list => string.Equals(list.Id, requested, StringComparison.Ordinal)))
        {
            _selectedId = requested;
        }
        else if (_selectedId is null || lists.All(list => list.Id != _selectedId))
        {
            var open = SearchSession.Recall(SearchSession.OpenPlaylist).Text;
            _selectedId = lists.Any(list => string.Equals(list.Id, open, StringComparison.Ordinal))
                ? open
                : lists.FirstOrDefault()?.Id;
        }

        ShowLists();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_selectedId is null)
        {
            return;
        }

        SearchSession.Remember(
            SearchSession.Playlist(_selectedId),
            _videoSearch,
            VideoScroller.VerticalOffset,
            VideoScroller.ScrollableHeight > 0);
        SearchSession.Remember(SearchSession.OpenPlaylist, _selectedId, 0);
    }

    private void NameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        CreatePlaylist();
    }

    private void ApplyPageWidth(double width)
    {
        if (PageRoot is null || PlaylistBody is null)
        {
            return;
        }

        PageRoot.Padding = width > 0 && width < 720
            ? new Thickness(16, 8, 16, 16)
            : new Thickness(24, 8, 24, 24);
        var wide = width <= 0 || width >= 900;
        if (wide)
        {
            ListColumn.Width = new GridLength(280);
            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            ListRow.Height = new GridLength(1, GridUnitType.Star);
            DetailRow.Height = new GridLength(0);
            PlaylistBody.ColumnSpacing = 16;
            PlaylistBody.RowSpacing = 0;
            PlacePane(ListCard, column: 0, columnSpan: 1, row: 0);
            PlacePane(DetailHost, column: 1, columnSpan: 1, row: 0);
            ListCard.ClearValue(FrameworkElement.MaxHeightProperty);
            return;
        }

        ListColumn.Width = new GridLength(1, GridUnitType.Star);
        DetailColumn.Width = new GridLength(0);
        ListRow.Height = GridLength.Auto;
        DetailRow.Height = new GridLength(1, GridUnitType.Star);
        PlaylistBody.ColumnSpacing = 0;
        PlaylistBody.RowSpacing = 16;
        PlacePane(ListCard, column: 0, columnSpan: 1, row: 0);
        PlacePane(DetailHost, column: 0, columnSpan: 1, row: 1);
        ListCard.MaxHeight = 220;
    }

    private static void PlacePane(FrameworkElement pane, int column, int columnSpan, int row)
    {
        Grid.SetColumn(pane, column);
        Grid.SetColumnSpan(pane, columnSpan);
        Grid.SetRow(pane, row);
        Grid.SetRowSpan(pane, 1);
    }

    private void Create_Click(object sender, RoutedEventArgs e) => CreatePlaylist();

    private void CreatePlaylist()
    {
        if (!TryReadName(NameBox.Text, null, out var name))
        {
            return;
        }

        try
        {
            var created = Playlists.Create(name);
            if (created is null)
            {
                ShowStatus("You already have a playlist with that name.", InfoBarSeverity.Informational);
                return;
            }

            NameBox.Text = string.Empty;
            _selectedId = created.Id;
            ShowStatus(null);
            ShowLists();
        }
        catch (IOException)
        {
            ShowStatus("Could not save the playlist.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not save the playlist.", InfoBarSeverity.Error);
        }
    }

    private async void AddVideos_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is null)
        {
            return;
        }

        var picked = await PickVideosAsync(_selectedId);
        if (picked.Count == 0)
        {
            return;
        }

        try
        {
            var added = Playlists.Add(_selectedId, picked);
            ShowStatus(added == 0
                ? "Those videos are already in this playlist."
                : added == 1 ? "Added 1 video." : $"Added {added} videos.",
                InfoBarSeverity.Success);
            ShowLists();
        }
        catch (IOException)
        {
            ShowStatus("Could not add those videos.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not add those videos.", InfoBarSeverity.Error);
        }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var list = Selected();
        if (list is null)
        {
            return;
        }

        var box = new TextBox { Text = list.Name };
        var dialog = new ContentDialog
        {
            Title = "Rename playlist",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!TryReadName(box.Text, list.Id, out var name))
        {
            return;
        }

        try
        {
            if (!Playlists.Rename(list.Id, name))
            {
                ShowStatus("You already have a playlist with that name.", InfoBarSeverity.Informational);
                return;
            }

            ShowStatus(null);
            ShowLists();
        }
        catch (IOException)
        {
            ShowStatus("Could not rename the playlist.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not rename the playlist.", InfoBarSeverity.Error);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var list = Selected();
        if (list is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete playlist?",
            Content = $"Delete \"{list.Name}\"? The videos stay in your library.",
            PrimaryButtonText = "Delete",
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
            Playlists.Delete(list.Id);
            _selectedId = null;
            ShowStatus(null);
            ShowLists();
        }
        catch (IOException)
        {
            ShowStatus("Could not delete the playlist.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not delete the playlist.", InfoBarSeverity.Error);
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        var list = Selected();
        if (list is null)
        {
            return;
        }

        var index = FirstVisiblePlayable(list);
        if (index < 0)
        {
            ShowStatus("None of these videos are on this PC.", InfoBarSeverity.Warning);
            return;
        }

        OpenVideo(list.Id, index);
    }

    private int FirstVisiblePlayable(Playlist list)
    {
        var names = LibraryNames();
        string? LibraryName(string path) => names.TryGetValue(path, out var known) ? known : null;
        for (var index = 0; index < list.Videos.Count; index++)
        {
            var entry = list.Videos[index];
            if (entry.Matches(_videoSearch, LibraryName) && entry.IsPlayable && ShowsEntry(entry))
            {
                return index;
            }
        }

        return -1;
    }

    private void ShowLists()
    {
        if (PlaylistRows is null)
        {
            return;
        }

        var lists = Playlists.All();
        if (_selectedId is null || lists.All(list => list.Id != _selectedId))
        {
            _selectedId = lists.FirstOrDefault()?.Id;
        }

        PlaylistRows.Children.Clear();
        var hasLists = lists.Count > 0;
        EmptyLists.Visibility = hasLists ? Visibility.Collapsed : Visibility.Visible;
        ListScroller.Visibility = hasLists ? Visibility.Visible : Visibility.Collapsed;
        FrameworkElement? selectedRow = null;
        foreach (var list in lists)
        {
            var row = CreatePlaylistRow(list);
            if (string.Equals(list.Id, _selectedId, StringComparison.Ordinal))
            {
                selectedRow = row;
            }

            PlaylistRows.Children.Add(row);
        }

        if (selectedRow is not null)
        {
            var row = selectedRow;
            DispatcherQueue.TryEnqueue(() => row.StartBringIntoView());
        }

        ShowVideos(Selected());
        UpdateUndoMessage();
    }

    private void Unwatched_Changed(object sender, RoutedEventArgs e)
    {
        if (VideoRows is null)
        {
            return;
        }

        _unwatchedOnly = UnwatchedBox.IsChecked == true;
        ShowVideos(Selected());
    }

    private void VideoSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingSearch)
        {
            return;
        }

        _videoSearch = VideoSearch.Text;
        ShowVideos(Selected());
    }

    private void SortByName_Click(object sender, RoutedEventArgs e)
        => SortSelected(byName: true);

    private void SortByDate_Click(object sender, RoutedEventArgs e)
        => SortSelected(byName: false);

    private void SortSelected(bool byName)
    {
        if (_selectedId is null)
        {
            return;
        }

        var list = Selected();
        if (list is null || list.Videos.Count < 2)
        {
            return;
        }

        try
        {
            if (byName)
            {
                var names = LibraryNames();
                PlaylistChanges.Apply(_selectedId, "Sorted by name.", () =>
                    Playlists.SortByName(_selectedId, path => names.TryGetValue(path, out var known) ? known : null));
            }
            else
            {
                PlaylistChanges.Apply(_selectedId, "Sorted by date added. Newest first.", () =>
                    Playlists.SortByAdded(_selectedId));
            }

            ShowLists();
        }
        catch (IOException)
        {
            ShowStatus("Could not sort this playlist.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not sort this playlist.", InfoBarSeverity.Error);
        }
    }

    private void ShowVideos(Playlist? selected)
    {
        var showDetail = selected is not null;
        EmptyDetail.Visibility = showDetail ? Visibility.Collapsed : Visibility.Visible;
        DetailCard.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        double? restoreOffset = null;
        if (!string.Equals(_searchForId, selected?.Id, StringComparison.Ordinal))
        {
            if (_searchForId is not null)
            {
                SearchSession.Remember(
                    SearchSession.Playlist(_searchForId),
                    _videoSearch,
                    VideoScroller.VerticalOffset,
                    VideoScroller.ScrollableHeight > 0);
            }

            _searchForId = selected?.Id;
            var saved = selected is null
                ? new SearchSession.Entry(string.Empty, 0)
                : SearchSession.Recall(SearchSession.Playlist(selected.Id));
            _videoSearch = saved.Text;
            _settingSearch = true;
            VideoSearch.Text = saved.Text;
            _settingSearch = false;
            if (selected is not null && !string.IsNullOrWhiteSpace(saved.Text) && saved.Offset > 0)
            {
                restoreOffset = saved.Offset;
            }
        }

        VideoRows.Children.Clear();
        if (selected is null)
        {
            return;
        }

        DetailTitle.Text = selected.Name;
        ToolTipService.SetToolTip(DetailTitle, selected.Name);
        var names = LibraryNames();
        string? LibraryName(string path) => names.TryGetValue(path, out var known) ? known : null;
        var visible = new List<int>();
        for (var i = 0; i < selected.Videos.Count; i++)
        {
            if (selected.Videos[i].Matches(_videoSearch, LibraryName) && ShowsEntry(selected.Videos[i]))
            {
                visible.Add(i);
            }
        }

        var watched = selected.Videos.Count(video => video.Watched);
        var searching = !string.IsNullOrWhiteSpace(_videoSearch);
        DetailCount.Text = visible.Count == selected.Videos.Count || (!searching && visible.Count == 0)
            ? selected.WatchedSummary()
            : $"{FilterLabel(visible.Count, selected.Videos.Count)} · {watched} watched";
        var anyPlayable = false;
        for (var place = 0; place < visible.Count; place++)
        {
            var index = visible[place];
            if (selected.Videos[index].IsPlayable)
            {
                anyPlayable = true;
            }

            VideoRows.Children.Add(CreateVideoRow(selected, selected.Videos[index], index, names, visible, place));
        }

        var empty = selected.Videos.Count == 0;
        var noMatch = !empty && visible.Count == 0;
        EmptyVideos.Text = empty
            ? "Add videos from your library, or add a stream from the player."
            : _unwatchedOnly && string.IsNullOrWhiteSpace(_videoSearch)
                ? "Every video is marked watched."
                : "No videos match that search.";
        EmptyVideos.Visibility = empty || noMatch ? Visibility.Visible : Visibility.Collapsed;
        VideoHost.Visibility = visible.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PlayButton.IsEnabled = anyPlayable;
        if (restoreOffset is double offset)
        {
            SearchScroll.Restore(VideoScroller, offset);
        }
    }

    private void VideoScroller_DragOver(object sender, DragEventArgs e)
    {
        if (_dragFrom is null)
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
        var y = e.GetPosition(VideoScroller).Y;
        if (y < 36)
        {
            VideoScroller.ChangeView(null, Math.Max(0, VideoScroller.VerticalOffset - 28), null, true);
        }
        else if (y > VideoScroller.ActualHeight - 36)
        {
            VideoScroller.ChangeView(null, VideoScroller.VerticalOffset + 28, null, true);
        }
    }

    private Border CreatePlaylistRow(Playlist list)
    {
        var selected = string.Equals(list.Id, _selectedId, StringComparison.Ordinal);
        var body = new StackPanel { Spacing = 2, IsHitTestVisible = false };
        body.Children.Add(new TextBlock
        {
            Text = list.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = PrimaryInk.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        body.Children.Add(new TextBlock
        {
            Text = CountLabel(list.Videos.Count),
            FontSize = 14,
            Foreground = SecondaryInk.Foreground
        });
        var row = new Grid
        {
            Padding = new Thickness(14, 12, 14, 12),
            ColumnSpacing = 10,
            Background = selected ? HoverInk.Background : _clear
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var accent = new Border { Background = selected ? AccentInk.Background : _clear };
        Grid.SetColumn(body, 1);
        row.Children.Add(accent);
        row.Children.Add(body);
        var host = new Border
        {
            BorderBrush = LineInk.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row
        };
        ToolTipService.SetToolTip(host, list.Name);
        var id = list.Id;
        host.Tapped += (_, args) =>
        {
            args.Handled = true;
            if (string.Equals(_selectedId, id, StringComparison.Ordinal))
            {
                return;
            }

            _selectedId = id;
            ShowLists();
        };
        host.PointerEntered += (_, _) => row.Background = HoverInk.Background;
        host.PointerExited += (_, args) =>
        {
            var point = args.GetCurrentPoint(host).Position;
            if (point.X < 0 || point.Y < 0 || point.X >= host.ActualWidth || point.Y >= host.ActualHeight)
            {
                row.Background = string.Equals(_selectedId, id, StringComparison.Ordinal) ? HoverInk.Background : _clear;
            }
        };
        return host;
    }

    private Border CreateVideoRow(
        Playlist list,
        PlaylistEntry entry,
        int index,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyList<int> visible,
        int place)
    {
        var playable = entry.IsPlayable;
        var title = entry.DisplayTitle(path => names.TryGetValue(path, out var known) ? known : null);
        var note = entry.Note();
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        text.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = playable ? PrimaryInk.Foreground : SecondaryInk.Foreground,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (note is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = note,
                FontSize = 14,
                Foreground = SecondaryInk.Foreground,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var playlistId = list.Id;
        var rowIndex = index;
        var canMoveUp = place > 0;
        var canMoveDown = place < visible.Count - 1;
        var upTo = canMoveUp ? visible[place - 1] : 0;
        var downTo = canMoveDown ? visible[place + 1] + 1 : 0;
        actions.Children.Add(IconButton("\uE70E", "Move up", canMoveUp, () => MoveVisible(playlistId, rowIndex, upTo)));
        actions.Children.Add(IconButton("\uE70D", "Move down", canMoveDown, () => MoveVisible(playlistId, rowIndex, downTo)));
        actions.Children.Add(IconButton("\uE74D", "Remove from playlist", true, () => RemoveVideo(playlistId, rowIndex)));

        var grip = new Border
        {
            Width = 36,
            MinHeight = 40,
            Background = _clear,
            CanDrag = true,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new FontIcon
            {
                Glyph = "\uE76F",
                FontSize = 14,
                Foreground = SecondaryInk.Foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            }
        };
        ToolTipService.SetToolTip(grip, "Drag to reorder");
        grip.DragStarting += (_, args) =>
        {
            _dragFrom = rowIndex;
            args.AllowedOperations = DataPackageOperation.Move;
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.SetText(rowIndex.ToString());
        };
        grip.DropCompleted += (_, _) => _dragFrom = null;

        var thumb = CreateThumbnail(entry, index + 1);
        var row = new Grid
        {
            Padding = new Thickness(4, 8, 12, 8),
            ColumnSpacing = 10,
            Background = _clear
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(thumb, 1);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(actions, 3);
        row.Children.Add(grip);
        row.Children.Add(thumb);
        row.Children.Add(text);
        row.Children.Add(actions);
        var topLine = new Border
        {
            Height = 3,
            VerticalAlignment = VerticalAlignment.Top,
            Background = AccentInk.Background,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        var bottomLine = new Border
        {
            Height = 3,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = AccentInk.Background,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        var surface = new Grid { Children = { row, topLine, bottomLine } };
        var host = new Border
        {
            AllowDrop = true,
            BorderBrush = LineInk.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = surface
        };
        ToolTipService.SetToolTip(host, note is null ? title : $"{title}{Environment.NewLine}{note}");
        var watchedNow = entry.Watched;
        host.ContextFlyout = RowMenu(
            watchedNow,
            () => ToggleWatched(playlistId, rowIndex, !watchedNow),
            () => QueueEntry(entry, names, next: false),
            () => QueueEntry(entry, names, next: true));
        void HideInsert()
        {
            topLine.Visibility = Visibility.Collapsed;
            bottomLine.Visibility = Visibility.Collapsed;
        }

        host.DragOver += (_, args) =>
        {
            if (_dragFrom is null)
            {
                return;
            }

            args.AcceptedOperation = DataPackageOperation.Move;
            if (args.DragUIOverride is { } dragUi)
            {
                dragUi.IsCaptionVisible = false;
                dragUi.IsGlyphVisible = false;
            }
            var topHalf = args.GetPosition(host).Y < host.ActualHeight / 2;
            topLine.Visibility = topHalf ? Visibility.Visible : Visibility.Collapsed;
            bottomLine.Visibility = topHalf ? Visibility.Collapsed : Visibility.Visible;
        };
        host.DragLeave += (_, args) =>
        {
            var point = args.GetPosition(host);
            if (point.X >= 0 && point.Y >= 0 && point.X < host.ActualWidth && point.Y < host.ActualHeight)
            {
                return;
            }

            HideInsert();
        };
        host.Drop += (_, args) =>
        {
            if (_dragFrom is not int from)
            {
                return;
            }

            args.AcceptedOperation = DataPackageOperation.Move;
            args.Handled = true;
            var topHalf = args.GetPosition(host).Y < host.ActualHeight / 2;
            var to = rowIndex + (topHalf ? 0 : 1);
            _dragFrom = null;
            HideInsert();
            var destination = to;
            DispatcherQueue.TryEnqueue(() => MoveVisible(playlistId, from, destination));
        };
        host.Tapped += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject source && (IsInside(source, actions) || IsInside(source, grip)))
            {
                return;
            }

            args.Handled = true;
            OpenVideo(playlistId, rowIndex);
        };
        host.PointerEntered += (_, _) => row.Background = HoverInk.Background;
        host.PointerExited += (_, args) =>
        {
            var point = args.GetCurrentPoint(host).Position;
            if (point.X < 0 || point.Y < 0 || point.X >= host.ActualWidth || point.Y >= host.ActualHeight)
            {
                row.Background = _clear;
            }
        };
        return host;
    }

    private Border CreateThumbnail(PlaylistEntry entry, int number)
    {
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false
        };
        image.ImageFailed += (_, _) => image.Source = null;
        var frame = new Grid
        {
            Width = 120,
            Height = 68,
            IsHitTestVisible = false,
            Children =
            {
                new FontIcon
                {
                    Glyph = "\uE714",
                    FontSize = 16,
                    Foreground = SecondaryInk.Foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                },
                image,
                new Border
                {
                    Margin = new Thickness(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(204, 0, 0, 0)),
                    CornerRadius = new CornerRadius(4),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = number.ToString(),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                        IsHitTestVisible = false
                    }
                }
            }
        };
        if (entry.Resolve)
        {
            var picture = entry.Thumbnail ?? StreamThumbnail.ForPage(entry.Location);
            if (picture is not null)
            {
                LoadRemoteThumb(image, picture);
            }
        }
        else if (entry.IsPlayable)
        {
            _ = LoadFileThumbAsync(image, entry.Location);
        }

        return new Border
        {
            Width = 120,
            Height = 68,
            CornerRadius = new CornerRadius(8),
            Background = HoverInk.Background,
            IsHitTestVisible = false,
            Child = frame
        };
    }

    private static void LoadRemoteThumb(Image target, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var imageUri))
        {
            return;
        }

        try
        {
            target.Source = new BitmapImage
            {
                DecodePixelWidth = 240,
                UriSource = imageUri
            };
        }
        catch (Exception)
        {
            target.Source = null;
        }
    }

    private static async Task LoadFileThumbAsync(Image target, string path)
    {
        try
        {
            var bitmap = await VideoThumbnail.LoadAsync(path, 240);
            if (bitmap is not null)
            {
                target.Source = bitmap;
            }
        }
        catch (Exception)
        {
            target.Source = null;
        }
    }

    private Button IconButton(string glyph, string tip, bool enabled, Action action)
    {
        var button = new Button
        {
            IsEnabled = enabled,
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        if (Application.Current.Resources.TryGetValue("IconButtonStyle", out var style) && style is Style icon)
        {
            button.Style = icon;
        }

        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private void MoveVisible(string id, int from, int to)
    {
        try
        {
            PlaylistChanges.Apply(id, "Moved a video.", () => Playlists.MoveTo(id, from, to));
            ShowLists();
        }
        catch (IOException)
        {
            ShowStatus("Could not reorder that video.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not reorder that video.", InfoBarSeverity.Error);
        }
    }

    private void ToggleWatched(string id, int index, bool watched)
    {
        try
        {
            Playlists.SetWatched(id, index, watched);
            ShowVideos(Selected());
        }
        catch (IOException)
        {
            ShowStatus("Could not save that change.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not save that change.", InfoBarSeverity.Error);
        }
    }

    private void QueueEntry(PlaylistEntry entry, IReadOnlyDictionary<string, string> names, bool next)
    {
        if (!entry.Resolve && !entry.IsPlayable)
        {
            ShowStatus("That video is no longer on this PC.", InfoBarSeverity.Warning);
            return;
        }

        string? Name(string path) => names.TryGetValue(path, out var known) ? known : null;
        var added = next ? PlayQueue.PlayNext(entry, Name) : PlayQueue.Add(entry, Name);
        if (added is null)
        {
            ShowStatus("That video could not be queued.", InfoBarSeverity.Warning);
            return;
        }

        ShowStatus(
            next ? $"\"{added.Title}\" will play next." : $"Added \"{added.Title}\" to the queue.",
            InfoBarSeverity.Success);
    }

    private static MenuFlyout RowMenu(bool watched, Action toggle, Action queue, Action playNext)
    {
        var next = new MenuFlyoutItem { Text = "Play next" };
        next.Click += (_, _) => playNext();
        var add = new MenuFlyoutItem { Text = "Add to queue" };
        add.Click += (_, _) => queue();
        var item = new MenuFlyoutItem { Text = watched ? "Mark as unwatched" : "Mark as watched" };
        item.Click += (_, _) => toggle();
        var flyout = new MenuFlyout();
        flyout.Items.Add(next);
        flyout.Items.Add(add);
        flyout.Items.Add(item);
        return flyout;
    }

    private bool ShowsEntry(PlaylistEntry entry) => !_unwatchedOnly || !entry.Watched;

    private void RemoveVideo(string id, int index)
    {
        try
        {
            PlaylistChanges.Apply(id, "Removed a video.", () => Playlists.RemoveAt(id, index));
            ShowLists();
        }
        catch (IOException)
        {
            ShowStatus("Could not remove that video.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not remove that video.", InfoBarSeverity.Error);
        }
    }

    private void OpenVideo(string playlistId, int index)
        => NavigationHelper.OpenPlayer(new PlaylistOpenRequest(playlistId, index));

    private async Task<IReadOnlyList<string>> PickVideosAsync(string playlistId)
    {
        var list = Playlists.Find(playlistId);
        var present = new HashSet<string>(
            list?.Videos.Where(item => !item.Resolve).Select(item => item.Location) ?? [],
            StringComparer.OrdinalIgnoreCase);
        var choices = App.MediaLibrary.GetItems()
            .Where(IsLibraryVideo)
            .Where(item => !present.Contains(item.FilePath))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (choices.Count == 0)
        {
            ShowStatus(present.Count == 0
                ? "Your library has no videos yet."
                : "Every library video is already in this playlist.",
                InfoBarSeverity.Informational);
            return [];
        }

        var picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filling = false;
        var hint = new TextBlock
        {
            Text = "Select at least one video.",
            Foreground = SecondaryInk.Foreground,
            Visibility = Visibility.Collapsed
        };
        var note = new TextBlock
        {
            Text = present.Count == 0 ? "Select one video or several." : "Videos already in this playlist are hidden.",
            Foreground = SecondaryInk.Foreground,
            TextWrapping = TextWrapping.Wrap
        };
        var search = new AutoSuggestBox
        {
            PlaceholderText = "Search videos",
            QueryIcon = new SymbolIcon(Symbol.Find)
        };
        var view = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            DisplayMemberPath = nameof(MediaItem.DisplayName),
            Height = 320
        };
        void Reload()
        {
            filling = true;
            view.Items.Clear();
            var term = search.Text.Trim();
            foreach (var item in choices)
            {
                if (term.Length > 0 && !item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                view.Items.Add(item);
                if (picked.Contains(item.FilePath))
                {
                    view.SelectedItems.Add(item);
                }
            }

            filling = false;
        }

        view.SelectionChanged += (_, args) =>
        {
            if (filling)
            {
                return;
            }

            foreach (var item in args.AddedItems.OfType<MediaItem>())
            {
                picked.Add(item.FilePath);
            }

            foreach (var item in args.RemovedItems.OfType<MediaItem>())
            {
                picked.Remove(item.FilePath);
            }

            if (picked.Count > 0)
            {
                hint.Visibility = Visibility.Collapsed;
            }
        };
        search.TextChanged += (_, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                Reload();
            }
        };
        Reload();
        var dialog = new ContentDialog
        {
            Title = "Add videos",
            Content = new StackPanel
            {
                Spacing = 10,
                Width = 440,
                Children = { note, search, hint, view }
            },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (picked.Count == 0)
            {
                args.Cancel = true;
                hint.Visibility = Visibility.Visible;
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return [];
        }

        return choices.Where(item => picked.Contains(item.FilePath)).Select(item => item.FilePath).ToArray();
    }

    private Playlist? Selected()
        => _selectedId is null ? null : Playlists.Find(_selectedId);

    private bool TryReadName(string text, string? exceptId, out string name)
    {
        name = text.Trim();
        if (name.Length == 0)
        {
            ShowStatus("Enter a name for the playlist.", InfoBarSeverity.Informational);
            return false;
        }

        if (name.Length > 80)
        {
            ShowStatus("Use a name of 80 characters or fewer.", InfoBarSeverity.Informational);
            return false;
        }

        if (Playlists.NameTaken(name, exceptId))
        {
            ShowStatus("You already have a playlist with that name.", InfoBarSeverity.Informational);
            return false;
        }

        return true;
    }

    private void OnPlaylistChangesChanged()
    {
        if (!DispatcherQueue.TryEnqueue(UpdateUndoMessage))
        {
            UpdateUndoMessage();
        }
    }

    private void OnQueueCopied()
    {
        if (!DispatcherQueue.TryEnqueue(ShowLists))
        {
            ShowLists();
        }
    }

    private void UpdateUndoMessage()
    {
        if (PlaylistChanges.IsPending(_selectedId, out var message))
        {
            PresentUndo(message);
            return;
        }

        if (_showingUndo)
        {
            _showingUndo = false;
            HideStatus();
        }
    }

    private void PresentUndo(string message)
    {
        _showingUndo = true;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Message = message;
        StatusBar.ActionButton = UndoButton();
        StatusBar.Visibility = Visibility.Visible;
        StatusBar.IsOpen = true;
    }

    private void UndoLast()
    {
        try
        {
            PlaylistChanges.Undo();
            _showingUndo = false;
            HideStatus();
            ShowLists();
        }
        catch (IOException)
        {
            ShowStatus("Could not undo that change.", InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not undo that change.", InfoBarSeverity.Error);
        }
    }

    private Button UndoButton()
    {
        var button = new Button { Content = "Undo" };
        button.Click += (_, _) => UndoLast();
        return button;
    }

    private void StatusBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        StatusBar.Visibility = Visibility.Collapsed;
        if (_closeSuppress > 0)
        {
            _closeSuppress--;
            return;
        }

        if (_showingUndo)
        {
            _showingUndo = false;
            StatusBar.ActionButton = null;
            PlaylistChanges.Dismiss();
            return;
        }

        if (PlaylistChanges.IsPending(_selectedId, out var message))
        {
            PresentUndo(message);
        }
    }

    private void HideStatus()
    {
        StatusBar.ActionButton = null;
        if (!StatusBar.IsOpen)
        {
            StatusBar.Visibility = Visibility.Collapsed;
            return;
        }

        _closeSuppress++;
        StatusBar.IsOpen = false;
        StatusBar.Visibility = Visibility.Collapsed;
    }

    private void ShowStatus(string? message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        _showingUndo = false;
        StatusBar.ActionButton = null;
        if (string.IsNullOrWhiteSpace(message))
        {
            HideStatus();
            return;
        }

        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.Visibility = Visibility.Visible;
        StatusBar.IsOpen = true;
    }

    private static Dictionary<string, string> LibraryNames()
        => App.MediaLibrary.GetItems()
            .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);

    private static bool IsLibraryVideo(MediaItem item)
    {
        if (!item.IsVideo || string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
        {
            return false;
        }

        var extension = Path.GetExtension(item.FilePath);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".avi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase);
    }

    private static string CountLabel(int count)
        => count == 0 ? "No videos" : count == 1 ? "1 video" : $"{count} videos";

    private static string FilterLabel(int shown, int total)
    {
        if (total == 0 || shown == total)
        {
            return CountLabel(total);
        }

        if (shown == 0)
        {
            return total == 1 ? "No matches in 1 video" : $"No matches in {total} videos";
        }

        return $"{shown} of {total} videos";
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
