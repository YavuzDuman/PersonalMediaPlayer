using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.ViewModels;
using PersonalMediaPlayer.Core.Storage;

namespace PersonalMediaPlayer.App.Views;

internal static class LocateFolderDialog
{
    public static async Task<IReadOnlyList<(string CurrentPath, string NewPath)>?> ShowAsync(
        XamlRoot root,
        string folder,
        IReadOnlyList<LinkMatch> matches,
        int skipped)
    {
        var rows = new List<Row>();
        var list = new StackPanel { Spacing = 14, Width = 480 };
        list.Children.Add(new TextBlock
        {
            Text = Lead(matches, skipped),
            TextWrapping = TextWrapping.Wrap
        });

        foreach (var match in matches)
        {
            list.Children.Add(BuildRow(folder, match, rows));
        }

        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Secondary()
        };
        list.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = $"Locate in {ConnectedPcFolder.LabelOf(folder)}?",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = list
            },
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root
        };

        void UpdateApply()
        {
            var chosen = Chosen(rows);
            var duplicate = chosen
                .GroupBy(item => item.NewPath, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            error.Text = duplicate ? "Each file in the new folder can only be used once." : string.Empty;
            dialog.IsPrimaryButtonEnabled = chosen.Count > 0 && !duplicate;
        }

        foreach (var row in rows)
        {
            row.Changed = UpdateApply;
        }

        UpdateApply();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return Chosen(rows);
    }

    private static string Lead(IReadOnlyList<LinkMatch> matches, int skipped)
    {
        var ready = matches.Count(match => match.State == LinkMatchState.Ready);
        var choose = matches.Count(match => match.State == LinkMatchState.Choose);
        var none = matches.Count(match => match.State == LinkMatchState.None);
        var parts = new List<string>
        {
            "Review these matches before they are applied. A file matches when its name appears once, or when one path repeats the old folders. Choose a file when more than one could fit."
        };
        if (ready > 0 || choose > 0 || none > 0)
        {
            parts.Add($"{ready} ready, {choose} to choose, {none} not found.");
        }

        if (skipped == 1)
        {
            parts.Add("1 selected file is not missing, so it stays as it is.");
        }
        else if (skipped > 1)
        {
            parts.Add($"{skipped} selected files are not missing, so they stay as they are.");
        }

        return string.Join(" ", parts);
    }

    private static FrameworkElement BuildRow(string folder, LinkMatch match, List<Row> rows)
    {
        var body = new StackPanel { Spacing = 2 };
        var row = new Row(match.CurrentPath);
        rows.Add(row);
        if (match.State == LinkMatchState.Ready && match.ChosenPath is not null)
        {
            row.Path = match.ChosenPath;
            row.Include = true;
            var box = new CheckBox
            {
                Content = match.FileName,
                IsChecked = true
            };
            box.Checked += (_, _) =>
            {
                row.Include = true;
                row.Changed?.Invoke();
            };
            box.Unchecked += (_, _) =>
            {
                row.Include = false;
                row.Changed?.Invoke();
            };
            body.Children.Add(box);
            body.Children.Add(Note($"Use {Display(folder, match.ChosenPath)}"));
        }
        else if (match.State == LinkMatchState.Choose)
        {
            body.Children.Add(Name(match.FileName));
            body.Children.Add(Note("More than one file could fit. Choose one."));
            var combo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "Choose a file",
                ItemsSource = match.Candidates.Select(path => new Candidate(path, Display(folder, path))).ToArray()
            };
            combo.DisplayMemberPath = nameof(Candidate.Label);
            combo.SelectionChanged += (_, _) =>
            {
                row.Path = combo.SelectedItem is Candidate candidate ? candidate.Path : null;
                row.Include = row.Path is not null;
                row.Changed?.Invoke();
            };
            body.Children.Add(combo);
        }
        else
        {
            body.Children.Add(Name(match.FileName));
            body.Children.Add(Note("No file with this name in that folder."));
        }

        body.Children.Add(Note($"Was in {Path.GetDirectoryName(match.CurrentPath)}"));
        ToolTipService.SetToolTip(body, match.CurrentPath);
        return body;
    }

    private static TextBlock Name(string text)
        => new() { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Note(string text)
        => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Secondary()
        };

    private static Brush? Secondary()
    {
        if (Application.Current?.Resources is not ResourceDictionary resources
            || !resources.TryGetValue("TextFillColorSecondaryBrush", out var brush))
        {
            return null;
        }

        return brush as Brush;
    }

    private static string Display(string folder, string path)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full[root.Length..] : full;
    }

    private static List<(string CurrentPath, string NewPath)> Chosen(IEnumerable<Row> rows)
        => rows.Where(row => row.Include && !string.IsNullOrWhiteSpace(row.Path))
            .Select(row => (row.CurrentPath, row.Path!))
            .ToList();

    private sealed class Row
    {
        public Row(string currentPath) => CurrentPath = currentPath;

        public string CurrentPath { get; }

        public string? Path { get; set; }

        public bool Include { get; set; }

        public Action? Changed { get; set; }
    }

    private sealed class Candidate
    {
        public Candidate(string path, string label)
        {
            Path = path;
            Label = label;
        }

        public string Path { get; }

        public string Label { get; }
    }
}
