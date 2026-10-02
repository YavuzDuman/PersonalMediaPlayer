using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class StreamThumbnailTests
{
    [Fact]
    public void AYouTubePageUsesItsRealThumbnail()
    {
        const string image = "https://i.ytimg.com/vi/jNQXAC9IVRw/hqdefault.jpg";
        Assert.Equal(image, StreamThumbnail.ForPage("https://www.youtube.com/watch?v=jNQXAC9IVRw"));
        Assert.Equal(image, StreamThumbnail.ForPage("https://youtu.be/jNQXAC9IVRw?t=12"));
        Assert.Equal(image, StreamThumbnail.ForPage("https://www.youtube.com/shorts/jNQXAC9IVRw"));
        Assert.Equal(image, StreamThumbnail.ForPage("https://music.youtube.com/watch?v=jNQXAC9IVRw&list=PLabc"));
        Assert.Equal(image, StreamThumbnail.Choose("https://www.youtube.com/watch?v=jNQXAC9IVRw", new Uri("https://cdn.example/temp.jpg")));
        Assert.Null(StreamThumbnail.ForPage("https://www.youtube.com/playlist?list=PLabc"));
        Assert.Null(StreamThumbnail.ForPage("https://example.com/video"));
    }

    [Fact]
    public void AnotherSiteUsesTheLookedUpThumbnail()
    {
        var lookedUp = new Uri("https://i.vimeocdn.com/video/123_640.jpg");
        Assert.Equal(lookedUp.AbsoluteUri, StreamThumbnail.Choose("https://vimeo.com/123", lookedUp));
        Assert.Null(StreamThumbnail.Choose("https://vimeo.com/123", null));

        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Hosted",
              "thumbnail": "https://cdn.example/poster.jpg",
              "thumbnails": [
                { "url": "https://cdn.example/small.jpg", "width": 120 },
                { "url": "https://cdn.example/large.jpg", "width": 1280 }
              ],
              "formats": [
                {
                  "format_id": "18",
                  "url": "https://cdn.example/video.mp4",
                  "vcodec": "avc1",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 720
                }
              ]
            }
            """);
        Assert.Equal("https://cdn.example/poster.jpg", choice.Thumbnail!.AbsoluteUri);

        var listed = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Listed",
              "thumbnails": [
                { "url": "https://cdn.example/small.jpg", "width": 120 },
                { "url": "https://cdn.example/story.mhtml", "width": 2000 },
                { "url": "https://cdn.example/large.jpg", "width": 640 }
              ],
              "formats": [
                {
                  "format_id": "18",
                  "url": "https://cdn.example/video.mp4",
                  "vcodec": "avc1",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 720
                }
              ]
            }
            """);
        Assert.Equal("https://cdn.example/large.jpg", listed.Thumbnail!.AbsoluteUri);
    }
}
