using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PreviewVolumeTests
{
    [Fact]
    public void PreviewVolumeStaysOutOfTheWatchingFile()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalMediaPlayer",
            "playback-volume.json");
        var existed = File.Exists(path);
        var before = existed ? File.ReadAllText(path) : null;

        PlaybackVolume.SavePreview(25);
        PlaybackVolume.SavePreview(0);

        var state = PlaybackVolume.LoadPreview();
        Assert.Equal(0, state.Level);
        Assert.Equal(25, state.Audible);
        if (existed)
        {
            Assert.Equal(before, File.ReadAllText(path));
        }
        else
        {
            Assert.False(File.Exists(path));
        }
    }
}
