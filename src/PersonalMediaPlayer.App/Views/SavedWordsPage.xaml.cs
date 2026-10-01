using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Subtitles;
using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class SavedWordsPage : Page
{
    public SavedWordsPage()
    {
        InitializeComponent();
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
        ShowWords();
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || WordList is null)
        {
            return;
        }

        ShowWords();
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
        WordList.Children.Add(CreateColumnHeader());
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
            Margin = new Thickness(16, first ? 10 : 14, 16, 2),
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
        var located = word.TimeMs is long && (!string.IsNullOrWhiteSpace(word.VideoPath) || !string.IsNullOrWhiteSpace(word.PageUrl));
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

        var remove = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = "\uE74D",
                FontSize = 12,
                Foreground = PrimaryInk.Foreground
            }
        };
        ToolTipService.SetToolTip(remove, "Remove this word");
        remove.Click += async (_, _) => await RemoveWordAsync(word);
        Grid.SetColumn(remove, 4);
        row.Children.Add(remove);

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

    private static Grid RowGrid()
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 5, 8, 5),
            ColumnSpacing = 12
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        return row;
    }

    private TextBlock AddLabel(Grid row, string text, int column, bool header, bool strong = false)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = header ? 12 : 14,
            FontWeight = header || strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = header || column == 3 ? SecondaryInk.Foreground : PrimaryInk.Foreground,
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
        Frame.Navigate(typeof(VideoPlayerPage), new VideoOpenRequest(item, time, word));
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
