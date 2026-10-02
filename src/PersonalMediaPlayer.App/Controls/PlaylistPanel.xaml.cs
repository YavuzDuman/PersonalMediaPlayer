using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalMediaPlayer.App.Playback;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class PlaylistPanel : UserControl
{
    private readonly SolidColorBrush _clear = new(Microsoft.UI.Colors.Transparent);
    private string? _playlistId;
    private int _index = -1;
    private bool _loading;
    private Border? _currentHost;
    private int? _dragFrom;

    public PlaylistPanel()
    {
        InitializeComponent();
        Status.Closed += (_, _) => Status.Visibility = Visibility.Collapsed;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => Refresh());
    }

    internal event EventHandler<int>? VideoChosen;

    internal event EventHandler<int>? OrderChanged;

    public bool IsOpen => Panel.Visibility == Visibility.Visible;

    public void Show(string playlistId, int index)
    {
        var first = Visibility != Visibility.Visible;
        _playlistId = playlistId;
        _index = index;
        Visibility = Visibility.Visible;
        if (first)
        {
            Open();
            return;
        }

        if (IsOpen)
        {
            Refresh();
        }
    }

    public void Hide()
    {
        _playlistId = null;
        _index = -1;
        _currentHost = null;
        _dragFrom = null;
        Visibility = Visibility.Collapsed;
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Collapse();
        }
        else
        {
            Open();
        }
    }

    public void SetStatus(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Status.IsOpen = false;
            Status.Visibility = Visibility.Collapsed;
            return;
        }

        Status.Severity = InfoBarSeverity.Warning;
        Status.Message = message;
        Status.Visibility = Visibility.Visible;
        Status.IsOpen = true;
    }

    private void Open()
    {
        Rail.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Visible;
        Refresh();
    }

    private void Collapse()
    {
        Panel.Visibility = Visibility.Collapsed;
        Rail.Visibility = Visibility.Visible;
    }

    private void Rail_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        Open();
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();

    private void Previous_Click(object sender, RoutedEventArgs e) => ChooseNeighbor(-1);

    private void Next_Click(object sender, RoutedEventArgs e) => ChooseNeighbor(1);

    private void PlayNext_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _playlistId is null || PlayNextBox.IsChecked is not bool playNext)
        {
            return;
        }

        try
        {
            Playlists.SetPlayNext(_playlistId, playNext);
        }
        catch (IOException)
        {
            SetStatus("Could not save that setting.");
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Could not save that setting.");
        }
    }

    private void ChooseNeighbor(int delta)
    {
        var list = _playlistId is null ? null : Playlists.Find(_playlistId);
        var next = list is null ? -1 : Neighbor(list, _index, delta);
        if (next >= 0)
        {
            VideoChosen?.Invoke(this, next);
        }
    }

    private void Scroller_DragOver(object sender, DragEventArgs e)
    {
        if (_dragFrom is null)
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
        var y = e.GetPosition(Scroller).Y;
        if (y < 36)
        {
            Scroller.ChangeView(null, Math.Max(0, Scroller.VerticalOffset - 28), null, true);
        }
        else if (y > Scroller.ActualHeight - 36)
        {
            Scroller.ChangeView(null, Scroller.VerticalOffset + 28, null, true);
        }
    }

    private void Refresh(bool bringCurrentIntoView = true)
    {
        var keptOffset = bringCurrentIntoView ? 0 : Scroller.VerticalOffset;
        List.Children.Clear();
        _currentHost = null;
        var list = _playlistId is null ? null : Playlists.Find(_playlistId);
        if (list is null)
        {
            PlaylistName.Text = "Playlist";
            PositionText.Text = "This playlist is no longer available.";
            PreviousButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            PlayNextBox.IsEnabled = false;
            Scroller.Visibility = Visibility.Collapsed;
            Empty.Visibility = Visibility.Visible;
            Empty.Text = "It may have been deleted.";
            return;
        }

        PlaylistName.Text = list.Name;
        ToolTipService.SetToolTip(PlaylistName, list.Name);
        var count = list.Videos.Count;
        var place = _index >= 0 && _index < count ? _index + 1 : 0;
        PositionText.Text = count == 0 ? "No videos" : $"{place} of {count}";
        PreviousButton.IsEnabled = Neighbor(list, _index, -1) >= 0;
        NextButton.IsEnabled = Neighbor(list, _index, 1) >= 0;
        PlayNextBox.IsEnabled = true;
        _loading = true;
        PlayNextBox.IsChecked = list.PlayNext;
        _loading = false;
        if (count == 0)
        {
            Scroller.Visibility = Visibility.Collapsed;
            Empty.Visibility = Visibility.Visible;
            Empty.Text = "This playlist is empty.";
            return;
        }

        Empty.Visibility = Visibility.Collapsed;
        Scroller.Visibility = Visibility.Visible;
        var names = App.MediaLibrary.GetItems()
            .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Videos.Count; i++)
        {
            List.Children.Add(CreateRow(list.Videos[i], i, list.Videos.Count, names));
        }

        if (bringCurrentIntoView && _currentHost is Border current)
        {
            DispatcherQueue.TryEnqueue(() =>
                current.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.3 }));
            return;
        }

        if (!bringCurrentIntoView)
        {
            var offset = keptOffset;
            DispatcherQueue.TryEnqueue(() =>
            {
                Scroller.UpdateLayout();
                Scroller.ChangeView(null, offset, null, true);
            });
        }
    }

    private Border CreateRow(PlaylistEntry entry, int index, int count, IReadOnlyDictionary<string, string> names)
    {
        var current = index == _index;
        var playable = entry.IsPlayable;
        var title = entry.DisplayTitle(path => names.TryGetValue(path, out var known) ? known : null);
        var note = entry.Resolve ? "Online" : playable ? null : "Not on this PC";
        var body = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = playable ? PrimaryInk.Foreground : SecondaryInk.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        });
        if (note is not null)
        {
            body.Children.Add(new TextBlock
            {
                Text = note,
                FontSize = 12,
                Foreground = SecondaryInk.Foreground,
                IsHitTestVisible = false
            });
        }

        var heading = new Grid { ColumnSpacing = 6 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var number = new TextBlock
        {
            Text = $"{index + 1}",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SecondaryInk.Foreground,
            IsHitTestVisible = false
        };
        var titleBlock = (TextBlock)body.Children[0];
        titleBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
        body.Children.RemoveAt(0);
        Grid.SetColumn(titleBlock, 1);
        heading.Children.Add(number);
        heading.Children.Add(titleBlock);
        body.Children.Insert(0, heading);

        var canMoveUp = index > 0;
        var canMoveDown = index < count - 1;
        var actions = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var rowIndex = index;
        actions.Children.Add(IconButton("\uE70E", "Move up", canMoveUp, () => MoveRow(rowIndex, rowIndex - 1)));
        actions.Children.Add(IconButton("\uE70D", "Move down", canMoveDown, () => MoveRow(rowIndex, rowIndex + 2)));

        var grip = new Border
        {
            Width = 28,
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

        var thumb = CreateThumbnail(entry);
        var row = new Grid
        {
            Padding = new Thickness(2, 6, 4, 6),
            ColumnSpacing = 8,
            Background = current ? HoverInk.Background : _clear
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(thumb, 1);
        Grid.SetColumn(body, 2);
        Grid.SetColumn(actions, 3);
        row.Children.Add(grip);
        row.Children.Add(thumb);
        row.Children.Add(body);
        row.Children.Add(actions);

        var accent = new Border
        {
            Width = 3,
            Background = current ? AccentInk.Background : _clear
        };
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(row, 1);
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
        Grid.SetColumnSpan(topLine, 2);
        Grid.SetColumnSpan(bottomLine, 2);
        shell.Children.Add(accent);
        shell.Children.Add(row);
        shell.Children.Add(topLine);
        shell.Children.Add(bottomLine);

        var host = new Border
        {
            AllowDrop = true,
            Background = _clear,
            BorderBrush = LineInk.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = shell
        };
        ToolTipService.SetToolTip(host, note is null ? title : $"{title}{Environment.NewLine}{note}");
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
            var destination = rowIndex + (topHalf ? 0 : 1);
            _dragFrom = null;
            HideInsert();
            MoveRow(from, destination);
        };
        var chosen = index;
        host.Tapped += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject source && (IsInside(source, actions) || IsInside(source, grip)))
            {
                return;
            }

            args.Handled = true;
            VideoChosen?.Invoke(this, chosen);
        };
        host.PointerEntered += (_, _) =>
        {
            if (chosen != _index)
            {
                row.Background = HoverInk.Background;
            }
        };
        host.PointerExited += (_, args) =>
        {
            var point = args.GetCurrentPoint(host).Position;
            if (point.X < 0 || point.Y < 0 || point.X >= host.ActualWidth || point.Y >= host.ActualHeight)
            {
                row.Background = chosen == _index ? HoverInk.Background : _clear;
            }
        };
        if (current)
        {
            _currentHost = host;
        }

        return host;
    }

    private Border CreateThumbnail(PlaylistEntry entry)
    {
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false
        };
        image.ImageFailed += (_, _) => image.Source = null;
        var frame = new Grid
        {
            Width = 72,
            Height = 40,
            IsHitTestVisible = false,
            Children =
            {
                new FontIcon
                {
                    Glyph = "\uE714",
                    FontSize = 14,
                    Foreground = SecondaryInk.Foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                },
                image
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
            Width = 72,
            Height = 40,
            CornerRadius = new CornerRadius(4),
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
                DecodePixelWidth = 160,
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
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 160);
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

    private Button IconButton(string glyph, string tip, bool enabled, Action action)
    {
        var button = new Button
        {
            Width = 28,
            Height = 28,
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

    private void MoveRow(int from, int to)
    {
        if (!DispatcherQueue.TryEnqueue(() => ApplyMove(from, to)))
        {
            ApplyMove(from, to);
        }
    }

    private void ApplyMove(int from, int to)
    {
        if (_playlistId is null)
        {
            return;
        }

        var list = Playlists.Find(_playlistId);
        if (list is null || _index < 0 || _index >= list.Videos.Count)
        {
            return;
        }

        var playing = list.Videos[_index];
        try
        {
            if (!Playlists.MoveTo(_playlistId, from, to))
            {
                return;
            }
        }
        catch (IOException)
        {
            SetStatus("Could not reorder that video.");
            return;
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Could not reorder that video.");
            return;
        }

        var updated = Playlists.Find(_playlistId);
        var next = updated?.Videos.FindIndex(item => SameEntry(item, playing)) ?? -1;
        var last = Math.Max(0, (updated?.Videos.Count ?? 1) - 1);
        _index = next >= 0 ? next : Math.Clamp(_index, 0, last);
        Refresh(bringCurrentIntoView: false);
        OrderChanged?.Invoke(this, _index);
    }

    private static bool SameEntry(PlaylistEntry left, PlaylistEntry right)
        => left.Resolve == right.Resolve
           && string.Equals(left.Location, right.Location, StringComparison.OrdinalIgnoreCase);

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

    private static int Neighbor(Playlist list, int index, int delta)
    {
        for (var i = index + delta; i >= 0 && i < list.Videos.Count; i += delta)
        {
            if (list.Videos[i].IsPlayable)
            {
                return i;
            }
        }

        return -1;
    }
}
