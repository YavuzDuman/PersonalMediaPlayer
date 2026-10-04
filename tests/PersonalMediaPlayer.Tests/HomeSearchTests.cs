using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core.Models;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class HomeSearchTests
{
    [Fact]
    public void AnEmptyQueryMatchesNothing()
    {
        var library = new[] { File("alpha.png", MediaKind.Image) };
        var playlists = new[] { List("evening", "Evening", Page("https://www.youtube.com/watch?v=abcdefghijk", "Alpha talk")) };

        Assert.Empty(HomeSearch.Find(null, library, playlists));
        Assert.Empty(HomeSearch.Find("   ", library, playlists));
    }

    [Fact]
    public void LibraryFilesKeepTheirOwnSource()
    {
        var library = new[]
        {
            File("alpha.png", MediaKind.Image, @"C:\library\alpha.png", "Trip"),
            File("mid.mp4", MediaKind.Video, @"C:\library\mid.mp4", "Videos"),
            File("take.mp4", MediaKind.Recording, @"C:\library\take.mp4", LibraryFolder.Recordings),
            File("shot.png", MediaKind.Image, @"C:\library\shot.png", LibraryFolder.Screenshots),
            File("other.txt", MediaKind.Image, @"C:\library\other.png", "Trip")
        };

        var hits = HomeSearch.Find("a", library, []);

        Assert.Equal(["alpha.png", "take.mp4"], hits.Select(hit => hit.Title).ToArray());
        Assert.Equal(HomeHitKind.LibraryPhoto, hits[0].Kind);
        Assert.Equal("Library · Photo", hits[0].Source);
        Assert.Equal(@"C:\library\alpha.png", hits[0].FilePath);
        Assert.Null(hits[0].PlaylistId);
        Assert.Equal(HomeHitKind.LibraryVideo, hits[1].Kind);
        Assert.Equal("Library · Video", hits[1].Source);
        Assert.Null(hits[0].Thumbnail);
        Assert.Null(hits[1].Thumbnail);
    }

    [Fact]
    public void AScreenshotIsAPhotoAndARecordingIsAVideo()
    {
        var shot = File("board.png", MediaKind.Image, @"C:\library\board.png", LibraryFolder.Screenshots);
        var take = File("board.mp4", MediaKind.Recording, @"C:\library\board.mp4", "Clips");

        var hits = HomeSearch.Find("board", [shot, take], []);

        var photo = Assert.Single(hits, hit => hit.Kind == HomeHitKind.LibraryPhoto);
        var video = Assert.Single(hits, hit => hit.Kind == HomeHitKind.LibraryVideo);
        Assert.Equal("Library · Photo", photo.Source);
        Assert.Equal("Library · Video", video.Source);
    }

    [Fact]
    public void TheSameOnlineVideoInSeveralPlaylistsIsOneResult()
    {
        var watch = "https://www.youtube.com/watch?v=abcdefghijk";
        var shortLink = "https://youtu.be/abcdefghijk?t=12";
        var playlists = new[]
        {
            List("evening", "Evening", Page(watch, "Harbor talk"), Page(watch, "Harbor talk")),
            List("weekend", "Weekend", Page("https://example.com/other", "Other"), Page(shortLink, "Harbor clip")),
            List("later", "Later", Page("https://www.youtube.com/watch?v=zzzzzzzzzzz", "Zebra"))
        };

        var hits = HomeSearch.Find("harbor", [], playlists);

        var hit = Assert.Single(hits);
        Assert.Equal("Harbor talk", hit.Title);
        Assert.Equal("Video · Playlists · Evening, Weekend", hit.Source);
        Assert.Equal(StreamThumbnail.ForPage(watch), hit.Thumbnail);
        Assert.Equal(HomeHitKind.Online, hit.Kind);
        Assert.Equal("evening", hit.PlaylistId);
        Assert.Equal(0, hit.PlaylistIndex);
        Assert.Null(hit.FilePath);
    }

    [Fact]
    public void OnePlaylistUsesASingularLabel()
    {
        var playlists = new[] { List("evening", "Evening", Page("https://example.com/harbor", "Harbor talk")) };

        var hit = Assert.Single(HomeSearch.Find("harbor", [], playlists));

        Assert.Equal("Video · Playlist · Evening", hit.Source);
        Assert.Null(hit.Thumbnail);
        Assert.Equal("evening", hit.PlaylistId);
        Assert.Equal(0, hit.PlaylistIndex);
    }

    [Fact]
    public void ALocalVideoInPlaylistsStaysOneLibraryResult()
    {
        var file = File("harbor.mp4", MediaKind.Video, @"C:\library\harbor.mp4");
        var photo = File("harbor.png", MediaKind.Image, @"C:\library\harbor.png");
        var playlists = new[]
        {
            List("evening", "Evening", new PlaylistEntry { Location = file.FilePath }, new PlaylistEntry { Location = photo.FilePath }),
            List("weekend", "Weekend", new PlaylistEntry { Location = file.FilePath.ToLowerInvariant() }),
            List("later", "Later", new PlaylistEntry { Location = @"C:\clips\only.mp4" })
        };

        var hits = HomeSearch.Find("harbor", [file, photo], playlists);

        var video = Assert.Single(hits, hit => hit.Kind == HomeHitKind.LibraryVideo);
        var picture = Assert.Single(hits, hit => hit.Kind == HomeHitKind.LibraryPhoto);
        Assert.Equal("Library · Video · Playlists · Evening, Weekend", video.Source);
        Assert.Equal(file.FilePath, video.FilePath);
        Assert.Null(video.PlaylistId);
        Assert.Null(video.Thumbnail);
        Assert.Equal("Library · Photo", picture.Source);
        Assert.Empty(HomeSearch.Find("only", [file, photo], playlists));
    }

    [Fact]
    public void AnOnlineResultUsesASavedPictureWhenThePageHasNone()
    {
        var page = "https://example.com/harbor";
        var plain = Page(page, "Harbor talk");
        var pictured = Page(page, "Harbor talk");
        pictured.Thumbnail = "https://cdn.example/harbor.jpg";
        var playlists = new[]
        {
            List("evening", "Evening", plain),
            List("weekend", "Weekend", pictured)
        };

        var hit = Assert.Single(HomeSearch.Find("harbor", [], playlists));

        Assert.Equal("https://cdn.example/harbor.jpg", hit.Thumbnail);
        Assert.Equal("Video · Playlists · Evening, Weekend", hit.Source);
    }

    [Fact]
    public void ALibraryFileAndAnOnlineVideoStaySeparate()
    {
        var file = File("harbor.png", MediaKind.Image, @"C:\library\harbor.png");
        var playlists = new[] { List("evening", "Evening", Page("https://example.com/watch", "Harbor talk")) };

        var hits = HomeSearch.Find("HARBOR", [file], playlists);

        var photo = Assert.Single(hits, hit => hit.Kind == HomeHitKind.LibraryPhoto);
        var online = Assert.Single(hits, hit => hit.Kind == HomeHitKind.Online);
        Assert.Equal("Library · Photo", photo.Source);
        Assert.Equal("Video · Playlist · Evening", online.Source);
    }

    [Fact]
    public void ThePlaylistNameAloneDoesNotMatch()
    {
        var playlists = new[] { List("evening", "Harbor", Page("https://example.com/watch", "Lecture")) };

        Assert.Empty(HomeSearch.Find("harbor", [], playlists));
    }

    [Fact]
    public void NothingMatchesAnUnknownName()
    {
        var library = new[] { File("alpha.png", MediaKind.Image) };
        var playlists = new[] { List("evening", "Evening", Page("https://example.com/watch", "Lecture")) };

        Assert.Empty(HomeSearch.Find("missing", library, playlists));
    }

    [Fact]
    public void ResultsAreOrderedByTitleWithLibraryBeforeOnline()
    {
        var library = new[]
        {
            File("marina.mp4", MediaKind.Video),
            File("alpha.png", MediaKind.Image)
        };
        var playlists = new[] { List("evening", "Evening", Page("https://example.com/zebra", "Zebra lecture")) };

        var titles = HomeSearch.Find("a", library, playlists).Select(hit => hit.Title).ToArray();

        Assert.Equal(["alpha.png", "marina.mp4", "Zebra lecture"], titles);
    }

    private static MediaItem File(string name, MediaKind kind, string? path = null, string? folder = null)
        => new()
        {
            DisplayName = name,
            Kind = kind,
            FilePath = path ?? @"C:\library\" + name,
            FolderName = folder ?? string.Empty
        };

    private static Playlist List(string id, string name, params PlaylistEntry[] videos)
        => new()
        {
            Id = id,
            Name = name,
            Videos = videos.ToList()
        };

    private static PlaylistEntry Page(string url, string title)
        => PlaylistEntry.Page(url, title);
}
