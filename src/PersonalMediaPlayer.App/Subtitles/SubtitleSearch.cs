namespace PersonalMediaPlayer.App.Subtitles;

internal static class SubtitleSearch
{
    public static IReadOnlyList<SubtitleCue> Find(IReadOnlyList<SubtitleCue> cues, string? query)
    {
        if (cues.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var term = query.Trim();
        var found = new List<SubtitleCue>();
        foreach (var cue in cues)
        {
            if (cue.Text.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            {
                found.Add(cue);
            }
        }

        return found;
    }
}
