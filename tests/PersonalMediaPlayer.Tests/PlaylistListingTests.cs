using PersonalMediaPlayer.App.Download;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaylistListingTests
{
    [Fact]
    public void APlaylistLinkIsNotASingleVideo()
    {
        Assert.True(YoutubeDownloader.IsPlaylist("https://www.youtube.com/playlist?list=PLabc"));
        Assert.True(YoutubeDownloader.IsPlaylist("https://music.youtube.com/playlist?list=PLabc"));
        Assert.True(YoutubeDownloader.IsPlaylist("https://www.youtube.com/watch?list=PLabc"));
        Assert.False(YoutubeDownloader.IsPlaylist("https://www.youtube.com/watch?v=abc"));
        Assert.False(YoutubeDownloader.IsPlaylist("https://www.youtube.com/watch?v=abc&list=PLabc"));
        Assert.False(YoutubeDownloader.IsPlaylist("https://youtu.be/abc?list=PLabc"));
        Assert.False(YoutubeDownloader.IsPlaylist("https://example.com/playlist?list=PLabc"));
    }

    [Fact]
    public void PlaylistEntriesBecomeSeparateVideos()
    {
        var listing = YoutubeDownloader.ReadPlaylist("""
            {
              "title": "Course",
              "playlist_count": 4,
              "entries": [
                null,
                { "id": "aaa_1", "title": "One", "duration": 65 },
                { "id": "bbb", "url": "https://www.youtube.com/watch?v=bbb", "title": "Two", "duration": 3725 },
                { "title": "Missing" }
              ]
            }
            """);

        Assert.Equal("Course", listing.Title);
        Assert.Equal(4, listing.TotalCount);
        Assert.Equal(2, listing.Videos.Count);
        Assert.Equal("https://www.youtube.com/watch?v=aaa_1", listing.Videos[0].Url);
        Assert.Equal("One", listing.Videos[0].Title);
        Assert.Equal("1:05", listing.Videos[0].Length);
        Assert.Equal("https://www.youtube.com/watch?v=bbb", listing.Videos[1].Url);
        Assert.Equal("1:02:05", listing.Videos[1].Length);
        Assert.Contains(YoutubeDownloader.PlaylistQualities(), quality => quality.AudioOnly && quality.Format == "bestaudio/best");
        Assert.DoesNotContain(YoutubeDownloader.PlaylistQualities(), quality => quality.Format.Any(char.IsDigit) && !quality.Format.Contains("height", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyPlaylistIsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() => YoutubeDownloader.ReadPlaylist("""{ "title": "Empty", "entries": [null] }"""));
        Assert.Equal("This playlist has no videos.", error.Message);
    }
}
