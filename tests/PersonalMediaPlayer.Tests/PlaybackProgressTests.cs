using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaybackProgressTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;
    private readonly string _folder;

    public PlaybackProgressTests()
    {
        Monitor.Enter(Gate);
        _folder = Path.Combine(Path.GetTempPath(), "pmp-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _store = Path.Combine(_folder, "positions.json");
        PlaybackProgress.StoreOverride = _store;
    }

    public void Dispose()
    {
        PlaybackProgress.StoreOverride = null;
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void UnfinishedKeepsTheMiddleOfAVideoAndAStream()
    {
        var video = Path.Combine(_folder, "clip.mp4");
        File.WriteAllText(video, "video");
        var missing = Path.Combine(_folder, "gone.mp4");
        PlaybackProgress.Save(video, 4_000, 120_000, "Too early");
        PlaybackProgress.Save(video, 20_000, 120_000, "Clip");
        PlaybackProgress.Save(missing, 30_000, 120_000, "Gone");
        PlaybackProgress.Save("https://www.youtube.com/watch?v=jNQXAC9IVRw", 90_000, 600_000, "Me at the zoo");
        PlaybackProgress.Save(video, 115_000, 120_000);

        Assert.Equal(0, PlaybackProgress.Load(video));
        var unfinished = PlaybackProgress.Unfinished();
        var stream = Assert.Single(unfinished);
        Assert.Equal("https://www.youtube.com/watch?v=jNQXAC9IVRw", stream.Key);
        Assert.Equal(90_000, stream.TimeMs);
        Assert.Equal(600_000, stream.DurationMs);
        Assert.Equal("Me at the zoo", stream.Title);
        Assert.Equal(90_000, PlaybackProgress.Load(stream.Key));

        PlaybackProgress.Save(video, 40_000, 120_000, "Clip");
        File.WriteAllText(_store, """
            {
              "https://www.youtube.com/watch?v=old": 15000
            }
            """);
        var legacy = Assert.Single(PlaybackProgress.Unfinished());
        Assert.Equal(15_000, legacy.TimeMs);
        Assert.Equal(0, legacy.DurationMs);

        var renamed = Path.Combine(_folder, "renamed.mp4");
        File.WriteAllText(renamed, "video");
        PlaybackProgress.Save(video, 40_000, 120_000, "Clip");
        PlaybackProgress.Move(video, renamed);
        Assert.Equal(40_000, PlaybackProgress.Load(renamed));
        Assert.Equal(0, PlaybackProgress.Load(video));
    }
}
