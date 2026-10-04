namespace PersonalMediaPlayer.App.Playback;

internal enum PlaybackVisitKind
{
    Open,
    Keep,
    Seek
}

internal readonly record struct PlaybackVisit(PlaybackVisitKind Kind, long SeekMs);

internal static class PlaybackVisitChoice
{
    public static PlaybackVisit Choose(string? currentKey, string? nextKey, bool jump, long jumpMs)
    {
        var current = Normalize(currentKey);
        var next = Normalize(nextKey);
        if (current is null || next is null || !string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
        {
            return new PlaybackVisit(PlaybackVisitKind.Open, jumpMs);
        }

        return jump
            ? new PlaybackVisit(PlaybackVisitKind.Seek, jumpMs)
            : new PlaybackVisit(PlaybackVisitKind.Keep, 0);
    }

    private static string? Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return key.Trim();
    }
}
