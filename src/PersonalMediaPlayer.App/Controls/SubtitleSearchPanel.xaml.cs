using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;

namespace PersonalMediaPlayer.App.Controls;

public sealed class SubtitleHit
{
    public long StartMs { get; init; }

    public string Time { get; init; } = string.Empty;

    public string Line { get; init; } = string.Empty;
}

public sealed partial class SubtitleSearchPanel : UserControl
{
    private enum Availability
    {
        Loading,
        None,
        Off,
        Ready
    }

    private IReadOnlyList<SubtitleCue> _cues = [];
    private Availability _availability = Availability.Loading;
    private bool _fillingQuery;

    public SubtitleSearchPanel()
    {
        InitializeComponent();
        Render();
    }

    internal event EventHandler<long>? CueChosen;

    internal void BeginVideo()
    {
        _fillingQuery = true;
        Query.Text = string.Empty;
        _fillingQuery = false;
        _cues = [];
        _availability = Availability.Loading;
        Render();
    }

    internal void ShowCues(IReadOnlyList<SubtitleCue> cues)
    {
        _cues = cues;
        _availability = cues.Count == 0 ? Availability.None : Availability.Ready;
        Render();
    }

    internal void ShowOff()
    {
        _cues = [];
        _availability = Availability.Off;
        Render();
    }

    internal void ShowLoading()
    {
        _cues = [];
        _availability = Availability.Loading;
        Render();
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_fillingQuery || _availability != Availability.Ready)
        {
            return;
        }

        Render();
    }

    private void Results_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SubtitleHit hit)
        {
            CueChosen?.Invoke(this, hit.StartMs);
        }
    }

    private void Render()
    {
        if (_availability != Availability.Ready)
        {
            Query.Visibility = Visibility.Collapsed;
            Results.Visibility = Visibility.Collapsed;
            Results.ItemsSource = null;
            Status.Text = _availability switch
            {
                Availability.Off => "Subtitles are off.",
                Availability.Loading => "Loading subtitles…",
                _ => "This video has no subtitles."
            };
            return;
        }

        Query.Visibility = Visibility.Visible;
        var matches = SubtitleSearch.Find(_cues, Query.Text);
        if (string.IsNullOrWhiteSpace(Query.Text))
        {
            Results.Visibility = Visibility.Collapsed;
            Results.ItemsSource = null;
            Status.Text = "Type a word to search the subtitles.";
            return;
        }

        if (matches.Count == 0)
        {
            Results.Visibility = Visibility.Collapsed;
            Results.ItemsSource = null;
            Status.Text = "No lines match.";
            return;
        }

        Status.Text = matches.Count == 1 ? "1 line" : $"{matches.Count} lines";
        Results.ItemsSource = matches.Select(cue => new SubtitleHit
        {
            StartMs = cue.StartMs,
            Time = VideoChapters.Format(cue.StartMs),
            Line = cue.Text
        }).ToArray();
        Results.Visibility = Visibility.Visible;
    }
}
