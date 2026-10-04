using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaybackVisitTests
{
    [Fact]
    public void SameVideoStaysWhereItIs()
    {
        var visit = PlaybackVisitChoice.Choose(@"C:\Videos\Clip.mp4", @"c:\videos\clip.mp4", jump: false, jumpMs: 4_000);

        Assert.Equal(PlaybackVisitKind.Keep, visit.Kind);
    }

    [Fact]
    public void SavedWordOnTheSameVideoSeeks()
    {
        var visit = PlaybackVisitChoice.Choose("https://example.com/watch", " https://example.com/watch ", jump: true, jumpMs: 0);

        Assert.Equal(PlaybackVisitKind.Seek, visit.Kind);
        Assert.Equal(0, visit.SeekMs);
    }

    [Fact]
    public void AnotherVideoOpensFresh()
    {
        var visit = PlaybackVisitChoice.Choose(@"C:\Videos\One.mp4", @"C:\Videos\Two.mp4", jump: true, jumpMs: 8_000);

        Assert.Equal(PlaybackVisitKind.Open, visit.Kind);
        Assert.Equal(8_000, visit.SeekMs);
    }

    [Theory]
    [InlineData(null, @"C:\Videos\One.mp4")]
    [InlineData(@"C:\Videos\One.mp4", null)]
    [InlineData("  ", "https://example.com/watch")]
    public void MissingKeyOpens(string? current, string? next)
    {
        var visit = PlaybackVisitChoice.Choose(current, next, jump: true, jumpMs: 1_500);

        Assert.Equal(PlaybackVisitKind.Open, visit.Kind);
    }
}
