using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;
using PersonalMediaPlayer.Core.Models;
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
        LibraryCaptionSearch.Clear();
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

    [Fact]
    public void AStoredCaptionIsFoundByLibrarySearch()
    {
        LibraryCaptionSearch.Clear();
        var video = Path.Combine(_folder, "movie.mp4");
        var again = Path.Combine(_folder, "again.mp4");
        var photo = Path.Combine(_folder, "shot.png");
        var missing = Path.Combine(_folder, "gone.mp4");
        File.WriteAllText(video, "video");
        File.WriteAllText(again, "video");
        File.WriteAllText(photo, "photo");
        File.WriteAllText(missing, "video");
        File.WriteAllText(SubtitleCues.CacheFile(video), "1\n00:00:03,500 --> 00:00:04,000\n<b>Cached line</b>\n");
        File.WriteAllText(Path.Combine(_folder, "again.srt"), "1\n00:00:09,000 --> 00:00:10,000\nCached take\n");
        File.WriteAllText(Path.Combine(_folder, "shot.srt"), "1\n00:00:01,000 --> 00:00:02,000\nCached line\n");
        File.WriteAllText(Path.Combine(_folder, "gone.srt"), "1\n00:00:01,000 --> 00:00:02,000\nCached line\n");
        File.Delete(missing);

        var match = LibraryCaptionSearch.Search("cached",
        [
            Item(video, MediaKind.Video, "Movie"),
            Item(video, MediaKind.Video, "Movie copy"),
            Item(again, MediaKind.Recording, "Take"),
            Item(photo, MediaKind.Image, "Shot"),
            Item(missing, MediaKind.Video, "Gone", linked: true)
        ]);

        Assert.Equal(2, match.Hits.Count);
        Assert.Equal("Movie", match.Hits[0].Title);
        Assert.Equal(3_500, match.Hits[0].StartMs);
        Assert.Equal("Cached line", match.Hits[0].Line);
        Assert.Equal("Take", match.Hits[1].Title);
        Assert.Equal(9_000, match.Hits[1].StartMs);
        Assert.False(match.Truncated);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "cache"), "*.none"));
    }

    [Fact]
    public void ACaptionBesideTheVideoWinsOverTheCache()
    {
        LibraryCaptionSearch.Clear();
        var video = Path.Combine(_folder, "clip.mp4");
        File.WriteAllText(video, "video");
        File.WriteAllText(SubtitleCues.CacheFile(video), "1\n00:00:08,000 --> 00:00:09,000\nCached line\n");
        File.WriteAllText(Path.Combine(_folder, "clip.en.srt"), "1\n00:00:02,000 --> 00:00:03,000\nBeside line\n");

        var match = LibraryCaptionSearch.Search("line", [Item(video, MediaKind.Video, "Clip")]);

        var hit = Assert.Single(match.Hits);
        Assert.Equal("Beside line", hit.Line);
        Assert.Equal(2_000, hit.StartMs);
    }

    [Fact]
    public void AnEmptyCaptionBesideTheVideoHidesTheCache()
    {
        LibraryCaptionSearch.Clear();
        var video = Path.Combine(_folder, "clip.mp4");
        File.WriteAllText(video, "video");
        File.WriteAllText(SubtitleCues.CacheFile(video), "1\n00:00:01,000 --> 00:00:02,000\nCached line\n");
        File.WriteAllText(Path.Combine(_folder, "clip.srt"), "");

        Assert.Empty(LibraryCaptionSearch.Search("cached", [Item(video, MediaKind.Video, "Clip")]).Hits);
    }

    [Fact]
    public void AVideoWithNoStoredCaptionIsSkipped()
    {
        LibraryCaptionSearch.Clear();
        var video = Path.Combine(_folder, "plain.mp4");
        File.WriteAllText(video, "video");
        var cache = Path.Combine(_folder, "cache");
        var before = Directory.GetFiles(cache);

        var match = LibraryCaptionSearch.Search("hello", [Item(video, MediaKind.Video, "Plain")]);

        Assert.Empty(match.Hits);
        Assert.Equal(before, Directory.GetFiles(cache));
    }

    [Fact]
    public void ARememberedMissIsSkipped()
    {
        LibraryCaptionSearch.Clear();
        var video = Path.Combine(_folder, "silent.mp4");
        File.WriteAllText(video, "video");
        File.WriteAllBytes(SubtitleCues.CacheFile(video) + ".none", []);

        Assert.Empty(LibraryCaptionSearch.Search("hello", [Item(video, MediaKind.Video, "Silent")]).Hits);
    }

    [Fact]
    public void AChangedCaptionIsReadAgain()
    {
        LibraryCaptionSearch.Clear();
        var video = Path.Combine(_folder, "clip.mp4");
        var srt = Path.Combine(_folder, "clip.srt");
        File.WriteAllText(video, "video");
        File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:02,000\nfirst word\n");
        var item = Item(video, MediaKind.Video, "Clip");

        Assert.Equal("first word", Assert.Single(LibraryCaptionSearch.Search("word", [item]).Hits).Line);

        File.WriteAllText(srt, "1\n00:00:04,000 --> 00:00:05,000\nsecond word\n");
        File.SetLastWriteTimeUtc(srt, DateTime.UtcNow.AddMinutes(1));

        var hit = Assert.Single(LibraryCaptionSearch.Search("word", [item]).Hits);
        Assert.Equal("second word", hit.Line);
        Assert.Equal(4_000, hit.StartMs);
    }

    private static MediaItem Item(string path, MediaKind kind, string name, bool linked = false)
        => new()
        {
            Id = path,
            Kind = kind,
            DisplayName = name,
            FilePath = path,
            IsLinked = linked
        };
}
