using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;

namespace PersonalMediaPlayer.App.Controls;

public sealed class SubtitleHit : INotifyPropertyChanged
{
    private bool _saved;
    private bool _ready;

    public long StartMs { get; init; }

    public string Time { get; init; } = string.Empty;

    public string Line { get; init; } = string.Empty;

    public string SaveLabel => _saved ? "Saved" : "Save";

    public bool CanSave => _ready;

    public string SaveTip
        => _saved
            ? "Saved on this computer"
            : _ready
                ? "Save this line on this computer"
                : "This caption is not attached to a video.";

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Apply(CaptionLineSaveState state)
    {
        var saved = state == CaptionLineSaveState.Saved;
        var ready = state == CaptionLineSaveState.Ready;
        if (_saved == saved && _ready == ready)
        {
            return;
        }

        _saved = saved;
        _ready = ready;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SaveLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSave)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SaveTip)));
    }
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

    internal Func<string, long, bool>? SaveLine { get; set; }

    internal Func<string, long, CaptionLineSaveState>? LineState { get; set; }

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

    internal void NoteSaved()
    {
        if (Results.ItemsSource is not IEnumerable<SubtitleHit> hits)
        {
            return;
        }

        foreach (var hit in hits)
        {
            hit.Apply(StateFor(hit.Line, hit.StartMs));
        }
    }

    private void Hit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not SubtitleHit hit)
        {
            return;
        }

        CueChosen?.Invoke(this, hit.StartMs);
    }

    private void SaveHit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not SubtitleHit hit || !hit.CanSave)
        {
            return;
        }

        if (SaveLine?.Invoke(hit.Line, hit.StartMs) != true)
        {
            hit.Apply(StateFor(hit.Line, hit.StartMs));
            if (hit.CanSave)
            {
                Status.Text = "Could not save this line.";
            }

            return;
        }

        hit.Apply(CaptionLineSaveState.Saved);
    }

    private CaptionLineSaveState StateFor(string line, long startMs)
        => LineState?.Invoke(line, startMs) ?? CaptionLineSaveState.Unavailable;

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
        Results.ItemsSource = matches.Select(cue =>
        {
            var hit = new SubtitleHit
            {
                StartMs = cue.StartMs,
                Time = VideoChapters.Format(cue.StartMs),
                Line = cue.Text
            };
            hit.Apply(StateFor(cue.Text, cue.StartMs));
            return hit;
        }).ToArray();
        Results.Visibility = Visibility.Visible;
    }
}
