using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class SubtitleEditTests
{
    [Fact]
    public void KeptRangesCutAndShiftCaptions()
    {
        var cues = new[]
        {
            new SubtitleCue(0, 2_000, "Opening"),
            new SubtitleCue(1_500, 4_500, "Across the cut"),
            new SubtitleCue(8_000, 9_000, "Later")
        };

        var kept = SubtitleEdit.Keep(cues, [(0, 2_000), (4_000, 9_000)], speed: 2);
        Assert.Equal(4, kept.Count);
        Assert.Equal("Opening", kept[0].Text);
        Assert.Equal(0, kept[0].StartMs);
        Assert.Equal(1_000, kept[0].EndMs);
        Assert.Equal("Across the cut", kept[1].Text);
        Assert.Equal(750, kept[1].StartMs);
        Assert.Equal(1_000, kept[1].EndMs);
        Assert.Equal("Across the cut", kept[2].Text);
        Assert.Equal(1_000, kept[2].StartMs);
        Assert.Equal(1_250, kept[2].EndMs);
        Assert.Equal("Later", kept[3].Text);
        Assert.Equal(3_000, kept[3].StartMs);
        Assert.Equal(3_500, kept[3].EndMs);
    }

    [Fact]
    public void JoinedClipsFollowEachOther()
    {
        var joined = SubtitleEdit.Join(
        [
            ([new SubtitleCue(1_000, 2_000, "First")], 500, 2_500),
            ([new SubtitleCue(0, 800, "Second")], 0, 1_000)
        ]);

        Assert.Equal(2, joined.Count);
        Assert.Equal("First", joined[0].Text);
        Assert.Equal(500, joined[0].StartMs);
        Assert.Equal(1_500, joined[0].EndMs);
        Assert.Equal("Second", joined[1].Text);
        Assert.Equal(2_000, joined[1].StartMs);
        Assert.Equal(2_800, joined[1].EndMs);
        var parsed = SubtitleCues.Parse(SubtitleEdit.ToSrt(joined));
        Assert.Equal(joined, parsed);
    }
}
