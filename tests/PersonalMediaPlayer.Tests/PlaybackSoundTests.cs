using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaybackSoundTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;

    public PlaybackSoundTests()
    {
        Monitor.Enter(Gate);
        _store = Path.Combine(Path.GetTempPath(), "pmp-playback-sound-" + Guid.NewGuid().ToString("N") + ".json");
        PlaybackSound.StoreOverride = _store;
    }

    public void Dispose()
    {
        PlaybackSound.StoreOverride = null;
        if (File.Exists(_store))
        {
            File.Delete(_store);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void AMissingFileLeavesTheSoundUnchanged()
    {
        var sound = PlaybackSound.Current;
        Assert.Equal(PlaybackSoundState.Default, sound);
        Assert.Equal(SoundPreset.Flat, sound.Preset);
        Assert.False(sound.Normalize);
        Assert.Equal("0 dB", PlaybackSound.GainLabel(0));
        Assert.Equal("+4 dB", PlaybackSound.GainLabel(4));
        Assert.Equal("-3 dB", PlaybackSound.GainLabel(-3));
        Assert.Empty(PlaybackSound.MediaOptions(sound));
    }

    [Fact]
    public void BassAndNormalizationApplyTogetherAndResetClearsThem()
    {
        var raises = 0;
        void Count() => raises++;
        PlaybackSound.Changed += Count;
        try
        {
            var saved = new PlaybackSoundState(SoundPreset.Bass, 3, -1, 2, true);
            PlaybackSound.Save(saved, apply: true);
            Assert.Equal(1, raises);
            Assert.Equal(saved, PlaybackSound.Current);
            var options = PlaybackSound.MediaOptions(PlaybackSound.Current);
            Assert.Contains(":audio-filter=equalizer:compressor", options);
            Assert.Contains(":equalizer-vlcfreqs=1", options);
            Assert.Contains(":equalizer-preamp=-2", options);
            Assert.Contains(":equalizer-bands=6 5 4 1 0 0 -1 -2 -2 -2", options);
            Assert.Contains(":compressor-makeup-gain=8", options);
            Assert.Contains(":compressor-threshold=-18", options);
            Assert.Single(options, option => option.StartsWith(":audio-filter=", StringComparison.Ordinal));

            PlaybackSound.Save(PlaybackSound.Current, apply: true);
            Assert.Equal(1, raises);

            var custom = PlaybackSound.Current with { Preset = SoundPreset.Custom, Bass = 8, Voice = 0, Treble = -6 };
            PlaybackSound.Save(custom, apply: false);
            Assert.Equal(1, raises);
            PlaybackSound.Commit();
            Assert.Equal(2, raises);
            Assert.Contains(":equalizer-bands=8 8 4 0 0 0 -3 -6 -6 -6", PlaybackSound.MediaOptions(PlaybackSound.Current));
            Assert.Contains(":audio-filter=equalizer:compressor", PlaybackSound.MediaOptions(PlaybackSound.Current));

            PlaybackSound.Reset();
            Assert.Equal(3, raises);
            Assert.Equal(PlaybackSoundState.Default, PlaybackSound.Current);
            Assert.Empty(PlaybackSound.MediaOptions(PlaybackSound.Current));
            Assert.Equal(PlaybackSoundState.Default, PlaybackSound.Parse(File.ReadAllText(_store)));
        }
        finally
        {
            PlaybackSound.Changed -= Count;
        }
    }

    [Fact]
    public void ABadFieldKeepsTheRestAndEachShapeStaysSeparate()
    {
        var parsed = PlaybackSound.Parse("""
            {"Preset":"nope","Bass":99,"Voice":"x","Treble":-40,"Normalize":true}
            """);
        Assert.Equal(SoundPreset.Flat, parsed.Preset);
        Assert.Equal(12, parsed.Bass);
        Assert.Equal(0, parsed.Voice);
        Assert.Equal(-12, parsed.Treble);
        Assert.True(parsed.Normalize);
        var leveled = PlaybackSound.MediaOptions(parsed);
        Assert.Contains(":audio-filter=compressor", leveled);
        Assert.DoesNotContain(leveled, option => option.StartsWith(":equalizer-", StringComparison.Ordinal));

        var numbered = PlaybackSound.Parse("""{"Preset":"1","Normalize":true}""");
        Assert.Equal(SoundPreset.Flat, numbered.Preset);
        Assert.True(numbered.Normalize);

        var custom = PlaybackSound.Parse("""{"Preset":"voice","Normalize":false}""");
        Assert.Equal(SoundPreset.Voice, custom.Preset);
        Assert.Contains(":audio-filter=equalizer", PlaybackSound.MediaOptions(custom));
        Assert.DoesNotContain(PlaybackSound.MediaOptions(custom), option => option.Contains("compressor", StringComparison.Ordinal));

        var silentCustom = new PlaybackSoundState(SoundPreset.Custom, 0, 0, 0, false);
        Assert.Empty(PlaybackSound.MediaOptions(silentCustom));
        Assert.Equal(PlaybackSoundState.Default, PlaybackSound.Parse("{"));

        Assert.Equal(200, PlaybackVolume.Limit(250, 80));
        Assert.Equal(200, PlaybackVolume.Limit(199.6, 80));
        Assert.Equal(150, PlaybackVolume.Limit(150.2, 80));
        Assert.Equal(0, PlaybackVolume.Limit(-4, 80));
        Assert.Equal(80, PlaybackVolume.Limit(double.NaN, 80));
        Assert.Equal(100, PlaybackVolume.Limit(100, 80));
    }
}
