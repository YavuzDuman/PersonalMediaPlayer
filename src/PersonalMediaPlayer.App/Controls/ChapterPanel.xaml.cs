using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.Playback;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class ChapterPanel : UserControl
{
    private static readonly SolidColorBrush ClearBrush = new(Colors.Transparent);

    private readonly List<Button> _rows = [];
    private IReadOnlyList<VideoChapter> _items = [];
    private int _current = -1;

    public ChapterPanel()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => Bind(_items));
    }

    internal event EventHandler<ChapterJumpEventArgs>? ChapterChosen;

    internal void Bind(IReadOnlyList<VideoChapter> chapters)
    {
        _items = chapters;
        var selected = _current;
        var offset = Scroller.VerticalOffset;
        CountText.Text = chapters.Count == 1 ? "1 chapter" : $"{chapters.Count} chapters";
        Rows.Children.Clear();
        _rows.Clear();
        _current = -1;
        foreach (var chapter in chapters)
        {
            Rows.Children.Add(CreateRow(chapter));
        }

        Highlight(selected);
        DispatcherQueue.TryEnqueue(() =>
        {
            Scroller.UpdateLayout();
            Scroller.ChangeView(null, offset, null, true);
            if (_current >= 0 && _current < _rows.Count)
            {
                _rows[_current].StartBringIntoView();
            }
        });
    }

    internal void Highlight(int index, bool force = false)
    {
        if (!force && index == _current)
        {
            return;
        }

        if (_current >= 0 && _current < _rows.Count && _current != index)
        {
            Paint(_rows[_current], false);
        }

        _current = index;
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        Paint(_rows[index], true);
        _rows[index].StartBringIntoView();
    }

    private Button CreateRow(VideoChapter chapter)
    {
        var time = new TextBlock
        {
            Text = VideoChapters.Format(chapter.StartMs),
            Width = 72,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SecondaryInk.Foreground
        };
        var title = new TextBlock
        {
            Text = chapter.Title,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = PrimaryInk.Foreground,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var body = new Grid { ColumnSpacing = 8 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(title, 1);
        body.Children.Add(time);
        body.Children.Add(title);
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = ClearBrush,
            BorderBrush = ClearBrush,
            Content = body,
            Tag = chapter.StartMs
        };
        ToolTipService.SetToolTip(button, "Jump to this chapter");
        button.Click += (_, _) =>
        {
            if (button.Tag is long start)
            {
                ChapterChosen?.Invoke(this, new ChapterJumpEventArgs(start));
            }
        };
        _rows.Add(button);
        Paint(button, false);
        return button;
    }

    private void Paint(Button row, bool current)
    {
        row.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
        row.Background = current ? CurrentInk.Background : ClearBrush;
        row.BorderBrush = current ? MarkInk.BorderBrush : ClearBrush;
    }
}

internal sealed class ChapterJumpEventArgs : EventArgs
{
    public ChapterJumpEventArgs(long startMs) => StartMs = startMs;

    public long StartMs { get; }
}
