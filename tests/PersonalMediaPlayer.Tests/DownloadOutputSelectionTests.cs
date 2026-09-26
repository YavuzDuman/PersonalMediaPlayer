using PersonalMediaPlayer.App.Download;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class DownloadOutputSelectionTests
{
    [Fact]
    public void MissingExpectedFile_DoesNotReturnAnUnrelatedFile()
    {
        var folder = CreateFolder();
        try
        {
            WriteFile(folder, "this-download.f137.mp4", 4096, DateTime.UtcNow.AddMinutes(-2));
            WriteFile(folder, "earlier-video.mp4", 8192, DateTime.UtcNow);
            var expected = Path.Combine(folder, "this-download.mp4");

            Assert.Throws<InvalidOperationException>(() => YoutubeDownloader.ResolveProducedFile(expected));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MissingExpectedFile_UsesAnotherCompletedFileFromThisDownload()
    {
        var folder = CreateFolder();
        try
        {
            var merged = WriteFile(folder, "this-download.webm", 8192, DateTime.UtcNow.AddMinutes(-3));
            WriteFile(folder, "this-download.f137.mp4", 9000, DateTime.UtcNow);
            WriteFile(folder, "earlier-video.mp4", 9000, DateTime.UtcNow);
            var expected = Path.Combine(folder, "this-download.mp4");

            var chosen = YoutubeDownloader.ResolveProducedFile(expected);

            Assert.Equal(merged, chosen);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void LinkText_TreatsADifferentLetterCaseAsADifferentLink()
    {
        Assert.False(YoutubeDownloader.SameLinkText("https://youtu.be/abcE", "https://youtu.be/abce"));
        Assert.True(YoutubeDownloader.SameLinkText(" https://youtu.be/abcE ", "https://youtu.be/abcE"));
    }

    [Fact]
    public void ExistingExpectedFile_IsKeptEvenWhenAnotherFileIsNewer()
    {
        var folder = CreateFolder();
        try
        {
            var expected = WriteFile(folder, "this-download.mp4", 2048, DateTime.UtcNow.AddMinutes(-5));
            WriteFile(folder, "earlier-video.mp4", 4096, DateTime.UtcNow);

            var chosen = YoutubeDownloader.ResolveProducedFile(expected);

            Assert.Equal(expected, chosen);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pmp-download-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string WriteFile(string folder, string name, int bytes, DateTime writtenAtUtc)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, writtenAtUtc);
        return path;
    }
}
