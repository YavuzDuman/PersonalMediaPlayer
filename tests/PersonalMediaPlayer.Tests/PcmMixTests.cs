using PersonalMediaPlayer.App.Capture;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PcmMixTests
{
    [Fact]
    public void Convert_returns_the_same_bytes_when_the_format_matches()
    {
        var pcm = Pcm(1, -2, 3);
        Assert.Same(pcm, PcmMix.Convert(pcm, 1, 48_000, 1, 48_000));
    }

    [Fact]
    public void Convert_returns_empty_for_a_blank_or_unusable_format()
    {
        Assert.Empty(PcmMix.Convert([], 1, 48_000, 1, 48_000));
        Assert.Empty(PcmMix.Convert(Pcm(1), 0, 48_000, 1, 48_000));
        Assert.Empty(PcmMix.Convert(Pcm(1), 3, 48_000, 1, 48_000));
        Assert.Empty(PcmMix.Convert(Pcm(1), 1, 0, 1, 48_000));
        Assert.Empty(PcmMix.Convert(Pcm(1), 1, 48_000, 2, 0));
    }

    [Fact]
    public void Convert_duplicates_mono_and_averages_stereo()
    {
        Assert.Equal(new short[] { 5, 5, -2, -2 }, Samples(PcmMix.Convert(Pcm(5, -2), 1, 48_000, 2, 48_000)));
        Assert.Equal(new short[] { 3, 1 }, Samples(PcmMix.Convert(Pcm(2, 4, 1, 2), 2, 48_000, 1, 48_000)));
    }

    [Fact]
    public void Convert_keeps_both_ends_when_the_rate_doubles()
    {
        var output = Samples(PcmMix.Convert(Pcm(0, 100), 1, 8_000, 1, 16_000));
        Assert.Equal(4, output.Length);
        Assert.Equal(0, output[0]);
        Assert.Equal(100, output[^1]);
        for (var index = 1; index < output.Length; index++)
        {
            Assert.True(output[index] >= output[index - 1]);
        }
    }

    [Fact]
    public void Add_sums_a_short_prefix_and_leaves_the_primary_alone()
    {
        var primary = Pcm(0, 0, 5);
        var mixed = PcmMix.Add(primary, Pcm(7));
        Assert.Equal(new short[] { 7, 0, 5 }, Samples(mixed));
        Assert.Equal(new short[] { 0, 0, 5 }, Samples(primary));
        Assert.Equal(new short[] { 0, 0 }, Samples(PcmMix.Add(Pcm(0, 0), Pcm(0, 0))));
    }

    [Fact]
    public void Add_returns_the_primary_when_there_is_nothing_to_mix()
    {
        var primary = Pcm(4, 5);
        Assert.Same(primary, PcmMix.Add(primary, []));
    }

    [Fact]
    public void Add_clamps_to_the_short_range()
    {
        Assert.Equal(short.MaxValue, Samples(PcmMix.Add(Pcm(short.MaxValue), Pcm(1)))[0]);
        Assert.Equal(short.MinValue, Samples(PcmMix.Add(Pcm(short.MinValue), Pcm(-1)))[0]);
    }

    [Fact]
    public void Buffer_take_pads_with_zeros_and_keeps_the_remainder()
    {
        var buffer = new PcmBuffer();
        buffer.Append([1, 2]);
        buffer.Append([]);
        buffer.Append([3, 4, 5]);
        Assert.Equal(5, buffer.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer.Take(3));
        Assert.Equal(2, buffer.Count);
        Assert.Equal(new byte[] { 4, 5, 0, 0 }, buffer.Take(4));
        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.Take(0));
    }

    [Fact]
    public void Buffer_trims_the_oldest_whole_frames()
    {
        var buffer = new PcmBuffer();
        buffer.Append([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        buffer.TrimTo(6, 2);
        Assert.Equal(6, buffer.Count);
        Assert.Equal(new byte[] { 4, 5, 6, 7, 8, 9 }, buffer.Take(6));

        buffer.Append([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        buffer.TrimTo(5, 2);
        Assert.Equal(6, buffer.Count);
        buffer.Take(6);

        buffer.Append([1, 2, 3, 4]);
        buffer.TrimTo(8, 2);
        buffer.TrimTo(1, 0);
        Assert.Equal(4, buffer.Count);
    }

    private static byte[] Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var index = 0; index < samples.Length; index++)
        {
            bytes[index * 2] = (byte)samples[index];
            bytes[index * 2 + 1] = (byte)(samples[index] >> 8);
        }

        return bytes;
    }

    private static short[] Samples(byte[] pcm)
    {
        var samples = new short[pcm.Length / 2];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = (short)(pcm[index * 2] | (pcm[index * 2 + 1] << 8));
        }

        return samples;
    }
}
