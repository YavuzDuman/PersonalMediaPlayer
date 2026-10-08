using PersonalMediaPlayer.App.Editing;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class AudioSilenceTests
{
    [Fact]
    public void Normalize_merges_overlaps_and_drops_a_short_span()
    {
        var spans = AudioSilence.Normalize(
            [
                new(1_000, 1_200, 0),
                new(2_000, 3_000, 0),
                new(2_800, 4_000, 0),
                new(8_000, 8_100, 0)
            ],
            10_000);

        var kept = Assert.Single(spans);
        Assert.Equal(2_000, kept.StartMs);
        Assert.Equal(4_000, kept.EndMs);
        Assert.Equal(0, kept.VolumePercent);
    }

    [Fact]
    public void Normalize_clamps_to_the_video()
    {
        var spans = AudioSilence.Normalize([new(-500, 12_000, 250)], 5_000);

        var kept = Assert.Single(spans);
        Assert.Equal(0, kept.StartMs);
        Assert.Equal(5_000, kept.EndMs);
        Assert.Equal(200, kept.VolumePercent);
    }

    [Fact]
    public void A_later_level_replaces_the_overlap()
    {
        var spans = AudioSilence.Normalize(
            [
                new(0, 5_000, 0),
                new(1_000, 2_000, 30),
                new(1_500, 2_500, 100)
            ],
            10_000);

        Assert.Equal(3, spans.Count);
        Assert.Equal((0L, 1_000L, 0), (spans[0].StartMs, spans[0].EndMs, spans[0].VolumePercent));
        Assert.Equal((1_000L, 1_500L, 30), (spans[1].StartMs, spans[1].EndMs, spans[1].VolumePercent));
        Assert.Equal((2_500L, 5_000L, 0), (spans[2].StartMs, spans[2].EndMs, spans[2].VolumePercent));
    }

    [Fact]
    public void Original_volume_clears_that_stretch_and_drops_a_short_remnant()
    {
        var spans = AudioSilence.Normalize(
            [
                new(0, 5_000, 30),
                new(1_000, 4_800, 100)
            ],
            10_000);

        var kept = Assert.Single(spans);
        Assert.Equal(0, kept.StartMs);
        Assert.Equal(1_000, kept.EndMs);
        Assert.Equal(30, kept.VolumePercent);
    }

    [Fact]
    public void Touching_spans_at_different_levels_stay_separate()
    {
        var spans = AudioSilence.Normalize([new(0, 2_000, 30), new(2_000, 4_000, 150)], 10_000);

        Assert.Equal(2, spans.Count);
        Assert.Equal((0L, 2_000L, 30), (spans[0].StartMs, spans[0].EndMs, spans[0].VolumePercent));
        Assert.Equal((2_000L, 4_000L, 150), (spans[1].StartMs, spans[1].EndMs, spans[1].VolumePercent));
    }

    [Fact]
    public void Touching_spans_at_the_same_level_merge()
    {
        var spans = AudioSilence.Normalize([new(0, 2_000, 150), new(2_000, 4_000, 150)], 10_000);

        var kept = Assert.Single(spans);
        Assert.Equal(0, kept.StartMs);
        Assert.Equal(4_000, kept.EndMs);
        Assert.Equal(150, kept.VolumePercent);
    }

    [Fact]
    public void Gain_is_original_outside_the_span()
    {
        var spans = new[] { new AudioSilence.Span(1_000, 2_000, 30) };

        Assert.Equal(100, AudioSilence.GainPercent(spans, 999));
        Assert.Equal(30, AudioSilence.GainPercent(spans, 1_000));
        Assert.Equal(30, AudioSilence.GainPercent(spans, 1_999));
        Assert.Equal(100, AudioSilence.GainPercent(spans, 2_000));
        Assert.True(AudioSilence.Contains(spans, 1_000));
        Assert.False(AudioSilence.Contains(spans, 2_000));
    }

    [Fact]
    public void Volume_filter_groups_each_level()
    {
        var silent = AudioSilence.VolumeFilter([new(1_500, 2_250, 0), new(4_000, 4_500, 0)]);
        var mixed = AudioSilence.VolumeFilter([new(1_000, 2_000, 30), new(3_000, 4_000, 150)]);

        Assert.Equal("volume=0:enable='between(t,1.500,2.250)+between(t,4.000,4.500)'", silent);
        Assert.Equal("volume=0.3:enable='between(t,1.000,2.000)',volume=1.5:enable='between(t,3.000,4.000)'", mixed);
    }

    [Fact]
    public void Preview_volume_keeps_the_saved_ratio()
    {
        var rest = AudioSilence.PlayerVolume(80, 1, 100);
        var quiet = AudioSilence.PlayerVolume(80, 1, 30);
        var loud = AudioSilence.PlayerVolume(80, 1, 150);
        var full = AudioSilence.PlayerVolume(100, 1, 100);
        var max = AudioSilence.PlayerVolume(100, 1, 200);

        Assert.Equal(80, rest);
        Assert.Equal(54, quiet);
        Assert.Equal(92, loud);
        Assert.Equal(0, AudioSilence.PlayerVolume(80, 1, 0));
        Assert.Equal(40, AudioSilence.PlayerVolume(80, 0.5, 100));
        Assert.Equal(100, full);
        Assert.Equal(126, max);
        Assert.InRange(Amplitude(quiet) / Amplitude(rest), 0.29, 0.31);
        Assert.InRange(Amplitude(loud) / Amplitude(rest), 1.45, 1.55);
        Assert.InRange(Amplitude(max) / Amplitude(full), 1.95, 2.05);
    }

    private static double Amplitude(int playerVolume) => Math.Pow(playerVolume / 100d, 3);
}
