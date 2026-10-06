using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class SubtitleSearchTests
{
    [Fact]
    public void AWordFindsTheLinesThatContainIt()
    {
        var cues = new SubtitleCue[]
        {
            new(0, 1_000, "Hello there"),
            new(1_500, 2_500, "Nothing here"),
            new(3_000, 4_000, "Say HELLO again")
        };

        var found = SubtitleSearch.Find(cues, " hello ");

        Assert.Equal(2, found.Count);
        Assert.Equal(0, found[0].StartMs);
        Assert.Equal("Hello there", found[0].Text);
        Assert.Equal(3_000, found[1].StartMs);
    }

    [Fact]
    public void AnEmptyQueryMatchesNothing()
    {
        var cues = new SubtitleCue[] { new(0, 1_000, "Hello") };

        Assert.Empty(SubtitleSearch.Find(cues, "  "));
        Assert.Empty(SubtitleSearch.Find([], "hello"));
    }
}
