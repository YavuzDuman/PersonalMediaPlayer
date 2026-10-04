using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaybackFocusTests
{
    public PlaybackFocusTests() => PlaybackFocus.Clear();

    [Fact]
    public void PlayingOnePausesTheOther()
    {
        var watching = new FakePlayback();
        var preview = new FakePlayback();
        PlaybackFocus.Register(watching);
        PlaybackFocus.Register(preview);

        PlaybackFocus.Claim(preview);

        Assert.Equal(1, watching.Pauses);
        Assert.Equal(0, preview.Pauses);
        Assert.True(PlaybackFocus.HeldBySomeoneElse(watching));
    }

    [Fact]
    public void FullPlayerPausesThePreviewOnly()
    {
        var watching = new FakePlayback();
        var preview = new FakePlayback();
        PlaybackFocus.Register(watching);
        PlaybackFocus.Register(preview);

        PlaybackFocus.PauseOthers(watching);

        Assert.Equal(0, watching.Pauses);
        Assert.Equal(1, preview.Pauses);
    }

    private sealed class FakePlayback : IPlaybackSource
    {
        public int Pauses { get; private set; }

        public void PauseForOther() => Pauses++;
    }
}
