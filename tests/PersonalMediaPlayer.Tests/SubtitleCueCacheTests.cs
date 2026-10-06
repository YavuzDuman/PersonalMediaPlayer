using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class SubtitleCueCacheTests : IDisposable
{
    private readonly string _folder;

    public SubtitleCueCacheTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "pmp-cues-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(_folder, "cache");
        Directory.CreateDirectory(cache);
        SubtitleCues.CacheDirectoryOverride = cache;
    }

    public void Dispose()
    {
        SubtitleCues.CacheDirectoryOverride = null;
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ASubtitleBesideTheVideoIsRead()
    {
        var video = Path.Combine(_folder, "clip.mp4");
        File.WriteAllText(video, "not a video");
        File.WriteAllText(Path.Combine(_folder, "clip.en.srt"), "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        var cues = SubtitleCues.LoadFor(video);

        var cue = Assert.Single(cues);
        Assert.Equal("Hello", cue.Text);
        Assert.Equal(1_000, cue.StartMs);
    }

    [Fact]
    public void ACachedSubtitleIsRead()
    {
        var video = Path.Combine(_folder, "movie.mkv");
        File.WriteAllText(video, "x");
        File.WriteAllText(SubtitleCues.CacheFile(video), "1\n00:00:03,000 --> 00:00:04,000\nCached\n");

        var cues = SubtitleCues.LoadFor(video);

        Assert.Equal("Cached", Assert.Single(cues).Text);
    }

    [Fact]
    public void ARememberedMissDoesNotLookAgain()
    {
        var video = Path.Combine(_folder, "silent.mp4");
        File.WriteAllText(video, "x");
        File.WriteAllBytes(SubtitleCues.CacheFile(video) + ".none", []);

        Assert.Empty(SubtitleCues.LoadFor(video));
    }
}
