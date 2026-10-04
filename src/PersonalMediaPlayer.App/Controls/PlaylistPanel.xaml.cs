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
    private bool _unwatchedOnly;
    private Border? _currentHost;
    private int? _dragFrom;
    private bool _showingUndo;
    private int _closeSuppress;

    public PlaylistPanel()
    {
        InitializeComponent();
        Status.Closed += Status_Closed;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => Refresh());
        PlaylistChanges.Changed += OnPlaylistChangesChanged;
        PlayQueue.Changed += OnQueueChanged;
        Unloaded += (_, _) =>
        {
            PlaylistChanges.Changed -= OnPlaylistChangesChanged;
            PlayQueue.Changed -= OnQueueChanged;
        };
    }

    internal event EventHandler<int>? VideoChosen;

    internal event EventHandler? ReplayChosen;

    internal event EventHandler? ModeChanged;

    internal event EventHandler? NextChosen;

    internal event EventHandler<int>? OrderChanged;

    public bool IsOpen => Panel.Visibility == Visibility.Visible;

    public bool UnwatchedOnly => _unwatchedOnly;

    public void Sync(string playlistId, int index)
    {
        _playlistId = playlistId;
        _index = index;
        if (Visibility != Visibility.Visible)
        {
            return;
        }

        if (IsOpen)
        {
            Refresh();
            return;
        }

        UpdateRail();
    }

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
            return;
        }

        UpdateRail();
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

    public void SetStatus(string? message, InfoBarSeverity severity = InfoBarSeverity.Warning)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ShowPendingUndo(replaceStatus: true);
            return;
        }

        _showingUndo = false;
        Status.ActionButton = null;
        Status.Severity = severity;
        Status.Message = message;
        Status.Visibility = Visibility.Visible;
        Status.IsOpen = true;
    }

    private void Open()
    {
        Grid.SetColumn(this, 3);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Margin = new Thickness(12, 0, 0, 0);
        Rail.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Visible;
        Refresh();
    }

    private void Collapse()
    {
        Panel.Visibility = Visibility.Collapsed;
        Grid.SetColumn(this, 0);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(16, 12, 0, 0);
        UpdateRail();
        Rail.Visibility = Visibility.Visible;
    }

    private void UpdateRail()
    {
        var list = _playlistId is null ? null : Playlists.Find(_playlistId);
        if (list is null)
        {
            RailName.Text = "Playlist";
            RailCount.Text = "Show playlist";
            ToolTipService.SetToolTip(Rail, "Show playlist");
            return;
        }

        RailName.Text = list.Name;
        var note = PlaylistRuns.For(list.Id).ActiveNote;
        RailCount.Text = note is null ? list.WatchedSummary() : list.WatchedSummary() + " · " + note;
        var tip = list.Name + Environment.NewLine + list.WatchedSummary();
        if (note is not null)
        {
            tip += Environment.NewLine + note;
        }

        ToolTipService.SetToolTip(Rail, tip + Environment.NewLine + "Show playlist");
    }

    private void Unwatched_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || List is null)
        {
            return;
        }

        _unwatchedOnly = UnwatchedBox.IsChecked == true;
        if (IsOpen)
        {
            Refresh(bringCurrentIntoView: false);
        }
    }

    private void Rail_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        Open();
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();

    private void Previous_Click(object sender, RoutedEventArgs e) => ChooseNeighbor(forward: false);

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (NextChosen is not null)
        {
            NextChosen.Invoke(this, EventArgs.Empty);
            return;
        }

        ChooseNeighbor(forward: true);
    }

    internal void PlayForward() => ChooseNeighbor(forward: true);

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        var run = CurrentRun();
        if (run is null)
        {
            return;
        }

        run.SetShuffle(!run.Shuffle);
        ShowMode();
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        var run = CurrentRun();
        if (run is null)
        {
            return;
        }

        run.CycleRepeat();
        ShowMode();
    }

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

    private void ChooseNeighbor(bool forward)
    {
        var list = _playlistId is null ? null : Playlists.Find(_playlistId);
        var run = CurrentRun();
        if (list is null || run is null)
        {
            return;
        }

        var step = run.Move(
            PlaylistRun.Keys(list),
            index => PlaylistRun.Include(list, index, _index, _unwatchedOnly),
            _index,
            playNext: true,
            fromEnd: false,
            forward,
            PlaylistRuns.Random);
        if (step.Kind == PlaylistStepKind.Open)
        {
            VideoChosen?.Invoke(this, step.Index);
        }
        else if (step.Kind == PlaylistStepKind.Replay)
        {
            ReplayChosen?.Invoke(this, EventArgs.Empty);
        }
    }

    private PlaylistRun? CurrentRun()
        => _playlistId is null ? null : PlaylistRuns.For(_playlistId);

    private void ShowMode()
    {
        var run = CurrentRun();
        var list = _playlistId is null ? null : Playlists.Find(_playlistId);
        var enabled = run is not null && list is { Videos.Count: > 0 };
        ShuffleButton.IsEnabled = enabled;
        RepeatButton.IsEnabled = enabled;
        if (run is null)
        {
            ShuffleLabel.Text = "Shuffle";
            RepeatLabel.Text = "Repeat off";
            RepeatIcon.Glyph = "\uE1CD";
            ShuffleButton.Style = null;
            RepeatButton.Style = null;
            ModeLine.Text = "Saved order. Repeat is off.";
            PreviousButton.IsEnabled = false;
            NextButton.IsEnabled = PlayQueue.Count > 0;
            UpdateRail();
            return;
        }

        ShuffleLabel.Text = run.ShuffleLabel;
        RepeatLabel.Text = run.RepeatLabel;
        RepeatIcon.Glyph = run.RepeatGlyph;
        ModeLine.Text = run.Summary;
        Style? accent = Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) ? style as Style : null;
        ShuffleButton.Style = run.Shuffle ? accent : null;
        RepeatButton.Style = run.Repeat == PlaylistRepeat.Off ? null : accent;
        ToolTipService.SetToolTip(
            ShuffleButton,
            run.Shuffle
                ? "Shuffle is on. Each video plays once before one plays again."
                : "Shuffle is off. Click to play in a random order.");
        ToolTipService.SetToolTip(RepeatButton, run.Repeat switch
        {
            PlaylistRepeat.All => "Repeating the playlist. Click to repeat this video.",
            PlaylistRepeat.One => "Repeating this video. Click to turn repeat off.",
            _ => "Repeat is off. Click to repeat the playlist."
        });
        if (list is null)
        {
            PreviousButton.IsEnabled = false;
            NextButton.IsEnabled = PlayQueue.Count > 0;
        }
        else
        {
            var keys = PlaylistRun.Keys(list);
            bool Include(int index) => PlaylistRun.Include(list, index, _index, _unwatchedOnly);
            PreviousButton.IsEnabled = run.HasMove(keys, Include, _index, forward: false);
            NextButton.IsEnabled = PlayQueue.Count > 0 || run.HasMove(keys, Include, _index, forward: true);
        }

        UpdateRail();
        ModeChanged?.Invoke(this, EventArgs.Empty);
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
        try
        {
            Rebuild(bringCurrentIntoView);
        }
        finally
        {
            ShowMode();
            ShowPendingUndo(replaceStatus: false);
        }
    }

    private void Rebuild(bool bringCurrentIntoView)
    {
        var keptOffset = bringCurrentIntoView ? 0 : Scroller.VerticalOffset;
        List.Children.Clear();
        _currentHost = null;
        var list = _playlistId is null ? null : Playlists.Find(_playlistId);
        if (list is null)
        {
            PlaylistName.Text = "Playlist";
            PositionText.Text = "This playlist is no longer available.";
            WatchedText.Visibility = Visibility.Collapsed;
            UnwatchedBox.IsEnabled = false;
            PlayNextBox.IsEnabled = false;
            Scroller.Visibility = Visibility.Collapsed;
            Empty.Visibility = Visibility.Visible;
            Empty.Text = "It may have been deleted.";
            return;
        }

        PlaylistName.Text = list.Name;
        ToolTipService.SetToolTip(PlaylistName, list.Name);
        var count = list.Videos.Count;
        var position = _index >= 0 && _index < count ? _index + 1 : 0;
        PositionText.Text = count == 0 ? "No videos" : $"{position} of {count}";
        WatchedText.Text = list.WatchedSummary();
        WatchedText.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UnwatchedBox.IsEnabled = count > 0;
        PlayNextBox.IsEnabled = true;
        _loading = true;
        PlayNextBox.IsChecked = list.PlayNext;
        _loading = false;
        var visible = new List<int>();
        for (var i = 0; i < count; i++)
        {
            if (_unwatchedOnly && list.Videos[i].Watched)
            {
                continue;
            }

            visible.Add(i);
        }

        if (visible.Count == 0)
        {
            Scroller.Visibility = Visibility.Collapsed;
            Empty.Visibility = Visibility.Visible;
            Empty.Text = count == 0 ? "This playlist is empty." : "Every video is marked watched.";
            return;
        }

        Empty.Visibility = Visibility.Collapsed;
        Scroller.Visibility = Visibility.Visible;
        var names = App.MediaLibrary.GetItems()
            .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        for (var place = 0; place < visible.Count; place++)
        {
            var index = visible[place];
            List.Children.Add(CreateRow(list.Videos[index], index, visible, place, names));
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

    private Border CreateRow(
        PlaylistEntry entry,
        int index,
        IReadOnlyList<int> visible,
        int place,
        IReadOnlyDictionary<string, string> names)
    {
        var current = index == _index;
        var playable = entry.IsPlayable;
        var title = entry.DisplayTitle(path => names.TryGetValue(path, out var known) ? known : null);
        var note = entry.Note();
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

        var canMoveUp = place > 0;
        var canMoveDown = place < visible.Count - 1;
        var upTo = canMoveUp ? visible[place - 1] : 0;
        var downTo = canMoveDown ? visible[place + 1] + 1 : 0;
        var actions = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var rowIndex = index;
        actions.Children.Add(IconButton("\uE70E", "Move up", canMoveUp, () => MoveRow(rowIndex, upTo)));
        actions.Children.Add(IconButton("\uE70D", "Move down", canMoveDown, () => MoveRow(rowIndex, downTo)));

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
        var watchedNow = entry.Watched;
        host.ContextFlyout = RowMenu(
            watchedNow,
            () => ToggleWatched(rowIndex, !watchedNow),
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
            if (!PlaylistChanges.Apply(_playlistId, "Moved a video.", () => Playlists.MoveTo(_playlistId, from, to)))
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

        KeepPlaying(playing);
        ShowPendingUndo(replaceStatus: true);
    }

    private void KeepPlaying(PlaylistEntry playing)
    {
        var updated = _playlistId is null ? null : Playlists.Find(_playlistId);
        var count = updated?.Videos.Count ?? 0;
        if (updated is null || count == 0)
        {
            _index = -1;
            Refresh(bringCurrentIntoView: false);
            return;
        }

        var next = PlaylistChanges.IndexOf(updated.Videos, playing);
        _index = next >= 0 ? next : Math.Clamp(_index, 0, count - 1);
        Refresh(bringCurrentIntoView: false);
        OrderChanged?.Invoke(this, _index);
    }

    private void OnPlaylistChangesChanged()
    {
        if (!DispatcherQueue.TryEnqueue(() => ShowPendingUndo(replaceStatus: false)))
        {
            ShowPendingUndo(replaceStatus: false);
        }
    }

    private void ShowPendingUndo(bool replaceStatus)
    {
        if (!replaceStatus && !_showingUndo && Status.IsOpen)
        {
            return;
        }

        if (_playlistId is null || !PlaylistChanges.IsPending(_playlistId, out var message))
        {
            if (_showingUndo)
            {
                _showingUndo = false;
                HideStatus();
            }

            return;
        }

        _showingUndo = true;
        Status.Severity = InfoBarSeverity.Informational;
        Status.Message = message;
        Status.ActionButton = UndoButton();
        Status.Visibility = Visibility.Visible;
        Status.IsOpen = true;
    }

    private void UndoLast()
    {
        if (_playlistId is null)
        {
            return;
        }

        var list = Playlists.Find(_playlistId);
        var playing = list is not null && _index >= 0 && _index < list.Videos.Count
            ? list.Videos[_index]
            : null;
        try
        {
            var id = PlaylistChanges.Undo();
            _showingUndo = false;
            HideStatus();
            if (playing is null || !string.Equals(id, _playlistId, StringComparison.Ordinal))
            {
                Refresh(bringCurrentIntoView: false);
                return;
            }

            KeepPlaying(playing);
        }
        catch (IOException)
        {
            SetStatus("Could not undo that change.");
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Could not undo that change.");
        }
    }

    private Button UndoButton()
    {
        var button = new Button { Content = "Undo" };
        button.Click += (_, _) => UndoLast();
        return button;
    }

    private void Status_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        Status.Visibility = Visibility.Collapsed;
        if (_closeSuppress > 0)
        {
            _closeSuppress--;
            return;
        }

        if (_showingUndo)
        {
            _showingUndo = false;
            Status.ActionButton = null;
            PlaylistChanges.Dismiss();
            return;
        }

        ShowPendingUndo(replaceStatus: true);
    }

    private void HideStatus()
    {
        Status.ActionButton = null;
        if (!Status.IsOpen)
        {
            Status.Visibility = Visibility.Collapsed;
            return;
        }

        _closeSuppress++;
        Status.IsOpen = false;
        Status.Visibility = Visibility.Collapsed;
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

    private void ToggleWatched(int index, bool watched)
    {
        if (_playlistId is null)
        {
            return;
        }

        try
        {
            Playlists.SetWatched(_playlistId, index, watched);
        }
        catch (IOException)
        {
            SetStatus("Could not save that change.");
            return;
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Could not save that change.");
            return;
        }

        if (IsOpen)
        {
            Refresh(bringCurrentIntoView: false);
        }
        else
        {
            UpdateRail();
        }
    }

    private void OnQueueChanged(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(ApplyNextForQueue);

    private void ApplyNextForQueue()
    {
        if (PlayQueue.Count > 0)
        {
            NextButton.IsEnabled = true;
            return;
        }

        var list = _playlistId is null ? null : Playlists.Find(_playlistId);
        var run = CurrentRun();
        if (list is null || run is null || list.Videos.Count == 0)
        {
            NextButton.IsEnabled = false;
            return;
        }

        var keys = PlaylistRun.Keys(list);
        NextButton.IsEnabled = run.HasMove(
            keys,
            index => PlaylistRun.Include(list, index, _index, _unwatchedOnly),
            _index,
            forward: true);
    }

    private void QueueEntry(PlaylistEntry entry, IReadOnlyDictionary<string, string> names, bool next)
    {
        if (!entry.Resolve && !entry.IsPlayable)
        {
            SetStatus("That video is no longer on this PC.");
            return;
        }

        string? Name(string path) => names.TryGetValue(path, out var known) ? known : null;
        var queued = next ? PlayQueue.PlayNext(entry, Name) : PlayQueue.Add(entry, Name);
        if (queued is null)
        {
            SetStatus("That video could not be queued.");
            return;
        }

        if (next)
        {
            SetStatus($"\"{queued.Title}\" will play next.", InfoBarSeverity.Success);
        }
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
}
