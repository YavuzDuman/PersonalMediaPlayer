using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class LibraryCaptionSearchTests
{
    [Fact]
    public void HitsAreOrderedByVideoThenTime()
    {
        var match = LibraryCaptionSearch.Find("  hello ",
        [
            new CaptionVideo("Zebra", @"C:\z.mp4", [new SubtitleCue(5_000, 6_000, "hello there")]),
            new CaptionVideo("alpha", @"C:\a.mp4", [new SubtitleCue(8_000, 9_000, "nope"), new SubtitleCue(1_000, 2_000, "Say Hello")])
        ]);

        Assert.False(match.Truncated);
        Assert.Equal(["alpha", "Zebra"], match.Hits.Select(hit => hit.Title).ToArray());
        Assert.Equal(1_000, match.Hits[0].StartMs);
        Assert.Equal("Say Hello", match.Hits[0].Line);
        Assert.Equal(@"C:\a.mp4", match.Hits[0].FilePath);
        Assert.Equal(5_000, match.Hits[1].StartMs);
    }

    [Fact]
    public void AnEmptyQueryMatchesNothing()
    {
        var cues = new CaptionVideo("Alpha", @"C:\a.mp4", [new SubtitleCue(0, 1_000, "Hello")]);

        Assert.Empty(LibraryCaptionSearch.Find(null, [cues]).Hits);
        Assert.Empty(LibraryCaptionSearch.Find("   ", [cues]).Hits);
        Assert.Empty(LibraryCaptionSearch.Find("hello", [new CaptionVideo("Alpha", @"C:\a.mp4", [])]).Hits);
    }

    [Fact]
    public void TheSameTitleFollowsTheFilePath()
    {
        var match = LibraryCaptionSearch.Find("hi",
        [
            new CaptionVideo("Same", @"C:\b.mp4", [new SubtitleCue(2_000, 3_000, "hi b")]),
            new CaptionVideo("Same", @"C:\a.mp4", [new SubtitleCue(1_000, 2_000, "hi a")])
        ]);

        Assert.Equal([@"C:\a.mp4", @"C:\b.mp4"], match.Hits.Select(hit => hit.FilePath).ToArray());
    }

    [Fact]
    public void ABlankTitleUsesTheFileName()
    {
        var hit = Assert.Single(LibraryCaptionSearch.Find("hi",
        [
            new CaptionVideo("  ", @"C:\clips\shore.mp4", [new SubtitleCue(0, 1_000, "hi")])
        ]).Hits);

        Assert.Equal("shore.mp4", hit.Title);
    }

    [Fact]
    public void EachVideoKeepsEightLinesAndTheListKeepsEighty()
    {
        var videos = new List<CaptionVideo>();
        for (var video = 0; video < 11; video++)
        {
            var cues = new List<SubtitleCue>();
            for (var line = 0; line < 9; line++)
            {
                cues.Add(new SubtitleCue(line * 1_000, line * 1_000 + 500, "alpha " + line));
            }

            videos.Add(new CaptionVideo($"Video {video:00}", $@"C:\v{video}.mp4", cues));
        }

        var match = LibraryCaptionSearch.Find("alpha", videos);

        Assert.Equal(LibraryCaptionSearch.MaxHits, match.Hits.Count);
        Assert.True(match.Truncated);
        Assert.Equal(LibraryCaptionSearch.MaxPerVideo, match.Hits.Count(hit => hit.Title == "Video 00"));
        Assert.Equal(0, match.Hits[0].StartMs);
        Assert.Equal(7_000, match.Hits[7].StartMs);
        Assert.DoesNotContain(match.Hits, hit => hit.Title == "Video 10");
    }
}
