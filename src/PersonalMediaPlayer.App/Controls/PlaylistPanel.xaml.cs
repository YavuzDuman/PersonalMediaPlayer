using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.Playback;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class PlaylistPanel : UserControl
{
    private readonly SolidColorBrush _clear = new(Microsoft.UI.Colors.Transparent);
    private string? _playlistId;
    private int _index = -1;
    private bool _loading;
    private Border? _currentHost;

    public PlaylistPanel()
    {
        InitializeComponent();
        Status.Closed += (_, _) => Status.Visibility = Visibility.Collapsed;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
    }

    internal event EventHandler<int>? VideoChosen;

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

    private void Refresh()
    {
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
            List.Children.Add(CreateRow(list.Videos[i], i, names));
        }

        if (_currentHost is Border current)
        {
            DispatcherQueue.TryEnqueue(() =>
                current.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.3 }));
        }
    }

    private Border CreateRow(string path, int index, IReadOnlyDictionary<string, string> names)
    {
        var current = index == _index;
        var exists = File.Exists(path);
        var title = names.TryGetValue(path, out var known) ? known : Path.GetFileName(path);
        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = exists ? PrimaryInk.Foreground : SecondaryInk.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        });
        if (!exists)
        {
            body.Children.Add(new TextBlock
            {
                Text = "Not on this PC",
                FontSize = 12,
                Foreground = SecondaryInk.Foreground,
                IsHitTestVisible = false
            });
        }

        var number = new TextBlock
        {
            Text = $"{index + 1}",
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SecondaryInk.Foreground,
            IsHitTestVisible = false
        };
        var row = new Grid
        {
            Padding = new Thickness(8, 8, 8, 8),
            ColumnSpacing = 8,
            Background = current ? HoverInk.Background : _clear
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(body, 1);
        row.Children.Add(number);
        row.Children.Add(body);

        var accent = new Border
        {
            Width = 3,
            Background = current ? AccentInk.Background : _clear
        };
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(row, 1);
        shell.Children.Add(accent);
        shell.Children.Add(row);

        var host = new Border
        {
            Background = _clear,
            BorderBrush = LineInk.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = shell
        };
        ToolTipService.SetToolTip(host, exists ? title : $"{title}{Environment.NewLine}Not on this PC");
        var chosen = index;
        host.Tapped += (_, args) =>
        {
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

    private static int Neighbor(Playlist list, int index, int delta)
    {
        for (var i = index + delta; i >= 0 && i < list.Videos.Count; i += delta)
        {
            if (File.Exists(list.Videos[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
