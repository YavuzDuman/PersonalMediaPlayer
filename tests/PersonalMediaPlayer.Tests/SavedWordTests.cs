using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class SavedWordTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;

    public SavedWordTests()
    {
        Monitor.Enter(Gate);
        _store = Path.Combine(Path.GetTempPath(), "pmp-saved-words-" + Guid.NewGuid().ToString("N") + ".json");
        SavedWords.StoreOverride = _store;
    }

    public void Dispose()
    {
        SavedWords.StoreOverride = null;
        if (File.Exists(_store))
        {
            File.Delete(_store);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void AStreamWordKeepsThePageAndTheMoment()
    {
        SavedWords.Add("trunk", "hortum", "They have long trunks.", null, null);
        SavedWords.Add("trunk", "hortum", "They have long trunks.", null, 1_200, "https://www.youtube.com/watch?v=jNQXAC9IVRw", true, "Me at the zoo");
        SavedWords.Add("here", "burada", "Here we are.", null, 3_400, "https://www.youtube.com/watch?v=jNQXAC9IVRw", true, "Me at the zoo", "en", "en-orig");
        SavedWords.Add("here", "burada", "Here we are.", null, 3_400, "https://www.youtube.com/watch?v=other", true, "Other");

        var words = SavedWords.All();
        Assert.Equal(3, words.Count);
        var filled = Assert.Single(words, word => word.English == "trunk");
        Assert.Equal("https://www.youtube.com/watch?v=jNQXAC9IVRw", filled.PageUrl);
        Assert.Equal(1_200, filled.TimeMs);
        Assert.True(filled.ResolvePage);
        Assert.Equal("Me at the zoo", filled.SourceName);
        Assert.Null(filled.VideoPath);
        Assert.True(SavedWords.Contains("here", "Here we are.", null, 3_400, "https://www.youtube.com/watch?v=jNQXAC9IVRw"));
        Assert.True(SavedWords.Contains("here", "Here we are.", null, 3_400, "https://www.youtube.com/watch?v=other"));
        Assert.False(SavedWords.Contains("here", "Here we are.", null, 9_000, "https://www.youtube.com/watch?v=jNQXAC9IVRw"));
        Assert.Equal("Me at the zoo", SavedWords.SourceTitle(filled));
        Assert.True(SavedWords.CanOpen(filled));
        var here = Assert.Single(words, word => word.English == "here" && word.PageUrl == "https://www.youtube.com/watch?v=jNQXAC9IVRw");
        Assert.Equal("en", here.AudioLanguage);
        Assert.Equal("en-orig", here.CaptionLanguage);
        SavedWords.Add("here", "burada", "Here we are.", null, 3_400, "https://www.youtube.com/watch?v=jNQXAC9IVRw", true, "Me at the zoo", "es-US", "en");
        var updated = SavedWords.All().Single(word => word.English == "here" && word.PageUrl == "https://www.youtube.com/watch?v=jNQXAC9IVRw");
        Assert.Equal("es-US", updated.AudioLanguage);
        Assert.Equal("en", updated.CaptionLanguage);
    }

    [Fact]
    public void AFileWordStaysAFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pmp-saved-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "clip.mp4");
        File.WriteAllText(path, "video");
        try
        {
            SavedWords.Add("hello", "merhaba", "Hello there.", path, 2_000);
            var word = Assert.Single(SavedWords.All());
            Assert.Equal(path, word.VideoPath);
            Assert.Null(word.PageUrl);
            Assert.False(word.ResolvePage);
            Assert.Equal(2_000, word.TimeMs);
            Assert.True(SavedWords.CanOpen(word));
            Assert.Equal("clip.mp4", SavedWords.SourceTitle(word));
            Assert.True(SavedWords.Contains("hello", "Hello there.", path, 2_000));
            Assert.Null(word.AudioLanguage);
            Assert.Null(word.CaptionLanguage);
            Assert.False(SavedWords.Contains("hello", "Hello there.", null, 2_000, "https://example.com/watch"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
