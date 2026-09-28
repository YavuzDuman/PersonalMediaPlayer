using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.Subtitles;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class HoverCaptions : UserControl
{
    private IReadOnlyList<SubtitleCue> _cues = [];
    private string[] _line = [];
    private int _cueIndex = -1;
    private bool _shown = true;

    public HoverCaptions()
    {
        InitializeComponent();
    }

    public bool HasCues => _cues.Count > 0;

    public void Load(string? mediaPath)
    {
        _cues = string.IsNullOrWhiteSpace(mediaPath) ? [] : SubtitleCues.LoadFor(mediaPath);
        _cueIndex = -1;
        Line.Children.Clear();
        HideMeaning();
        SetTime(0);
    }

    public void SetShown(bool shown)
    {
        _shown = shown;
        Line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            HideMeaning();
        }
    }

    public void SetTime(long milliseconds)
    {
        if (!_shown)
        {
            return;
        }

        var index = -1;
        for (var i = 0; i < _cues.Count; i++)
        {
            if (milliseconds >= _cues[i].StartMs && milliseconds < _cues[i].EndMs)
            {
                index = i;
                break;
            }
        }

        if (index == _cueIndex)
        {
            return;
        }

        _cueIndex = index;
        HideMeaning();
        Line.Children.Clear();
        _line = [];
        if (index < 0)
        {
            return;
        }

        _line = _cues[index].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var wordIndex = 0; wordIndex < _line.Length; wordIndex++)
        {
            var text = new TextBlock
            {
                Text = _line[wordIndex],
                Tag = wordIndex,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(3, 0, 3, 0),
                IsHitTestVisible = true
            };
            text.PointerEntered += Word_Entered;
            text.PointerExited += Word_Exited;
            Line.Children.Add(text);
        }

        Line.IsHitTestVisible = Line.Children.Count > 0;
    }

    private void Word_Entered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement word)
        {
            return;
        }

        var text = word as TextBlock;
        var wordIndex = text?.Tag is int index ? index : -1;
        var meaning = TurkishDictionary.Lookup(text?.Text ?? string.Empty, _line, wordIndex);
        if (meaning is null)
        {
            HideMeaning();
            return;
        }

        MeaningText.Text = meaning;
        MeaningCard.Visibility = Visibility.Visible;
        MeaningCard.Measure(new Windows.Foundation.Size(320, 400));
        var size = MeaningCard.DesiredSize;
        var origin = word.TransformToVisual(Line).TransformPoint(new Windows.Foundation.Point(0, 0));
        Canvas.SetLeft(MeaningCard, origin.X + (word.ActualWidth - size.Width) / 2);
        Canvas.SetTop(MeaningCard, origin.Y - size.Height - 8);
    }

    private void Word_Exited(object sender, PointerRoutedEventArgs e) => HideMeaning();

    private void HideMeaning() => MeaningCard.Visibility = Visibility.Collapsed;
}
