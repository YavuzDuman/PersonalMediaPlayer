using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class SavedWordsPanel : UserControl
{
    private readonly SolidColorBrush _clear = new(Microsoft.UI.Colors.Transparent);
    private SavedWord? _selected;
    private Border? _selectedHost;
    private string? _currentVideoPath;
    private string? _appliedKey;
    private double? _pendingOffset;

    public SavedWordsPanel()
    {
        InitializeComponent();
        Status.Closed += (_, _) => Status.Visibility = Visibility.Collapsed;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
    }

    internal event EventHandler<SavedWord>? WordChosen;

    public bool IsOpen => Visibility == Visibility.Visible;

    public string? CurrentVideoPath
    {
        get => _currentVideoPath;
        set
        {
            if (string.Equals(_currentVideoPath, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_appliedKey is not null)
            {
                SearchSession.Remember(
                    SearchSession.Words(_appliedKey),
                    Search.Text,
                    Scroller.VerticalOffset,
                    Scroller.ScrollableHeight > 0);
            }

            _currentVideoPath = value;
            _appliedKey = null;
        }
    }

    public void Remember()
    {
        if (string.IsNullOrWhiteSpace(_appliedKey))
        {
            return;
        }

        SearchSession.Remember(
            SearchSession.Words(_appliedKey),
            Search.Text,
            Scroller.VerticalOffset,
            Scroller.ScrollableHeight > 0);
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

    public void Open()
    {
        Visibility = Visibility.Visible;
        Refresh();
    }

    internal void Show(SavedWord word)
    {
        _selected = word;
        if (!string.IsNullOrWhiteSpace(_currentVideoPath))
        {
            SearchSession.Remember(SearchSession.Words(_currentVideoPath), string.Empty, 0);
        }

        _appliedKey = _currentVideoPath;
        _pendingOffset = null;
        Search.Text = string.Empty;
        Open();
    }

    public void Collapse()
    {
        Visibility = Visibility.Collapsed;
    }

    public void SetStatus(string? message, InfoBarSeverity severity = InfoBarSeverity.Warning)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Status.IsOpen = false;
            Status.Visibility = Visibility.Collapsed;
            return;
        }

        Status.Severity = severity;
        Status.Message = message;
        Status.Visibility = Visibility.Visible;
        Status.IsOpen = true;
    }

    public void Refresh()
    {
        if (!IsOpen || List is null)
        {
            return;
        }

        EnsureSearch();
        var offset = Scroller.VerticalOffset;
        var fromMemory = false;
        if (_pendingOffset is double remembered && remembered > 0)
        {
            offset = remembered;
            fromMemory = true;
        }

        _pendingOffset = null;
        List.Children.Clear();
        _selectedHost = null;
        var all = SavedWords.All();
        var query = Search.Text.Trim();
        var shown = query.Length == 0 ? all : all.Where(word => Matches(word, query)).ToArray();
        if (shown.Count == 0)
        {
            Scroller.Visibility = Visibility.Collapsed;
            Empty.Visibility = Visibility.Visible;
            Empty.Text = all.Count == 0
                ? "You have not saved any words yet. Click a subtitle word, then press Save."
                : "No words match that search.";
            return;
        }

        Empty.Visibility = Visibility.Collapsed;
        Scroller.Visibility = Visibility.Visible;
        var firstGroup = true;
        foreach (var group in shown
            .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key.Length == 0)
            .ThenBy(group => CurrentVideoPath is null
                || !string.Equals(group.Key, CurrentVideoPath, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(group => group.Max(SavedTicks)))
        {
            List.Children.Add(CreateHeader(group.First(), firstGroup));
            firstGroup = false;
            foreach (var word in group)
            {
                List.Children.Add(CreateRow(word));
            }
        }

        if (_selectedHost is Border selectedHost)
        {
            DispatcherQueue.TryEnqueue(() =>
                selectedHost.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.3 }));
        }
        else if (fromMemory)
        {
            SearchScroll.Restore(Scroller, offset);
        }
        else
        {
            Scroller.ChangeView(null, offset, null, true);
        }
    }

    private void EnsureSearch()
    {
        if (string.Equals(_appliedKey, _currentVideoPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_appliedKey is not null)
        {
            SearchSession.Remember(
                SearchSession.Words(_appliedKey),
                Search.Text,
                Scroller.VerticalOffset,
                Scroller.ScrollableHeight > 0);
        }

        _appliedKey = _currentVideoPath;
        if (string.IsNullOrWhiteSpace(_currentVideoPath))
        {
            _pendingOffset = null;
            return;
        }

        var saved = SearchSession.Recall(SearchSession.Words(_currentVideoPath));
        _pendingOffset = string.IsNullOrWhiteSpace(saved.Text) ? null : saved.Offset;
        if (!string.Equals(Search.Text, saved.Text, StringComparison.Ordinal))
        {
            Search.Text = saved.Text;
        }
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || List is null)
        {
            return;
        }

        Refresh();
    }

    private TextBlock CreateHeader(SavedWord sample, bool first)
    {
        var title = SavedWords.SourceTitle(sample);
        var text = new TextBlock
        {
            Text = title,
            Margin = new Thickness(8, first ? 4 : 12, 8, 2),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = PrimaryInk.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var place = sample.PageUrl ?? sample.VideoPath;
        if (!string.IsNullOrWhiteSpace(place))
        {
            ToolTipService.SetToolTip(text, place);
        }

        return text;
    }

    private Border CreateRow(SavedWord word)
    {
        var selected = _selected is not null && Same(word, _selected);
        var located = word.TimeMs is long && (!string.IsNullOrWhiteSpace(word.VideoPath) || !string.IsNullOrWhiteSpace(word.PageUrl));
        var body = new Grid
        {
            Padding = new Thickness(8, 6, 8, 6),
            ColumnSpacing = 8,
            Background = _clear
        };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var english = new TextBlock
        {
            Text = word.English,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = PrimaryInk.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };
        var turkish = new TextBlock
        {
            Text = word.Turkish,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = PrimaryInk.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };
        body.Children.Add(english);
        if (!string.IsNullOrWhiteSpace(word.Turkish))
        {
            Grid.SetRow(turkish, 1);
            Grid.SetColumnSpan(turkish, 2);
            body.Children.Add(turkish);
        }
        if (located && word.TimeMs is long time)
        {
            var clock = new TextBlock
            {
                Text = FormatClock(time),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = SecondaryInk.Foreground,
                IsHitTestVisible = false
            };
            Grid.SetColumn(clock, 1);
            body.Children.Add(clock);
        }

        var accent = new Border
        {
            Width = 3,
            Background = selected ? AccentInk.Background : _clear
        };
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(body, 1);
        shell.Children.Add(accent);
        shell.Children.Add(body);

        var host = new Border
        {
            Tag = new RowMark(word, accent),
            Background = selected ? HoverInk.Background : _clear,
            BorderBrush = LineInk.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = shell
        };
        ToolTipService.SetToolTip(host, Tip(word, located));
        WatchHover(host, accent, word);
        if (SavedWords.CanOpen(word))
        {
            host.Tapped += (_, args) =>
            {
                args.Handled = true;
                Choose(word, host);
            };
        }

        if (selected)
        {
            _selectedHost = host;
        }

        return host;
    }

    private void Choose(SavedWord word, Border host)
    {
        _selected = word;
        _selectedHost = host;
        foreach (var row in List.Children.OfType<Border>())
        {
            if (row.Tag is RowMark mark)
            {
                Paint(row, mark.Accent, Same(mark.Word, word), over: ReferenceEquals(row, host));
            }
        }

        WordChosen?.Invoke(this, word);
    }

    private void WatchHover(Border host, Border accent, SavedWord word)
    {
        host.PointerEntered += (_, _) => Paint(host, accent, Selected(word), over: true);
        host.PointerExited += (_, args) =>
        {
            var point = args.GetCurrentPoint(host).Position;
            if (point.X < 0 || point.Y < 0 || point.X >= host.ActualWidth || point.Y >= host.ActualHeight)
            {
                Paint(host, accent, Selected(word), over: false);
            }
        };
    }

    private void Paint(Border host, Border accent, bool selected, bool over)
    {
        host.Background = selected || over ? HoverInk.Background : _clear;
        accent.Background = selected ? AccentInk.Background : _clear;
    }

    private bool Selected(SavedWord word) => _selected is not null && Same(word, _selected);

    private static string Tip(SavedWord word, bool located)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(word.English))
        {
            lines.Add(string.IsNullOrWhiteSpace(word.Turkish)
                ? word.English
                : $"{word.English} — {word.Turkish}");
        }

        if (!string.IsNullOrWhiteSpace(word.Sentence))
        {
            lines.Add(word.Sentence);
        }

        if (!located)
        {
            lines.Add("Saved before a video moment was remembered.");
        }
        else if (!string.IsNullOrWhiteSpace(word.VideoPath) && string.IsNullOrWhiteSpace(word.PageUrl) && !File.Exists(word.VideoPath))
        {
            lines.Add("This video is no longer on this PC.");
        }
        else if (word.TimeMs is long time)
        {
            lines.Add($"Open at {FormatClock(time)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool Matches(SavedWord word, string query)
    {
        return Hit(word.English) || Hit(word.Turkish);

        bool Hit(string? value) => value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Same(SavedWord item, SavedWord word)
    {
        return string.Equals(item.English, word.English, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Sentence, word.Sentence, StringComparison.Ordinal)
            && string.Equals(item.VideoPath?.Trim(), word.VideoPath?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.PageUrl?.Trim(), word.PageUrl?.Trim(), StringComparison.OrdinalIgnoreCase)
            && item.TimeMs == word.TimeMs;
    }

    private static string GroupKey(SavedWord word)
        => !string.IsNullOrWhiteSpace(word.PageUrl)
            ? word.PageUrl.Trim()
            : string.IsNullOrWhiteSpace(word.VideoPath) ? string.Empty : word.VideoPath;

    private static long SavedTicks(SavedWord word)
        => DateTimeOffset.TryParse(word.SavedAt, out var saved) ? saved.UtcTicks : 0;

    private sealed class RowMark(SavedWord word, Border accent)
    {
        public SavedWord Word { get; } = word;

        public Border Accent { get; } = accent;
    }

    private static string FormatClock(long durationMs)
    {
        if (durationMs < 0)
        {
            return "00:00";
        }

        var totalSeconds = durationMs / 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }
}
