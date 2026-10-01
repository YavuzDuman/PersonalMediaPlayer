using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.Subtitles;
using Windows.Foundation;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class HoverCaptions : UserControl
{
    private const string MissingMeaning = "This word is not in the word list yet.";

    private readonly PointerEventHandler _rootPressed;
    private InputSystemCursor? _hand;
    private IReadOnlyList<SubtitleCue> _cues = [];
    private string[] _line = [];
    private int _cueIndex = -1;
    private bool _shown = true;
    private string? _mediaPath;
    private string? _pageUrl;
    private bool _resolvePage;
    private string? _sourceName;
    private string? _cardVideoPath;
    private string? _cardPageUrl;
    private bool _cardResolve;
    private string? _cardSourceName;
    private string? _audioLanguage;
    private string? _captionLanguage;
    private string? _cardAudioLanguage;
    private string? _cardCaptionLanguage;
    private long? _cardTimeMs;
    private UIElement? _root;
    private long _shownTime;
    private string? _savedEnglish;
    private string? _savedSentence;
    private long? _savedTimeMs;
    private readonly SolidColorBrush _captionBrush = new(Microsoft.UI.Colors.White);
    private readonly SolidColorBrush _clearBrush = new(Microsoft.UI.Colors.Transparent);
    private readonly SolidColorBrush _markBrush = new(Windows.UI.Color.FromArgb(255, 255, 214, 10));
    private readonly SolidColorBrush _markTextBrush = new(Windows.UI.Color.FromArgb(255, 28, 28, 28));

    public event EventHandler? WordSaved;

    public HoverCaptions()
    {
        InitializeComponent();
        _rootPressed = RootPressed;
        Unloaded += (_, _) => CloseCard();
    }

    public bool HasCues => _cues.Count > 0;

    public void Load(string? mediaPath)
    {
        _mediaPath = string.IsNullOrWhiteSpace(mediaPath) ? null : mediaPath;
        _pageUrl = null;
        _resolvePage = false;
        _sourceName = null;
        _audioLanguage = null;
        _captionLanguage = null;
        _cues = _mediaPath is null ? [] : SubtitleCues.LoadFor(_mediaPath);
        _cueIndex = -1;
        Line.Children.Clear();
        HideMeaning();
        CloseCard();
        SetTime(0);
    }

    internal void SetStreamSource(string? pageUrl, bool resolve, string? sourceName)
    {
        _pageUrl = string.IsNullOrWhiteSpace(pageUrl) ? null : pageUrl.Trim();
        _resolvePage = _pageUrl is not null && resolve;
        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName.Trim();
        if (_sourceName is { Length: > 120 })
        {
            _sourceName = _sourceName[..120].Trim();
        }
    }

    internal void RememberChoice(string? audioLanguage, string? captionLanguage)
    {
        _audioLanguage = string.IsNullOrWhiteSpace(audioLanguage) ? null : audioLanguage.Trim();
        _captionLanguage = string.IsNullOrWhiteSpace(captionLanguage) ? null : captionLanguage.Trim();
    }

    internal void LoadCues(IReadOnlyList<SubtitleCue> cues)
    {
        _mediaPath = null;
        _cues = cues;
        _cueIndex = -1;
        Line.Children.Clear();
        HideMeaning();
        CloseCard();
        SetTime(0);
    }

    public void SetShown(bool shown)
    {
        _shown = shown;
        Line.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            HideMeaning();
            CloseCard();
            return;
        }

        var time = _shownTime;
        _cueIndex = -1;
        SetTime(time);
    }

    public void ShowSavedWord(string? english, string? sentence, long? timeMs, bool redraw = true)
    {
        _savedEnglish = string.IsNullOrWhiteSpace(english) ? null : english.Trim();
        _savedSentence = string.IsNullOrWhiteSpace(sentence) ? null : sentence.Trim();
        _savedTimeMs = timeMs;
        if (!redraw)
        {
            return;
        }

        CloseCard();
        var time = _shownTime;
        _cueIndex = -1;
        SetTime(time);
    }

    public void ClearSavedWord() => ShowSavedWord(null, null, null);

    public void SetTime(long milliseconds)
    {
        _shownTime = milliseconds;
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
        ProtectedCursor = null;
        Line.Children.Clear();
        _line = [];
        if (index < 0)
        {
            return;
        }

        _line = _cues[index].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var marked = HighlightedIndexes(index);
        for (var wordIndex = 0; wordIndex < _line.Length; wordIndex++)
        {
            var highlight = marked.Contains(wordIndex);
            var word = new TextBlock
            {
                Text = _line[wordIndex],
                Foreground = highlight ? _markTextBrush : _captionBrush,
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                IsHitTestVisible = false
            };
            var host = new Border
            {
                Tag = wordIndex,
                Margin = new Thickness(3, 0, 3, 0),
                Padding = highlight ? new Thickness(4, 0, 4, 0) : new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Background = highlight ? _markBrush : _clearBrush,
                Child = word,
                IsHitTestVisible = true,
                IsTapEnabled = true
            };
            host.PointerEntered += Word_Entered;
            host.PointerExited += Word_Exited;
            host.Tapped += Word_Tapped;
            Line.Children.Add(host);
        }

        Line.IsHitTestVisible = Line.Children.Count > 0;
    }

    private void Word_Entered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement word || word.Tag is not int wordIndex || wordIndex < 0 || wordIndex >= _line.Length)
        {
            return;
        }

        ProtectedCursor = _hand ??= InputSystemCursor.Create(InputSystemCursorShape.Hand);
        var meaning = TurkishDictionary.Lookup(_line[wordIndex], _line, wordIndex);
        if (meaning is null)
        {
            HideMeaning();
            return;
        }

        MeaningText.Text = meaning;
        MeaningCard.Visibility = Visibility.Visible;
        MeaningCard.Measure(new Size(320, 400));
        var size = MeaningCard.DesiredSize;
        var origin = word.TransformToVisual(MeaningLayer).TransformPoint(new Point(0, 0));
        Canvas.SetLeft(MeaningCard, origin.X + (word.ActualWidth - size.Width) / 2);
        Canvas.SetTop(MeaningCard, origin.Y - size.Height - 8);
    }

    private void Word_Exited(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = null;
        HideMeaning();
    }

    private void Word_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement word || word.Tag is not int wordIndex || wordIndex < 0 || wordIndex >= _line.Length || _cueIndex < 0 || _cueIndex >= _cues.Count)
        {
            return;
        }

        e.Handled = true;
        var english = EnglishFor(wordIndex);
        if (english.Length == 0)
        {
            english = _line[wordIndex].Trim();
        }

        if (english.Length == 0)
        {
            return;
        }

        var sentence = _cues[_cueIndex].Text.Trim();
        var canSave = _mediaPath is not null || _pageUrl is not null;
        _cardVideoPath = _mediaPath;
        _cardPageUrl = _mediaPath is null ? _pageUrl : null;
        _cardResolve = _cardPageUrl is not null && _resolvePage;
        _cardSourceName = _cardPageUrl is null ? null : _sourceName;
        _cardAudioLanguage = canSave ? _audioLanguage : null;
        _cardCaptionLanguage = canSave ? _captionLanguage : null;
        _cardTimeMs = canSave ? _cues[_cueIndex].StartMs : null;
        CardWord.Text = english;
        CardMeaning.Text = TurkishDictionary.Lookup(_line[wordIndex], _line, wordIndex) ?? MissingMeaning;
        CardSentence.Text = sentence;
        if (!canSave)
        {
            SaveWordButton.IsEnabled = false;
            SaveWordButton.Content = "Save";
            ToolTipService.SetToolTip(SaveWordButton, "This caption is not attached to a video.");
        }
        else
        {
            var saved = SavedChoice(english, sentence);
            SaveWordButton.IsEnabled = !saved;
            SaveWordButton.Content = saved ? "Saved" : "Save";
            ToolTipService.SetToolTip(SaveWordButton, saved ? "Saved on this computer" : "Save this word on this computer");
        }
        Dismiss.Visibility = Visibility.Visible;
        WordCard.Visibility = Visibility.Visible;
        HookRoot();
    }

    private void CloseCard_Click(object sender, RoutedEventArgs e) => CloseCard();

    private void Dismiss_Pressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        CloseCard();
    }

    private void SaveWord_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_cardVideoPath) && string.IsNullOrWhiteSpace(_cardPageUrl))
        {
            return;
        }

        try
        {
            SavedWords.Add(CardWord.Text, CardMeaning.Text, CardSentence.Text, _cardVideoPath, _cardTimeMs, _cardPageUrl, _cardResolve, _cardSourceName, _cardAudioLanguage, _cardCaptionLanguage);
        }
        catch (IOException)
        {
            ToolTipService.SetToolTip(SaveWordButton, "Could not save this word");
            return;
        }
        catch (UnauthorizedAccessException)
        {
            ToolTipService.SetToolTip(SaveWordButton, "Could not save this word");
            return;
        }

        SaveWordButton.IsEnabled = false;
        SaveWordButton.Content = "Saved";
        ToolTipService.SetToolTip(SaveWordButton, "Saved on this computer");
        WordSaved?.Invoke(this, EventArgs.Empty);
    }

    private bool SavedChoice(string english, string sentence)
    {
        return SavedWords.All().Any(word =>
            string.Equals(word.English, english.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(word.Sentence, sentence.Trim(), StringComparison.Ordinal)
            && string.Equals(Clean(word.VideoPath), Clean(_cardVideoPath), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Clean(word.PageUrl), Clean(_cardPageUrl), StringComparison.OrdinalIgnoreCase)
            && word.TimeMs == _cardTimeMs
            && string.Equals(Clean(word.AudioLanguage), Clean(_cardAudioLanguage), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Clean(word.CaptionLanguage), Clean(_cardCaptionLanguage), StringComparison.OrdinalIgnoreCase));
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    // The playback bar sits outside this control, so a press anywhere else in the window closes the card.
    private void RootPressed(object sender, PointerRoutedEventArgs e)
    {
        if (WordCard.Visibility != Visibility.Visible || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (IsInside(source, WordCard) || IsInside(source, Line))
        {
            return;
        }

        CloseCard();
    }

    private HashSet<int> HighlightedIndexes(int cueIndex)
    {
        var marks = new HashSet<int>();
        if (_savedEnglish is null || cueIndex < 0 || cueIndex >= _cues.Count)
        {
            return marks;
        }

        var cue = _cues[cueIndex];
        var sentenceHit = _savedSentence is not null
            && string.Equals(Compact(cue.Text), Compact(_savedSentence), StringComparison.Ordinal);
        var timeHit = _savedTimeMs is long time && time >= cue.StartMs && time < cue.EndMs;
        if (!sentenceHit && !timeHit)
        {
            return marks;
        }

        if (!TryFindSpan(_line, _savedEnglish, out var start, out var length))
        {
            return marks;
        }

        for (var i = start; i < start + length; i++)
        {
            marks.Add(i);
        }

        return marks;
    }

    private static bool TryFindSpan(string[] line, string english, out int start, out int length)
    {
        start = 0;
        length = 0;
        var parts = english.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || line.Length < parts.Length)
        {
            return false;
        }

        for (var i = 0; i <= line.Length - parts.Length; i++)
        {
            var match = true;
            for (var j = 0; j < parts.Length; j++)
            {
                if (!string.Equals(DisplayWord(line[i + j]), DisplayWord(parts[j]), StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                start = i;
                length = parts.Length;
                return true;
            }
        }

        return false;
    }

    private static string Compact(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private string EnglishFor(int wordIndex)
    {
        if (TurkishDictionary.FindPhrase(_line, wordIndex) is { } hit)
        {
            var parts = new List<string>(hit.Length);
            var end = Math.Min(_line.Length, hit.Start + hit.Length);
            for (var i = Math.Max(0, hit.Start); i < end; i++)
            {
                var part = DisplayWord(_line[i]);
                if (part.Length > 0)
                {
                    parts.Add(part);
                }
            }

            if (parts.Count > 0)
            {
                return string.Join(' ', parts);
            }
        }

        return wordIndex >= 0 && wordIndex < _line.Length ? DisplayWord(_line[wordIndex]) : string.Empty;
    }

    private static string DisplayWord(string word)
    {
        var text = word.Trim()
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u02BC', '\'')
            .Replace('\uFF07', '\'');
        var start = 0;
        var end = text.Length;
        while (start < end && IsEdgeMark(text[start]))
        {
            start++;
        }

        while (end > start && IsEdgeMark(text[end - 1]))
        {
            end--;
        }

        return text[start..end];
    }

    private static bool IsEdgeMark(char character)
    {
        return character is '"' or '.' or ',' or '!' or '?' or ';' or ':'
            or '(' or ')' or '[' or ']' or '{' or '}'
            or '…' or '-' or '–' or '—'
            or '«' or '»' or '“' or '”' or '„' or '‹' or '›';
    }

    private void HookRoot()
    {
        if (_root is not null || XamlRoot?.Content is not UIElement root)
        {
            return;
        }

        _root = root;
        _root.AddHandler(UIElement.PointerPressedEvent, _rootPressed, true);
    }

    private void CloseCard()
    {
        WordCard.Visibility = Visibility.Collapsed;
        Dismiss.Visibility = Visibility.Collapsed;
        if (_root is null)
        {
            return;
        }

        _root.RemoveHandler(UIElement.PointerPressedEvent, _rootPressed);
        _root = null;
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

    private void HideMeaning() => MeaningCard.Visibility = Visibility.Collapsed;
}
