using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.Playback;
using Windows.ApplicationModel.DataTransfer;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class QueuePanel : UserControl
{
    private readonly SolidColorBrush _clear = new(Microsoft.UI.Colors.Transparent);
    private readonly Button _undoButton;
    private int? _dragFrom;
    private bool _showingUndo;
    private bool _saveMessage;
    private bool _savingPlaylist;
    private int _closeSuppress;

    public QueuePanel()
    {
        InitializeComponent();
        _undoButton = new Button { Content = "Undo" };
        _undoButton.Click += (_, _) => PlayQueue.Undo();
        Status.Closed += Status_Closed;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => Bind(PlayQueue.Snapshot()));
    }

    internal event EventHandler? NextChosen;

    internal void Bind(IReadOnlyList<PlayQueueItem> items)
    {
        _dragFrom = null;
        var offset = Scroller.VerticalOffset;
        CountText.Text = items.Count == 1 ? "1 video" : $"{items.Count} videos";
        NextButton.IsEnabled = items.Count > 0;
        ClearButton.IsEnabled = items.Count > 0;
        SavePlaylistButton.IsEnabled = items.Count > 0;
        Rows.Children.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            Rows.Children.Add(CreateRow(items[i], i));
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Scroller.UpdateLayout();
            Scroller.ChangeView(null, offset, null, true);
        });
        ShowUndo();
    }

    private void Next_Click(object sender, RoutedEventArgs e) => NextChosen?.Invoke(this, EventArgs.Empty);

    private void Clear_Click(object sender, RoutedEventArgs e) => PlayQueue.ClearWaiting();

    private async void SavePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_savingPlaylist || XamlRoot is null || PlayQueue.Count == 0)
        {
            return;
        }

        var box = new TextBox
        {
            Text = Playlists.NextQueueName(),
            PlaceholderText = "Playlist name",
            MaxLength = 80
        };
        box.Loaded += (_, _) =>
        {
            box.SelectAll();
            box.Focus(FocusState.Programmatic);
        };
        var error = new TextBlock
        {
            Foreground = SecondaryInk.Foreground,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var dialog = new ContentDialog
        {
            Title = "Save as playlist",
            Content = new StackPanel
            {
                Spacing = 8,
                Width = 320,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Name the new playlist. The videos in the queue are copied when you save, and the queue stays.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = SecondaryInk.Foreground
                    },
                    box,
                    error
                }
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        dialog.PrimaryButtonClick += (sender, args) =>
        {
            var problem = Playlists.ReadQueueName(box.Text, out _);
            if (problem == QueuePlaylistProblem.None)
            {
                error.Visibility = Visibility.Collapsed;
                return;
            }

            args.Cancel = true;
            error.Text = ProblemText(problem);
            error.Visibility = Visibility.Visible;
        };

        _savingPlaylist = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            QueuePlaylistResult result;
            try
            {
                result = Playlists.SaveFromQueue(box.Text, PlayQueue.Snapshot());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowSaveStatus("Could not save the playlist.", InfoBarSeverity.Error);
                return;
            }

            ShowSaveStatus(
                result.Problem == QueuePlaylistProblem.None ? SavedText(result) : ProblemText(result.Problem),
                result.Problem == QueuePlaylistProblem.None ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
        }
        finally
        {
            _savingPlaylist = false;
        }
    }

    private static string ProblemText(QueuePlaylistProblem problem) => problem switch
    {
        QueuePlaylistProblem.BlankName => "Enter a name for the playlist.",
        QueuePlaylistProblem.LongName => "Use a name of 80 characters or fewer.",
        QueuePlaylistProblem.NameTaken => "You already have a playlist with that name.",
        QueuePlaylistProblem.NothingToSave => "The queue has no videos to save.",
        _ => "Could not save the playlist."
    };

    private static string SavedText(QueuePlaylistResult result)
    {
        var name = result.Playlist?.Name ?? "the playlist";
        var count = result.Saved == 1 ? "Saved 1 video" : $"Saved {result.Saved} videos";
        var text = $"{count} as \"{name}\". The waiting queue is still here.";
        if (result.Repeated > 0)
        {
            text += " A video that was queued more than once is listed once.";
        }

        return text;
    }

    private void ShowSaveStatus(string message, InfoBarSeverity severity)
    {
        _saveMessage = true;
        _showingUndo = false;
        Status.Severity = severity;
        Status.Message = message;
        Status.ActionButton = null;
        Status.Visibility = Visibility.Visible;
        Status.IsOpen = true;
    }

    private void ShowUndo()
    {
        if (!PlayQueue.IsPending(out var message))
        {
            if (_showingUndo)
            {
                _showingUndo = false;
                HideStatus();
            }

            return;
        }

        _saveMessage = false;
        _showingUndo = true;
        Status.Severity = InfoBarSeverity.Informational;
        Status.Message = message;
        Status.ActionButton = _undoButton;
        Status.Visibility = Visibility.Visible;
        Status.IsOpen = true;
    }

    private void Status_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        Status.Visibility = Visibility.Collapsed;
        if (_closeSuppress > 0)
        {
            _closeSuppress--;
            return;
        }

        if (_saveMessage)
        {
            _saveMessage = false;
            Status.ActionButton = null;
            if (PlayQueue.IsPending(out _))
            {
                ShowUndo();
            }

            return;
        }

        if (_showingUndo)
        {
            _showingUndo = false;
            Status.ActionButton = null;
            PlayQueue.Dismiss();
        }
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

    private Border CreateRow(PlayQueueItem item, int index)
    {
        var title = new TextBlock
        {
            Text = item.Title,
            Foreground = PrimaryInk.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            IsHitTestVisible = false
        };
        var place = new TextBlock
        {
            Text = item.SourceLabel,
            FontSize = 12,
            Foreground = SecondaryInk.Foreground,
            IsHitTestVisible = false
        };
        var body = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Children = { title, place }
        };
        var remove = new Button
        {
            Content = "Remove",
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 4, 10, 4)
        };
        ToolTipService.SetToolTip(remove, "Remove from queue");
        var id = item.Id;
        remove.Click += (_, _) => PlayQueue.Remove(id);

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
        var rowIndex = index;
        grip.DragStarting += (_, args) =>
        {
            _dragFrom = rowIndex;
            args.AllowedOperations = DataPackageOperation.Move;
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.SetText(rowIndex.ToString());
        };
        grip.DropCompleted += (_, _) => _dragFrom = null;

        var row = new Grid
        {
            Padding = new Thickness(0, 8, 0, 8),
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = new TextBlock
        {
            Text = (index + 1).ToString(),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SecondaryInk.Foreground,
            IsHitTestVisible = false
        };
        Grid.SetColumn(number, 1);
        Grid.SetColumn(body, 2);
        Grid.SetColumn(remove, 3);
        row.Children.Add(grip);
        row.Children.Add(number);
        row.Children.Add(body);
        row.Children.Add(remove);

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
            Background = _clear,
            BorderBrush = LineInk.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = surface
        };
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
            PlayQueue.Move(from, destination);
        };
        return host;
    }
}
