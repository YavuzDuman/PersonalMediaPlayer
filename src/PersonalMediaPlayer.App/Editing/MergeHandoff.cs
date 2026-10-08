namespace PersonalMediaPlayer.App.Editing;

internal readonly record struct MergeEditorSeed(
    double TrimStartSeconds,
    double TrimEndSeconds,
    bool VideoFadeIn,
    double VideoFadeInSeconds,
    bool VideoFadeOut,
    double VideoFadeOutSeconds,
    bool AudioFadeIn,
    double AudioFadeInSeconds,
    bool AudioFadeOut,
    double AudioFadeOutSeconds);

internal readonly record struct MergeSlot(
    string Path,
    double StartSeconds,
    double EndSeconds,
    bool VideoFadeIn,
    bool VideoFadeOut,
    bool AudioFadeIn,
    bool AudioFadeOut);

internal sealed class MergeClipReturn
{
    public string? SavedPath { get; private set; }

    public bool Complete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        SavedPath = path;
        return true;
    }
}

internal static class MergeHandoff
{
    public static MergeSlot[]? PutBack(IReadOnlyList<MergeSlot> clips, int index, string? savedPath, double savedDuration)
    {
        if (string.IsNullOrWhiteSpace(savedPath) || savedDuration <= 0 || index < 0 || index >= clips.Count)
        {
            return null;
        }

        var next = clips.ToArray();
        next[index] = new MergeSlot(savedPath, 0, savedDuration, false, false, false, false);
        return next;
    }

    public static (double Start, double End) FitSpan(double start, double end, double duration)
    {
        const double minimum = 0.1;
        if (duration <= 0)
        {
            return (0, 0);
        }

        if (duration <= minimum)
        {
            return (0, duration);
        }

        var from = Math.Clamp(Math.Min(start, end), 0, duration);
        var to = Math.Clamp(Math.Max(start, end), 0, duration);
        if (to - from >= minimum)
        {
            return (from, to);
        }

        if (from + minimum <= duration)
        {
            return (from, from + minimum);
        }

        return (duration - minimum, duration);
    }

    public static MergeEditorSeed? Seed(
        double durationSeconds,
        double startSeconds,
        double endSeconds,
        bool videoIn,
        double videoInSeconds,
        bool videoOut,
        double videoOutSeconds,
        bool audioIn,
        double audioInSeconds,
        bool audioOut,
        double audioOutSeconds)
    {
        if (durationSeconds <= 0)
        {
            return null;
        }

        var start = Math.Clamp(startSeconds, 0, durationSeconds);
        var end = Math.Clamp(endSeconds, 0, durationSeconds);
        var trimmed = start > 0.05 || durationSeconds - end > 0.05;
        var fades = videoIn || videoOut || audioIn || audioOut;
        if (!trimmed && !fades)
        {
            return null;
        }

        return new MergeEditorSeed(
            start,
            Math.Max(start, end),
            videoIn,
            videoInSeconds,
            videoOut,
            videoOutSeconds,
            audioIn,
            audioInSeconds,
            audioOut,
            audioOutSeconds);
    }
}
