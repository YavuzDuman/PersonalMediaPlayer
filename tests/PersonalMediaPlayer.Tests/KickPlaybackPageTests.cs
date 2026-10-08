using PersonalMediaPlayer.App.Download;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class KickPlaybackPageTests
{
    [Fact]
    public void AKickAddressSlugIsNotAUuid()
    {
        Assert.True(YoutubeDownloader.TryKickVideoSlug(
            "https://www.kick.com/KokoroCam/videos/05d7d9a01706ff9d-247-cat?ref=1",
            out var channel,
            out var slug));
        Assert.Equal("KokoroCam", channel);
        Assert.Equal("05d7d9a01706ff9d-247-cat", slug);

        Assert.False(YoutubeDownloader.TryKickVideoSlug(
            "https://kick.com/kokorocam/videos/a25d6cac-988d-44b4-ad58-747e002758a7",
            out _,
            out _));
        Assert.False(YoutubeDownloader.TryKickVideoSlug("https://kick.com/kokorocam", out _, out _));
        Assert.False(YoutubeDownloader.TryKickVideoSlug(
            "https://kick.com/mxddy/clips/clip_01GYXVB5Y8PWAPWCWMSBCFB05X",
            out _,
            out _));
        Assert.False(YoutubeDownloader.TryKickVideoSlug("https://www.youtube.com/watch?v=abc", out _, out _));
    }

    [Fact]
    public void AKickSavedVideoUsesThePlaylistFromTheChannelList()
    {
        const string json = """
            [
              {
                "slug": "48cdec06-cookies",
                "session_title": "COOKIES",
                "is_live": false,
                "source": "https://stream.kick.com/example/media/hls/master.m3u8",
                "thumbnail": { "src": "https://images.kick.com/example.jpg" },
                "video": { "uuid": "2a423d10-372d-4b8d-8b12-6239e05c8760" }
              }
            ]
            """;

        var bySlug = YoutubeDownloader.ReadKickListedVideo(json, "48cdec06-cookies");
        var byId = YoutubeDownloader.ReadKickListedVideo(json, "2a423d10-372d-4b8d-8b12-6239e05c8760");

        Assert.NotNull(bySlug);
        Assert.Equal("COOKIES", bySlug.Title);
        Assert.Equal("https://stream.kick.com/example/media/hls/master.m3u8", bySlug.Source.AbsoluteUri);
        Assert.Equal("https://images.kick.com/example.jpg", bySlug.Thumbnail!.AbsoluteUri);
        Assert.False(bySlug.Live);
        Assert.Equal(bySlug.Source, byId!.Source);
        Assert.Null(YoutubeDownloader.ReadKickListedVideo(json, "missing"));
    }

    [Fact]
    public void AKickSavedVideoUsesTheRecordingForTheVideoId()
    {
        const string channel = """{"id":875396,"slug":"adinross"}""";
        const string video = """
            {
              "data": {
                "id": "01a02be6-62d0-76a1-b338-0a9c3f3b8175",
                "title": "Brand Risk Promotions #15",
                "is_live": false,
                "recording_url": "https://stream.kick.com/example/media/hls/master.m3u8",
                "thumbnail": { "src": "https://images.kick.com/example.jpg" }
              },
              "message": "Success"
            }
            """;

        Assert.Equal(875396, YoutubeDownloader.ReadKickChannelId(channel));
        var saved = YoutubeDownloader.ReadKickWebVideo(video, "01a02be6-62d0-76a1-b338-0a9c3f3b8175");
        Assert.NotNull(saved);
        Assert.Equal("Brand Risk Promotions #15", saved.Title);
        Assert.Equal("https://stream.kick.com/example/media/hls/master.m3u8", saved.Source.AbsoluteUri);
        Assert.Equal("https://images.kick.com/example.jpg", saved.Thumbnail!.AbsoluteUri);
        Assert.False(saved.Live);
        Assert.Null(YoutubeDownloader.ReadKickWebVideo(video, "other-id"));
        Assert.Null(YoutubeDownloader.ReadKickWebVideo("""{"data":{"id":"01a02be6-62d0-76a1-b338-0a9c3f3b8175","recording_url":""}}""", "01a02be6-62d0-76a1-b338-0a9c3f3b8175"));
        Assert.Null(YoutubeDownloader.ReadKickChannelId("""{"slug":"adinross"}"""));
    }

    [Fact]
    public void DownloadLinksStayOnYouTube()
    {
        Assert.True(YoutubeDownloader.TryNormalize("https://www.youtube.com/watch?v=aqz-KE-bpKQ", out _));
        Assert.False(YoutubeDownloader.TryNormalize("https://www.dailymotion.com/video/x87d4ev", out _));
        Assert.False(YoutubeDownloader.TryNormalize("https://www.twitch.tv/videos/2888890850", out _));
        Assert.False(YoutubeDownloader.TryNormalize("https://www.twitch.tv/summit1g", out _));
        Assert.False(YoutubeDownloader.TryNormalize("https://kick.com/kokorocam", out _));
        Assert.False(YoutubeDownloader.TryNormalize(
            "https://kick.com/kokorocam/videos/05d7d9a01706ff9d-247-cat",
            out _));
    }

    [Fact]
    public void PlaybackLookupStaysAPlainDump()
    {
        const string page = "https://kick.com/kokorocam/videos/05d7d9a01706ff9d-247-cat";
        var args = YoutubeDownloader.PlaybackLookupArgs(null, page);

        Assert.Contains(page, args);
        Assert.Contains("--dump-single-json", args);
        Assert.Contains("-4", args);
        Assert.DoesNotContain("--impersonate", args);
        Assert.DoesNotContain("--write-auto-subs", args);
        Assert.DoesNotContain("--live-from-start", args);
    }
}
