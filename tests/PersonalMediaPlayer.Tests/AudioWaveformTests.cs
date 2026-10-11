using PersonalMediaPlayer.App.Editing;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class AudioWaveformTests
{
    [Fact]
    public void Quiet_samples_stay_shorter_than_the_loud_ones()
    {
        var levels = AudioWaveform.FromSamples([0.1f, -0.1f, 0.5f], sampleRate: 1, peaksPerSecond: 1, maxPeaks: 10);

        Assert.Equal(3, levels.Peaks.Length);
        Assert.Equal(0.2f, levels.Peaks[0], 3);
        Assert.Equal(1f, levels.Peaks[2], 3);
        Assert.Equal(3, levels.DurationSeconds, 3);
    }

    [Fact]
    public void Silence_stays_flat()
    {
        var levels = AudioWaveform.FromSamples([0f, 0f, 0f], sampleRate: 1, peaksPerSecond: 1, maxPeaks: 10);

        Assert.All(levels.Peaks, peak => Assert.Equal(0f, peak));
    }

    [Fact]
    public void Zoom_and_scroll_keep_a_loud_moment_in_place()
    {
        var peaks = new float[10];
        peaks[8] = 1f;
        var levels = new AudioWaveform.Levels(peaks, 10);

        var wide = AudioWaveform.Window(levels, 10, 0, 1, 10);
        Assert.Equal(0f, wide[0]);
        Assert.Equal(1f, wide[8]);

        var start = AudioWaveform.Window(levels, 10, 0, 0.2, 4);
        Assert.All(start, peak => Assert.Equal(0f, peak));

        var later = AudioWaveform.Window(levels, 10, 0.7, 0.95, 4);
        Assert.Contains(later, peak => peak > 0.9f);
    }

    [Fact]
    public void A_long_file_keeps_its_length_and_its_ending()
    {
        var samples = new float[1000];
        samples[990] = 1f;
        var levels = AudioWaveform.FromSamples(samples, sampleRate: 100, peaksPerSecond: 10, maxPeaks: 4);

        Assert.InRange(levels.Peaks.Length, 1, 4);
        Assert.Equal(10, levels.DurationSeconds, 3);
        Assert.Equal(0f, AudioWaveform.Window(levels, 10, 0, 0.2, 4).Max());
        Assert.True(AudioWaveform.Window(levels, 10, 0.9, 1, 2).Max() > 0.9f);
    }

    [Fact]
    public void A_tall_bar_fills_the_lane_and_a_quiet_bar_stays_a_line()
    {
        var pixels = new byte[4 * 8 * 4];
        AudioWaveform.Paint([1f, 0f], pixels, width: 4, height: 8, blue: 10, green: 20, red: 30, alpha: 200);

        Assert.Equal(200, pixels[(1 * 4 + 0) * 4 + 3]);
        Assert.Equal(0, pixels[(0 * 4 + 2) * 4 + 3]);
        Assert.Equal(200, pixels[(3 * 4 + 2) * 4 + 3]);
    }

    [Fact]
    public void A_rewritten_file_does_not_reuse_the_old_waveform()
    {
        var before = new AudioWaveform.WaveformKey(@"C:\clips\scene.mp4", 10, 1);
        var after = new AudioWaveform.WaveformKey(@"C:\clips\scene.mp4", 24, 2);
        AudioWaveform.Remember(before, new AudioWaveform.Levels([0.2f, 1f], 2));

        Assert.True(AudioWaveform.TryGet(before, out var cached));
        Assert.Equal(1f, cached.Peaks[1]);
        Assert.False(AudioWaveform.TryGet(after, out _));
    }

    [Fact]
    public async Task A_saved_file_with_a_quiet_start_and_a_loud_ending_reads_back_that_way()
    {
        var ffmpeg = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalMediaPlayer",
            "tools",
            "ffmpeg.exe");
        if (!File.Exists(ffmpeg) && Environment.GetEnvironmentVariable("PATH")?.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "pmp-wave-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteWave(path);
            var levels = await AudioWaveform.ReadAsync(path);
            Assert.NotNull(levels);
            Assert.True(levels.Value.HasPeaks);
            Assert.InRange(levels.Value.DurationSeconds, 1.5, 2.5);
            var window = AudioWaveform.Window(levels.Value, levels.Value.DurationSeconds, 0, 1, 8);
            Assert.True(window[0] < 0.25f);
            Assert.True(window[^1] > 0.75f);

            var again = await AudioWaveform.ReadAsync(path);
            Assert.Equal(levels.Value.Peaks.Length, again!.Value.Peaks.Length);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void WriteWave(string path)
    {
        const int rate = 8000;
        var samples = new short[rate * 2];
        for (var index = rate; index < samples.Length; index++)
        {
            samples[index] = index / 20 % 2 == 0 ? (short)20000 : (short)-20000;
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataBytes = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }
}
