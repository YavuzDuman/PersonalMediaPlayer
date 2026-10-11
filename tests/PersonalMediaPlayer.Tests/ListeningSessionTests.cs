using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class ListeningSessionTests
{
    [Fact]
    public void ATimedSleepPausesWhenTheClockArrives()
    {
        var session = new ListeningSession();
        session.SetSleep(SleepSpan.QuarterHour, 1_000);

        Assert.False(session.Due(1_000 + 15 * 60_000 - 1));
        Assert.True(session.Due(1_000 + 15 * 60_000));
        Assert.Equal("15:00", session.Label(1_000));
        Assert.Equal("0:01", session.Label(1_000 + 15 * 60_000 - 1_000));
    }

    [Fact]
    public void ShortSleepsUseOneAndFiveMinutes()
    {
        var session = new ListeningSession();
        session.SetSleep(SleepSpan.OneMinute, 2_000);

        Assert.Equal(2_000 + 60_000, session.DeadlineUtcMs);
        Assert.Equal("1:00", session.Label(2_000));
        Assert.False(session.Due(2_000 + 60_000 - 1));
        Assert.True(session.Due(2_000 + 60_000));

        session.SetSleep(SleepSpan.FiveMinutes, 2_000);
        Assert.Equal(2_000 + 5 * 60_000, session.DeadlineUtcMs);
        Assert.Equal("5:00", session.Label(2_000));
        Assert.False(session.Due(2_000 + 5 * 60_000 - 1));
        Assert.True(session.Due(2_000 + 5 * 60_000));
    }

    [Fact]
    public void ASleepThatEndsWhileTheNoteBoxIsOpenStaysPaused()
    {
        var session = new ListeningSession();
        session.SetSleep(SleepSpan.OneMinute, 0);
        var opened = session.Sleep;

        Assert.False(session.FinishedWhileOpen(opened, 60_000 - 1));
        Assert.True(session.FinishedWhileOpen(opened, 60_000));

        session.ClearSleep();
        Assert.True(session.FinishedWhileOpen(opened, 60_000));

        var idle = new ListeningSession();
        Assert.False(idle.FinishedWhileOpen(SleepSpan.Off, 60_000));

        var untilEnd = new ListeningSession();
        untilEnd.SetSleep(SleepSpan.EndOfVideo, 0);
        var end = untilEnd.Sleep;
        Assert.False(untilEnd.FinishedWhileOpen(end, long.MaxValue));
        untilEnd.ClearSleep();
        Assert.True(untilEnd.FinishedWhileOpen(end, long.MaxValue));
    }

    [Fact]
    public void TheEndOfTheVideoStopsTheQueueWithoutAClock()
    {
        var session = new ListeningSession();
        session.SetSleep(SleepSpan.EndOfVideo, 5_000);

        Assert.True(session.StopsAtEnd);
        Assert.False(session.Due(long.MaxValue));
        Assert.Equal("End", session.Label(5_000));

        session.ClearSleep();
        Assert.False(session.StopsAtEnd);
        Assert.Equal("Sleep", session.Label(5_000));
    }

    [Fact]
    public void AudioOnlyStaysUntilVideoIsChosenAgain()
    {
        var session = new ListeningSession();
        session.UseAudio();
        session.SetSleep(SleepSpan.Hour, 0);

        Assert.True(session.AudioOnly);
        Assert.Equal(60 * 60_000, session.DeadlineUtcMs);

        session.UseVideo();
        Assert.False(session.AudioOnly);
        Assert.Equal(60 * 60_000, session.DeadlineUtcMs);
    }
}
