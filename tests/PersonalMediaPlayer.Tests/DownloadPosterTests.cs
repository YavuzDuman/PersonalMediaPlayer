using PersonalMediaPlayer.App.Download;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class DownloadPosterTests
{
    [Fact]
    public void ASavedYouTubeVideoUsesTheRealPosterCandidates()
    {
        var addresses = DownloadPoster.CandidateAddresses("https://www.youtube.com/watch?v=jNQXAC9IVRw");

        Assert.Equal(
        [
            "https://i.ytimg.com/vi/jNQXAC9IVRw/maxresdefault.jpg",
            "https://i.ytimg.com/vi/jNQXAC9IVRw/sddefault.jpg",
            "https://i.ytimg.com/vi/jNQXAC9IVRw/hqdefault.jpg"
        ], addresses);
        Assert.Empty(DownloadPoster.CandidateAddresses("https://www.youtube.com/playlist?list=PLabc"));
    }

    [Fact]
    public void ARealPosterIsAJpegLargeEnoughToBeThePicture()
    {
        var jpeg = new byte[2_048];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;

        Assert.True(DownloadPoster.IsPosterImage(jpeg));
        Assert.False(DownloadPoster.IsPosterImage(jpeg.AsSpan(0, 2_047)));
        jpeg[0] = 0x89;
        Assert.False(DownloadPoster.IsPosterImage(jpeg));
    }

    [Fact]
    public void HistoryFindsThePageForThatSavedFile()
    {
        var saved = Path.Combine(Path.GetTempPath(), "library", "Harbor.mp4");
        var json = """
            [
              {"Name":"Other.mp4","Url":"https://youtu.be/aaaaaaaaaaa","Location":"C:/clips/other.mp4"},
              {"Name":"Harbor.mp4","Url":"https://www.youtube.com/watch?v=jNQXAC9IVRw","Location":"PLACE"}
            ]
            """.Replace("PLACE", saved.Replace("\\", "\\\\"), StringComparison.Ordinal);

        Assert.Equal("https://www.youtube.com/watch?v=jNQXAC9IVRw", DownloadPoster.PageFromHistory(json, saved.ToLowerInvariant()));
        Assert.Null(DownloadPoster.PageFromHistory(json, Path.Combine(Path.GetTempPath(), "missing.mp4")));
        Assert.Null(DownloadPoster.PageFromHistory(json.Replace("https://www.youtube.com/watch?v=jNQXAC9IVRw", "https://example.com/clip", StringComparison.Ordinal), saved));
    }

    [Fact]
    public void RenamingTheFileUpdatesTheHistoryPath()
    {
        var oldPath = Path.Combine(Path.GetTempPath(), "old name.mp4");
        var newPath = Path.Combine(Path.GetTempPath(), "new name.mp4");
        var history = """[{"Name":"old name.mp4","Url":"https://youtu.be/jNQXAC9IVRw","Location":"OLD"}]"""
            .Replace("OLD", oldPath.Replace("\\", "\\\\"), StringComparison.Ordinal);

        var rewritten = DownloadPoster.RewriteHistory(history, oldPath, newPath);

        Assert.NotNull(rewritten);
        Assert.Contains("new name.mp4", rewritten, StringComparison.Ordinal);
        Assert.Contains(newPath.Replace("\\", "\\\\"), rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("old name.mp4", rewritten, StringComparison.Ordinal);
        Assert.Equal("https://youtu.be/jNQXAC9IVRw", DownloadPoster.PageFromHistory(rewritten, newPath));
        Assert.Null(DownloadPoster.RewriteHistory(rewritten, newPath, newPath));
    }

    [Fact]
    public void MovingACachedPosterFollowsTheFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pmp-poster-" + Guid.NewGuid().ToString("N"));
        var oldPath = Path.Combine(root, "clip.mp4");
        var newPath = Path.Combine(root, "renamed.mp4");
        DownloadPoster.DirectoryOverride = Path.Combine(root, "cache");
        try
        {
            Directory.CreateDirectory(DownloadPoster.DirectoryOverride);
            var before = PosterPath(oldPath);
            File.WriteAllBytes(before, [0xFF, 0xD8, 0xFF]);
            DownloadPoster.Move(oldPath, newPath);

            Assert.False(File.Exists(before));
            Assert.Equal(PosterPath(newPath), DownloadPoster.FindCached(newPath));
        }
        finally
        {
            DownloadPoster.DirectoryOverride = null;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string PosterPath(string mediaPath)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(mediaPath).ToLowerInvariant()))).ToLowerInvariant();
        return Path.Combine(DownloadPoster.DirectoryOverride!, hash + ".jpg");
    }
}
