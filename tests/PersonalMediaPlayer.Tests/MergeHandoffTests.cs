using PersonalMediaPlayer.App.Editing;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class MergeHandoffTests
{
    [Fact]
    public void Saved_file_replaces_that_clip_only()
    {
        var clips = new[]
        {
            new MergeSlot("a.mp4", 1, 4, true, false, false, true),
            new MergeSlot("b.mp4", 0, 8, false, false, false, false),
            new MergeSlot("c.mp4", 2, 6, false, true, true, false)
        };

        var placed = MergeHandoff.PutBack(clips, 1, "saved.mp4", 5);

        Assert.NotNull(placed);
        Assert.Equal(new MergeSlot("b.mp4", 0, 8, false, false, false, false), clips[1]);
        Assert.Equal(clips[0], placed[0]);
        Assert.Equal(clips[2], placed[2]);
        Assert.Equal(new MergeSlot("saved.mp4", 0, 5, false, false, false, false), placed[1]);
    }

    [Fact]
    public void A_missing_save_leaves_the_list_alone()
    {
        var clips = new[] { new MergeSlot("a.mp4", 0, 3, false, false, false, false) };
        Assert.Null(MergeHandoff.PutBack(clips, 0, "  ", 3));
        Assert.Null(MergeHandoff.PutBack(clips, 0, "saved.mp4", 0));
        Assert.Null(MergeHandoff.PutBack(clips, 2, "saved.mp4", 3));
        Assert.Null(MergeHandoff.PutBack(clips, -1, "saved.mp4", 3));
        var handoff = new MergeClipReturn();
        Assert.Null(handoff.SavedPath);
        Assert.False(handoff.Complete(" "));
        Assert.Null(handoff.SavedPath);
        Assert.True(handoff.Complete("saved.mp4"));
        Assert.Equal("saved.mp4", handoff.SavedPath);
    }

    [Fact]
    public void A_full_clip_with_no_fades_starts_clean()
    {
        Assert.Null(MergeHandoff.Seed(12, 0, 12, false, 1, false, 1, false, 1, false, 1));
        Assert.Null(MergeHandoff.Seed(10, 0.04, 10, false, 1, false, 1, false, 1, false, 1));
    }

    [Fact]
    public void A_trimmed_clip_keeps_its_span()
    {
        var seed = MergeHandoff.Seed(12, 1.5, 9, false, 1, false, 1, false, 1, false, 1);
        Assert.NotNull(seed);
        Assert.Equal(1.5, seed.Value.TrimStartSeconds);
        Assert.Equal(9, seed.Value.TrimEndSeconds);
        Assert.False(seed.Value.VideoFadeIn);
    }

    [Fact]
    public void Fades_come_along_on_a_full_clip()
    {
        var seed = MergeHandoff.Seed(8, 0, 8, true, 2, false, 1, false, 1, true, 0.4);
        Assert.NotNull(seed);
        Assert.True(seed.Value.VideoFadeIn);
        Assert.Equal(2, seed.Value.VideoFadeInSeconds);
        Assert.True(seed.Value.AudioFadeOut);
        Assert.Equal(0.4, seed.Value.AudioFadeOutSeconds);
        Assert.Equal(0, seed.Value.TrimStartSeconds);
        Assert.Equal(8, seed.Value.TrimEndSeconds);
    }

    [Fact]
    public void Seed_clamps_the_span_to_the_clip()
    {
        var seed = MergeHandoff.Seed(5, -1, 9, false, 1, true, 1, false, 1, false, 1);
        Assert.NotNull(seed);
        Assert.Equal(0, seed.Value.TrimStartSeconds);
        Assert.Equal(5, seed.Value.TrimEndSeconds);
        Assert.True(seed.Value.VideoFadeOut);
    }

    [Fact]
    public void A_shorter_file_pulls_the_span_in()
    {
        Assert.Equal((0d, 5d), MergeHandoff.FitSpan(0, 10, 5));
        Assert.Equal((1d, 4d), MergeHandoff.FitSpan(1, 4, 5));
        Assert.Equal((2d, 5d), MergeHandoff.FitSpan(2, 9, 5));
        var collapsed = MergeHandoff.FitSpan(8, 10, 5);
        Assert.Equal(4.9, collapsed.Start, 5);
        Assert.Equal(5, collapsed.End, 5);
        Assert.Equal((0d, 0d), MergeHandoff.FitSpan(0, 10, 0));
    }
}
