using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;
using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class SavedWordsPage : Page
{
    private bool _compactWords;

    public SavedWordsPage()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyPageWidth(ActualWidth, rebuild: true);
        StatusBar.Closed += (_, _) => StatusBar.Visibility = Visibility.Collapsed;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (WordList is not null)
            {
                ShowWords();
            }
        });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        StatusBar.IsOpen = false;
        StatusBar.Visibility = Visibility.Collapsed;
        var saved = SearchSession.Recall(SearchSession.SavedWords);
        if (!string.Equals(SearchBox.Text, saved.Text, StringComparison.Ordinal))
        {
            SearchBox.Text = saved.Text;
        }

        ApplyPageWidth(ActualWidth, rebuild: false);
        ShowWords();
        if (!string.IsNullOrWhiteSpace(saved.Text))
        {
            SearchScroll.Restore(WordScroll, saved.Offset);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SearchSession.Remember(
            SearchSession.SavedWords,
            SearchBox.Text,
            WordScroll.VerticalOffset,
            WordScroll.ScrollableHeight > 0);
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || WordList is null)
        {
            return;
        }

        ShowWords();
    }

    private void ApplyPageWidth(double width, bool rebuild)
    {
        if (PageRoot is null)
        {
            return;
        }

        PageRoot.Padding = width > 0 && width < 720
            ? new Thickness(16, 8, 16, 16)
            : new Thickness(24, 8, 24, 24);
        var compact = width > 0 && width < 860;
        if (compact == _compactWords)
        {
            return;
        }

        _compactWords = compact;
        if (!rebuild || WordList is null || WordList.Children.Count == 0)
        {
            return;
        }

        var offset = WordScroll.VerticalOffset;
        ShowWords();
        if (offset > 0)
        {
            SearchScroll.Restore(WordScroll, offset);
        }
    }

    private void ShowWords()
    {
        WordList.Children.Clear();
        var all = SavedWords.All();
        var query = SearchBox.Text.Trim();
        var shown = query.Length == 0 ? all : all.Where(word => Matches(word, query)).ToArray();
        CountText.Text = query.Length == 0 ? CountLabel(all.Count) : $"{shown.Count} of {CountLabel(all.Count)}";
        if (shown.Count == 0)
        {
            ListHost.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            EmptyText.Text = all.Count == 0
                ? "You have not saved any words yet. Open a video or a stream, click a subtitle word, and press Save."
                : "No words match that search.";
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        ListHost.Visibility = Visibility.Visible;
        if (!_compactWords)
        {
            WordList.Children.Add(CreateColumnHeader());
        }

        var firstGroup = true;
        foreach (var group in shown
            .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key.Length == 0)
            .ThenByDescending(group => group.Max(SavedTicks)))
        {
            WordList.Children.Add(CreateHeader(group.First(), firstGroup));
            firstGroup = false;
            foreach (var word in group)
            {
                WordList.Children.Add(CreateRow(word));
            }
        }

        if (WordList.Children.OfType<Border>().LastOrDefault() is { } last)
        {
            last.BorderThickness = new Thickness(0);
        }
    }

    private Border CreateColumnHeader()
    {
        var row = RowGrid();
        row.Padding = new Thickness(16, 8, 8, 4);
        AddLabel(row, "Word", 0, header: true);
        AddLabel(row, "Turkish", 1, header: true);
        AddLabel(row, "Sentence", 2, header: true);
        var time = AddLabel(row, "Time", 3, header: true);
        time.HorizontalAlignment = HorizontalAlignment.Right;
        return new Border
        {
            BorderBrush = LineInk.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row
        };
    }

    private UIElement CreateHeader(SavedWord sample, bool first)
    {
        var title = SavedWords.SourceTitle(sample);
        var text = new TextBlock
        {
            Text = title,
            Margin = new Thickness(16, first ? 12 : 16, 16, 4),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
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
        var located = word.TimeMs is long && (!string.IsNullOrWhiteSpace(word.VideoPath) || !string.IsNullOrWhiteSpace(word.PageUrl));
        var remove = CreateRemoveButton(word);
        var row = _compactWords ? CardGrid(word, located, remove) : TableGrid(word, located, remove);
        WatchHover(row);
        ToolTipService.SetToolTip(row, Tip(word, located));
        if (SavedWords.CanOpen(word))
        {
            row.Tapped += (_, args) =>
            {
                if (args.OriginalSource is DependencyObject source && IsInside(source, remove))
                {
                    return;
                }

                args.Handled = true;
                OpenWord(word);
            };
        }

        return new Border
        {
            BorderBrush = LineInk.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row
        };
    }

    private Grid TableGrid(SavedWord word, bool located, Button remove)
    {
        var row = RowGrid();
        row.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AddLabel(row, word.English, 0, header: false, strong: true);
        AddLabel(row, word.Turkish, 1, header: false, strong: false);
        AddLabel(row, word.Sentence, 2, header: false, strong: false);
        if (located && word.TimeMs is long time)
        {
            var clock = AddLabel(row, FormatClock(time), 3, header: false, strong: false);
            clock.HorizontalAlignment = HorizontalAlignment.Right;
        }

        Grid.SetColumn(remove, 4);
        row.Children.Add(remove);
        return row;
    }

    private Grid CardGrid(SavedWord word, bool located, Button remove)
    {
        var text = new StackPanel { Spacing = 2, IsHitTestVisible = false };
        var title = string.IsNullOrWhiteSpace(word.Turkish) ? word.English : $"{word.English} — {word.Turkish}";
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = PrimaryInk.Foreground,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3
        });
        if (!string.IsNullOrWhiteSpace(word.Sentence) && !word.SentenceIsWord())
        {
            text.Children.Add(new TextBlock
            {
                Text = word.Sentence,
                FontSize = 14,
                Foreground = SecondaryInk.Foreground,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 4
            });
        }

        if (located && word.TimeMs is long time)
        {
            text.Children.Add(new TextBlock
            {
                Text = FormatClock(time),
                FontSize = 14,
                Foreground = SecondaryInk.Foreground
            });
        }

        remove.VerticalAlignment = VerticalAlignment.Top;
        var row = new Grid
        {
            Padding = new Thickness(12, 10, 12, 10),
            ColumnSpacing = 8,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(remove, 1);
        row.Children.Add(text);
        row.Children.Add(remove);
        return row;
    }

    private Button CreateRemoveButton(SavedWord word)
    {
        var remove = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon
            {
                Glyph = "\uE74D",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        if (Application.Current.Resources.TryGetValue("IconButtonStyle", out var style) && style is Style icon)
        {
            remove.Style = icon;
        }

        ToolTipService.SetToolTip(remove, "Remove this word");
        remove.Click += async (_, _) => await RemoveWordAsync(word);
        return remove;
    }

    private static Grid RowGrid()
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 10, 8, 10),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        return row;
    }

    private TextBlock AddLabel(Grid row, string text, int column, bool header, bool strong = false)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            FontWeight = header || strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = header || column != 0 ? SecondaryInk.Foreground : PrimaryInk.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };
        Grid.SetColumn(block, column);
        row.Children.Add(block);
        return block;
    }

    private void WatchHover(Grid host)
    {
        var rest = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var hover = HoverInk.Background;
        host.Background = rest;
        host.PointerEntered += (_, _) => host.Background = hover;
        host.PointerExited += (_, args) =>
        {
            var point = args.GetCurrentPoint(host).Position;
            if (point.X < 0 || point.Y < 0 || point.X >= host.ActualWidth || point.Y >= host.ActualHeight)
            {
                host.Background = rest;
            }
        };
    }

    private static string Tip(SavedWord word, bool located)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(word.English))
        {
            lines.Add(string.IsNullOrWhiteSpace(word.Turkish)
                ? word.English
                : $"{word.English} — {word.Turkish}");
        }

        if (!string.IsNullOrWhiteSpace(word.Sentence) && !word.SentenceIsWord())
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

    private async Task RemoveWordAsync(SavedWord word)
    {
        var dialog = new ContentDialog
        {
            Title = "Remove saved word?",
            Content = $"Remove \"{word.English}\" from this list?",
            PrimaryButtonText = "Remove",
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
            SavedWords.Remove(word);
        }
        catch (IOException)
        {
            ShowStatus("Could not remove that word.");
            return;
        }
        catch (UnauthorizedAccessException)
        {
            ShowStatus("Could not remove that word.");
            return;
        }

        ShowWords();
    }

    private void OpenWord(SavedWord word)
    {
        if (!string.IsNullOrWhiteSpace(word.PageUrl))
        {
            if (App.MainAppWindow is MainWindow window)
            {
                window.OpenSavedStream(word);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(word.VideoPath) || word.TimeMs is not long time)
        {
            return;
        }

        if (!File.Exists(word.VideoPath))
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Message = "That video is no longer on this PC.";
            StatusBar.Visibility = Visibility.Visible;
            StatusBar.IsOpen = true;
            return;
        }

        var item = App.MediaLibrary.GetItems().FirstOrDefault(media =>
                string.Equals(media.FilePath, word.VideoPath, StringComparison.OrdinalIgnoreCase))
            ?? new MediaItem
            {
                Id = word.VideoPath,
                Kind = MediaKind.Video,
                DisplayName = Path.GetFileName(word.VideoPath),
                FilePath = word.VideoPath,
                ImportedAt = new DateTimeOffset(File.GetCreationTimeUtc(word.VideoPath)),
                FileSizeBytes = new FileInfo(word.VideoPath).Length
            };
        NavigationHelper.OpenPlayer(new VideoOpenRequest(item, time, word));
    }

    private void ShowStatus(string message)
    {
        StatusBar.Severity = InfoBarSeverity.Error;
        StatusBar.Message = message;
        StatusBar.Visibility = Visibility.Visible;
        StatusBar.IsOpen = true;
    }

    private static bool Matches(SavedWord word, string query)
    {
        return Hit(word.English) || Hit(word.Turkish);

        bool Hit(string? value) => value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string GroupKey(SavedWord word)
        => !string.IsNullOrWhiteSpace(word.PageUrl)
            ? word.PageUrl.Trim()
            : string.IsNullOrWhiteSpace(word.VideoPath) ? string.Empty : word.VideoPath;

    private static long SavedTicks(SavedWord word)
        => DateTimeOffset.TryParse(word.SavedAt, out var saved) ? saved.UtcTicks : 0;

    private static string CountLabel(int count) => count == 1 ? "1 word" : $"{count} words";

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
