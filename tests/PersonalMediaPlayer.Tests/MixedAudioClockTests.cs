using PersonalMediaPlayer.App.Capture;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class MixedAudioClockTests
{
    [Fact]
    public void CatchUp_keeps_the_start_of_the_microphone_while_speakers_stay_silent()
    {
        const int bytesPerSecond = 16_000;
        var clock = new MixedAudioClock(bytesPerSecond, 2, TimeSpan.FromMilliseconds(100));
        var expected = new byte[bytesPerSecond * 15];
        for (var index = 0; index < expected.Length; index++)
        {
            expected[index] = (byte)(index % 251);
        }

        var produced = new List<MixedAudioChunk>();
        var cursor = 0;
        var packet = bytesPerSecond / 100;
        while (cursor < expected.Length)
        {
            var size = Math.Min(packet, expected.Length - cursor);
            var slice = new byte[size];
            Buffer.BlockCopy(expected, cursor, slice, 0, size);
            clock.AppendMicrophone(slice);
            cursor += size;
            var elapsed = TimeSpan.FromTicks(cursor * (long)TimeSpan.TicksPerSecond / bytesPerSecond);
            produced.AddRange(clock.CatchUp(elapsed));
        }

        produced.AddRange(clock.CatchUp(TimeSpan.FromSeconds(15), flushShort: true));
        var output = produced.SelectMany(chunk => chunk.Pcm).ToArray();
        Assert.Equal(expected, output);

        var stamp = TimeSpan.Zero;
        foreach (var chunk in produced)
        {
            Assert.Equal(stamp, chunk.Stamp);
            stamp += chunk.Duration;
        }

        Assert.Equal(TimeSpan.FromSeconds(15), stamp);
        Assert.Equal(expected[0], output[0]);
        Assert.Equal(expected[^1], output[^1]);
    }

    [Fact]
    public void CatchUp_adds_speaker_and_microphone_on_the_same_clock()
    {
        var clock = new MixedAudioClock(200, 2, TimeSpan.FromMilliseconds(100));
        clock.AppendSpeaker(Pcm(1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000));
        clock.AppendMicrophone(Pcm(5, 5, 5, 5, 5, 5, 5, 5, 5, 5));
        var mixed = clock.CatchUp(TimeSpan.FromMilliseconds(100)).SelectMany(chunk => chunk.Pcm).ToArray();
        Assert.Equal(Pcm(1005, 1005, 1005, 1005, 1005, 1005, 1005, 1005, 1005, 1005), mixed);
    }

    [Fact]
    public void CatchUp_holds_a_short_tail_until_stop()
    {
        var clock = new MixedAudioClock(2_000, 2, TimeSpan.FromMilliseconds(100));
        var microphone = new byte[80];
        for (var index = 0; index < microphone.Length; index++)
        {
            microphone[index] = (byte)(index + 1);
        }

        clock.AppendMicrophone(microphone);
        Assert.Empty(clock.CatchUp(TimeSpan.FromMilliseconds(40)));
        var flushed = clock.CatchUp(TimeSpan.FromMilliseconds(40), flushShort: true).SelectMany(chunk => chunk.Pcm).ToArray();
        Assert.Equal(microphone, flushed);
    }

    [Fact]
    public void Append_without_catch_up_keeps_only_the_latest_five_seconds()
    {
        var clock = new MixedAudioClock(1_000, 2, TimeSpan.FromMilliseconds(100));
        var input = new byte[6_000];
        for (var index = 0; index < input.Length; index++)
        {
            input[index] = (byte)(index % 251);
        }

        clock.AppendMicrophone(input);
        var output = clock.CatchUp(TimeSpan.FromSeconds(6), flushShort: true).SelectMany(chunk => chunk.Pcm).ToArray();
        Assert.Equal(6_000, output.Length);
        Assert.Equal(input[1000..], output[..5_000]);
        Assert.All(output[5_000..], value => Assert.Equal(0, value));
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
}
