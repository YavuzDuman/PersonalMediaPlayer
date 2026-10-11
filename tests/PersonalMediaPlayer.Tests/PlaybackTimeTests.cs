using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaybackTimeTests
{
    [Fact]
    public void MinutesAndHoursBothJumpToThatMoment()
    {
        Assert.True(PlaybackTime.TryParse("12:35", out var minutes));
        Assert.Equal((12 * 60 + 35) * 1000L, minutes);
        Assert.True(PlaybackTime.TryParse("1:02:30", out var hours));
        Assert.Equal((1 * 3600 + 2 * 60 + 30) * 1000L, hours);
        Assert.True(PlaybackTime.TryParse(" 0:05 ", out var shortTime));
        Assert.Equal(5_000L, shortTime);
        Assert.True(PlaybackTime.TryParse("01:02:03", out var padded));
        Assert.Equal((3600 + 2 * 60 + 3) * 1000L, padded);
    }

    [Fact]
    public void ABadTimeDoesNotParse()
    {
        Assert.False(PlaybackTime.TryParse(null, out _));
        Assert.False(PlaybackTime.TryParse("", out _));
        Assert.False(PlaybackTime.TryParse("12", out _));
        Assert.False(PlaybackTime.TryParse("12:60", out _));
        Assert.False(PlaybackTime.TryParse("1:60:00", out _));
        Assert.False(PlaybackTime.TryParse("1:02:60", out _));
        Assert.False(PlaybackTime.TryParse("12:35:00:01", out _));
        Assert.False(PlaybackTime.TryParse("ab:cd", out _));
        Assert.False(PlaybackTime.TryParse("12 : 35", out _));
    }
}
