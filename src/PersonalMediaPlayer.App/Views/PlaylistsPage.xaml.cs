using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core;
using PersonalMediaPlayer.Core.Models;
using Windows.System;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class PlaylistsPage : Page
{
    private readonly SolidColorBrush _clear = new(Microsoft.UI.Colors.Transparent);
    private string? _selectedId;

    public PlaylistsPage()
    {
        InitializeComponent();
        StatusBar.Closed += (_, _) => StatusBar.Visibility = Visibility.Collapsed;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(ShowLists);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var lists = Playlists.All();
        if (_selectedId is null || lists.All(list => list.Id != _selectedId))
        {
            _selectedId = lists.FirstOrDefault()?.Id;
        }

        ShowLists();
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

        var index = list.Videos.FindIndex(File.Exists);
        if (index < 0)
        {
            ShowStatus("None of these videos are on this PC.", InfoBarSeverity.Warning);
            return;
        }

        OpenVideo(list.Id, index);
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
        foreach (var list in lists)
        {
            PlaylistRows.Children.Add(CreatePlaylistRow(list));
        }

        var selected = Selected();
        var showDetail = selected is not null;
        EmptyDetail.Visibility = showDetail ? Visibility.Collapsed : Visibility.Visible;
        DetailCard.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        VideoRows.Children.Clear();
        if (selected is null)
        {
            return;
        }

        DetailTitle.Text = selected.Name;
        ToolTipService.SetToolTip(DetailTitle, selected.Name);
        DetailCount.Text = CountLabel(selected.Videos.Count);
        var names = LibraryNames();
        var anyPlayable = false;
        for (var i = 0; i < selected.Videos.Count; i++)
        {
            if (File.Exists(selected.Videos[i]))
            {
                anyPlayable = true;
            }

            VideoRows.Children.Add(CreateVideoRow(selected, selected.Videos[i], i, names));
        }

        var empty = selected.Videos.Count == 0;
        EmptyVideos.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        VideoHost.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        PlayButton.IsEnabled = anyPlayable;
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
            FontSize = 12,
            Foreground = SecondaryInk.Foreground
        });
        var row = new Grid
        {
            Padding = new Thickness(12, 10, 12, 10),
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

    private Border CreateVideoRow(Playlist list, string path, int index, IReadOnlyDictionary<string, string> names)
    {
        var exists = File.Exists(path);
        var title = names.TryGetValue(path, out var known) ? known : Path.GetFileName(path);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        text.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = exists ? PrimaryInk.Foreground : SecondaryInk.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!exists)
        {
            text.Children.Add(new TextBlock
            {
                Text = "Not on this PC",
                FontSize = 12,
                Foreground = SecondaryInk.Foreground
            });
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var playlistId = list.Id;
        var rowIndex = index;
        actions.Children.Add(IconButton("\uE70E", "Move up", index > 0, () => Reorder(playlistId, rowIndex, -1)));
        actions.Children.Add(IconButton("\uE70D", "Move down", index < list.Videos.Count - 1, () => Reorder(playlistId, rowIndex, 1)));
        actions.Children.Add(IconButton("\uE74D", "Remove from playlist", true, () => RemoveVideo(playlistId, rowIndex)));

        var row = new Grid
        {
            Padding = new Thickness(16, 8, 12, 8),
            ColumnSpacing = 12,
            Background = _clear
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = new TextBlock
        {
            Text = $"{index + 1}",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SecondaryInk.Foreground,
            IsHitTestVisible = false
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(actions, 2);
        row.Children.Add(number);
        row.Children.Add(text);
        row.Children.Add(actions);
        var host = new Border
        {
            BorderBrush = LineInk.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row
        };
        ToolTipService.SetToolTip(host, exists ? title : $"{title}{Environment.NewLine}Not on this PC");
        host.Tapped += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject source && IsInside(source, actions))
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

    private Button IconButton(string glyph, string tip, bool enabled, Action action)
    {
        var button = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            IsEnabled = enabled,
            Background = _clear,
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = PrimaryInk.Foreground
            }
        };
        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private void Reorder(string id, int index, int delta)
    {
        try
        {
            Playlists.MoveVideo(id, index, delta);
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

    private void RemoveVideo(string id, int index)
    {
        try
        {
            Playlists.RemoveAt(id, index);
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
        => Frame.Navigate(typeof(VideoPlayerPage), new PlaylistOpenRequest(playlistId, index));

    private async Task<IReadOnlyList<string>> PickVideosAsync(string playlistId)
    {
        var list = Playlists.Find(playlistId);
        var present = new HashSet<string>(list?.Videos ?? [], StringComparer.OrdinalIgnoreCase);
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

    private void ShowStatus(string? message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            StatusBar.IsOpen = false;
            StatusBar.Visibility = Visibility.Collapsed;
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
