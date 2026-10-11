namespace PersonalMediaPlayer.App.Playback;

internal enum SleepSpan
{
    Off,
    OneMinute,
    FiveMinutes,
    QuarterHour,
    HalfHour,
    Hour,
    EndOfVideo
}

internal sealed class ListeningSession
{
    public bool AudioOnly { get; private set; }

    public SleepSpan Sleep { get; private set; }

    public long? DeadlineUtcMs { get; private set; }

    public bool StopsAtEnd => Sleep == SleepSpan.EndOfVideo;

    public void UseAudio() => AudioOnly = true;

    public void UseVideo() => AudioOnly = false;

    public void SetSleep(SleepSpan span, long nowUtcMs)
    {
        Sleep = span;
        DeadlineUtcMs = span switch
        {
            SleepSpan.OneMinute => nowUtcMs + 60_000,
            SleepSpan.FiveMinutes => nowUtcMs + 5 * 60_000,
            SleepSpan.QuarterHour => nowUtcMs + 15 * 60_000,
            SleepSpan.HalfHour => nowUtcMs + 30 * 60_000,
            SleepSpan.Hour => nowUtcMs + 60 * 60_000,
            _ => null
        };
    }

    public void ClearSleep()
    {
        Sleep = SleepSpan.Off;
        DeadlineUtcMs = null;
    }

    public bool Due(long nowUtcMs) => DeadlineUtcMs is long deadline && nowUtcMs >= deadline;

    // A note box pauses playback and resumes when it closes. A sleep that ended while the box was open stays paused.
    public bool FinishedWhileOpen(SleepSpan opened, long nowUtcMs)
    {
        if (opened == SleepSpan.Off)
        {
            return false;
        }

        return Sleep != opened || Due(nowUtcMs);
    }

    public string Label(long nowUtcMs)
    {
        if (Sleep == SleepSpan.EndOfVideo)
        {
            return "End";
        }

        if (DeadlineUtcMs is not long deadline)
        {
            return "Sleep";
        }

        var left = Math.Max(0, deadline - nowUtcMs) / 1000;
        return $"{left / 60}:{left % 60:00}";
    }
}
