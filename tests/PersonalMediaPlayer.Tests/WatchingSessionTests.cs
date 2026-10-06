using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class WatchingSessionTests : IDisposable
{
    public WatchingSessionTests()
    {
        WatchingSession.SuspendPersistence();
        WatchingSession.StoreOverride = null;
        WatchingSession.Forget();
    }

    public void Dispose()
    {
        WatchingSession.SuspendPersistence();
        WatchingSession.StoreOverride = null;
        WatchingSession.Forget();
    }

    [Fact]
    public void TheOpenVideoComesBackAtTheSameMomentAndLeavesTheQueueAlone()
    {
        UsingFolder(folder =>
        {
            var watching = Path.Combine(folder, WatchingSession.FileName);
            var queue = Path.Combine(folder, PlayQueue.FileName);
            var playlists = Path.Combine(folder, "playlists.json");
            var downloads = Path.Combine(folder, "download-queue.json");
            File.WriteAllText(queue, "keep-queue");
            File.WriteAllText(playlists, "keep-playlists");
            File.WriteAllText(downloads, "keep-downloads");
            var kept = Path.Combine(folder, "kept.mp4");
            File.WriteAllText(kept, "kept");

            WatchingSession.Load();
            Assert.Null(WatchingSession.Current);
            WatchingSession.RememberFile("Clip", kept, 12_500);
            WatchingSession.NotePosition(18_000);
            WatchingSession.Load();

            var video = WatchingSession.Current;
            Assert.NotNull(video);
            Assert.Equal(WatchingKind.File, video.Kind);
            Assert.Equal("Clip", video.Title);
            Assert.Equal(kept, video.Location);
            Assert.Equal(18_000, video.PositionMs);
            Assert.False(video.IsMissing);
            Assert.Equal("On this PC", video.SourceLabel);
            Assert.Equal("keep-queue", File.ReadAllText(queue));
            Assert.Equal("keep-playlists", File.ReadAllText(playlists));
            Assert.Equal("keep-downloads", File.ReadAllText(downloads));
            Assert.Equal("watching.json", WatchingSession.FileName);

            WatchingSession.RememberPage(
                "Harbor",
                "https://www.youtube.com/watch?v=abcdefghijk",
                "https://cdn.example/media.m3u8",
                4_000);
            WatchingSession.Load();
            video = WatchingSession.Current;
            Assert.NotNull(video);
            Assert.Equal(WatchingKind.Page, video.Kind);
            Assert.Equal("https://www.youtube.com/watch?v=abcdefghijk", video.Location);
            Assert.Equal("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg", video.Thumbnail);
            Assert.Equal(4_000, video.PositionMs);
            Assert.Equal("Online", video.SourceLabel);
            Assert.False(video.IsMissing);
            Assert.Equal("keep-queue", File.ReadAllText(queue));
        });
    }

    [Fact]
    public void ClosingThePlayerForgetsTheVideoAndKeepsTheQueue()
    {
        UsingFolder(folder =>
        {
            var queue = Path.Combine(folder, PlayQueue.FileName);
            File.WriteAllText(queue, "keep-queue");
            WatchingSession.Load();
            WatchingSession.RememberStream("Direct", "https://cdn.example/clip.mp4", null, 900);
            Assert.Equal(WatchingKind.Stream, WatchingSession.Current!.Kind);
            Assert.Equal("https://cdn.example/clip.mp4", WatchingSession.Current.Location);

            WatchingSession.Forget();
            WatchingSession.Load();
            Assert.Null(WatchingSession.Current);
            WatchingSession.RememberFile(" ", "  ", 0);
            Assert.Null(WatchingSession.Current);
            Assert.Equal("keep-queue", File.ReadAllText(queue));
        });
    }

    [Fact]
    public void AMissingVideoStaysMissingAndABrokenFileLoadsEmpty()
    {
        UsingFolder(folder =>
        {
            var watching = Path.Combine(folder, WatchingSession.FileName);
            var playlists = Path.Combine(folder, "playlists.json");
            File.WriteAllText(playlists, "keep-playlists");
            File.WriteAllText(watching, "{");
            WatchingSession.Load();
            Assert.Null(WatchingSession.Current);
            Assert.Equal("{", File.ReadAllText(watching));
            Assert.Equal("keep-playlists", File.ReadAllText(playlists));

            var missing = Path.Combine(folder, "gone.mp4");
            WatchingSession.RememberFile("Gone", missing, 2500);
            WatchingSession.Load();
            var video = WatchingSession.Current;
            Assert.NotNull(video);
            Assert.Equal(missing, video.Location);
            Assert.Equal(2500, video.PositionMs);
            Assert.True(video.IsMissing);
            Assert.Equal("Missing", video.SourceLabel);
            Assert.Equal("keep-playlists", File.ReadAllText(playlists));
        });
    }

    private static void UsingFolder(Action<string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pmp-watching-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        WatchingSession.SuspendPersistence();
        WatchingSession.StoreOverride = Path.Combine(folder, WatchingSession.FileName);
        try
        {
            check(folder);
        }
        finally
        {
            WatchingSession.SuspendPersistence();
            WatchingSession.StoreOverride = null;
            WatchingSession.Forget();
            Directory.Delete(folder, true);
        }
    }
}
