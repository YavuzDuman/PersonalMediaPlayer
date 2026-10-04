using System.Text.Json;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class VideoChapterTests
{
    [Fact]
    public void LookupChaptersStayInTimeOrder()
    {
        var chapters = Read("""
            {
              "chapters": [
                { "start_time": 90.5, "title": "  Harbor  " },
                { "start_time": 0, "title": "Intro" },
                { "start_time": 90.5, "title": "Duplicate" },
                { "start_time": -1, "title": "Nope" },
                { "title": "Missing time" },
                { "start_time": 10, "title": "Line\nTwo" }
              ]
            }
            """);

        Assert.Equal(3, chapters.Count);
        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal(0, chapters[0].StartMs);
        Assert.Equal("Line Two", chapters[1].Title);
        Assert.Equal(10_000, chapters[1].StartMs);
        Assert.Equal("Harbor", chapters[2].Title);
        Assert.Equal(90_500, chapters[2].StartMs);
        Assert.Equal("01:30", VideoChapters.Format(chapters[2].StartMs));
    }

    [Fact]
    public void ABlankTitleUsesTheChapterNumber()
    {
        var chapters = VideoChapters.FromOffsets([(0, "Cold open"), (65_000, "   ")]);

        Assert.Equal("Cold open", chapters[0].Title);
        Assert.Equal("Chapter 2", chapters[1].Title);
        Assert.Equal(65_000, chapters[1].StartMs);
    }

    [Fact]
    public void OneUnnamedChapterAtTheStartIsHidden()
    {
        Assert.Empty(VideoChapters.FromOffsets([(0, "  ")]));
        Assert.Empty(Read("""{"title":"No chapters"}"""));
    }

    [Fact]
    public void ANamedChapterAtTheStartStays()
    {
        var chapters = VideoChapters.FromOffsets([(0, "Only the opening")]);

        Assert.Equal("Only the opening", Assert.Single(chapters).Title);
    }

    [Fact]
    public void TheChapterButtonAndListFollowTheOpenChoice()
    {
        Assert.True(VideoChapters.ListOpen);
        Assert.Equal(new ChapterListChoice(false, false), VideoChapters.Choose(listOpen: true, mini: false, hasSession: true, count: 0));
        Assert.Equal(new ChapterListChoice(false, false), VideoChapters.Choose(listOpen: false, mini: false, hasSession: true, count: 0));
        Assert.Equal(new ChapterListChoice(false, false), VideoChapters.Choose(listOpen: true, mini: true, hasSession: true, count: 4));
        Assert.Equal(new ChapterListChoice(false, false), VideoChapters.Choose(listOpen: true, mini: false, hasSession: false, count: 4));
        Assert.Equal(new ChapterListChoice(true, true), VideoChapters.Choose(listOpen: true, mini: false, hasSession: true, count: 4));
        Assert.Equal(new ChapterListChoice(true, false), VideoChapters.Choose(listOpen: false, mini: false, hasSession: true, count: 4));
    }

    [Fact]
    public void ThePlayingChapterIsTheLatestStartAtOrBeforeNow()
    {
        var chapters = VideoChapters.FromOffsets([(0, "Intro"), (1_000, "Middle"), (5_000, "End")]);

        Assert.Equal(-1, VideoChapters.Current([], 0));
        Assert.Equal(0, VideoChapters.Current(chapters, 0));
        Assert.Equal(0, VideoChapters.Current(chapters, 999));
        Assert.Equal(1, VideoChapters.Current(chapters, 1_000));
        Assert.Equal(2, VideoChapters.Current(chapters, 500_000));
    }

    [Fact]
    public void ALongTitleIsShortened()
    {
        var chapters = VideoChapters.FromOffsets([(0, new string('a', 150))]);

        Assert.Equal(120, Assert.Single(chapters).Title.Length);
    }

    [Fact]
    public void ReadPlaybackKeepsChaptersOnThePageLookup()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Harbor",
              "chapters": [
                { "start_time": 0, "title": "Start", "end_time": 12 },
                { "start_time": 12, "title": "Middle", "end_time": 40 }
              ],
              "formats": [
                {
                  "format_id": "18",
                  "url": "https://cdn.example/video.mp4",
                  "vcodec": "avc1.4d401e",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 720,
                  "tbr": 500
                }
              ]
            }
            """);

        Assert.Equal("https://cdn.example/video.mp4", choice.Media.AbsoluteUri);
        Assert.Equal("Start", choice.Chapters[0].Title);
        Assert.Equal("Middle", choice.Chapters[1].Title);
        Assert.Equal(12_000, choice.Chapters[1].StartMs);
    }

    [Fact]
    public void ReadPlaybackWithoutChaptersLeavesTheListEmpty()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Plain",
              "formats": [
                {
                  "format_id": "18",
                  "url": "https://cdn.example/plain.mp4",
                  "vcodec": "avc1.4d401e",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 720,
                  "tbr": 500
                }
              ]
            }
            """);

        Assert.Empty(choice.Chapters);
    }

    private static IReadOnlyList<VideoChapter> Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return VideoChapters.FromLookup(document.RootElement);
    }
}
