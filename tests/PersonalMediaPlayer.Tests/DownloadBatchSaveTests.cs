using PersonalMediaPlayer.App.Download;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class DownloadBatchSaveTests
{
    [Fact]
    public void SavesEachReadyFileIntoTheFolder()
    {
        var source = TempFolder();
        var folder = TempFolder();
        try
        {
            var first = Write(source, "one.mp4", "first video");
            var second = Write(source, "two.m4a", "audio take");
            var plans = DownloadBatchSave.Plan(
            [
                new DownloadBatchFile("Hello: World", first, false),
                new DownloadBatchFile("Song", second, true)
            ], folder);

            var results = DownloadBatchSave.CopyAll(plans, replaceExisting: true);

            Assert.Equal(2, results.Count);
            Assert.All(results, result => Assert.Equal(DownloadBatchOutcome.Saved, result.Outcome));
            Assert.Equal("Hello World.mp4", results[0].FileName);
            Assert.Equal("Song.m4a", results[1].FileName);
            Assert.Equal("first video", File.ReadAllText(results[0].SavedPath!));
            Assert.Equal("audio take", File.ReadAllText(results[1].SavedPath!));
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.Empty(Directory.GetFiles(folder, "*.saving"));
        }
        finally
        {
            Delete(source);
            Delete(folder);
        }
    }

    [Fact]
    public void ASecondFileWithTheSameNameGetsANewName()
    {
        var source = TempFolder();
        var folder = TempFolder();
        try
        {
            var first = Write(source, "a.mp4", "one");
            var second = Write(source, "b.mp4", "two");
            var plans = DownloadBatchSave.Plan(
            [
                new DownloadBatchFile("Talk", first, false),
                new DownloadBatchFile("Talk", second, false)
            ], folder);

            Assert.Equal("Talk.mp4", plans[0].FileName);
            Assert.Equal("Talk (2).mp4", plans[1].FileName);
            Assert.False(plans[0].AlreadyThere);
            Assert.False(plans[1].AlreadyThere);

            var results = DownloadBatchSave.CopyAll(plans, replaceExisting: true);

            Assert.All(results, result => Assert.Equal(DownloadBatchOutcome.Saved, result.Outcome));
            Assert.Equal("one", File.ReadAllText(Path.Combine(folder, "Talk.mp4")));
            Assert.Equal("two", File.ReadAllText(Path.Combine(folder, "Talk (2).mp4")));
        }
        finally
        {
            Delete(source);
            Delete(folder);
        }
    }

    [Fact]
    public void KeepExistingLeavesThatFileAndSavesTheOther()
    {
        var source = TempFolder();
        var folder = TempFolder();
        try
        {
            var talk = Write(source, "talk.mp4", "new talk");
            var extra = Write(source, "extra.mp4", "extra video");
            Write(folder, "Talk.mp4", "old talk");
            var plans = DownloadBatchSave.Plan(
            [
                new DownloadBatchFile("Talk", talk, false),
                new DownloadBatchFile("Extra", extra, false)
            ], folder);

            Assert.True(plans[0].AlreadyThere);
            Assert.False(plans[1].AlreadyThere);

            var results = DownloadBatchSave.CopyAll(plans, replaceExisting: false);

            Assert.Equal(DownloadBatchOutcome.Kept, results[0].Outcome);
            Assert.Equal(DownloadBatchOutcome.Saved, results[1].Outcome);
            Assert.Equal("old talk", File.ReadAllText(Path.Combine(folder, "Talk.mp4")));
            Assert.Equal("extra video", File.ReadAllText(Path.Combine(folder, "Extra.mp4")));
            Assert.True(File.Exists(talk));
        }
        finally
        {
            Delete(source);
            Delete(folder);
        }
    }

    [Fact]
    public void ReplaceOverwritesTheExistingFile()
    {
        var source = TempFolder();
        var folder = TempFolder();
        try
        {
            var talk = Write(source, "talk.mp4", "new talk");
            Write(folder, "Talk.mp4", "old talk");
            var plans = DownloadBatchSave.Plan([new DownloadBatchFile("Talk", talk, false)], folder);

            var results = DownloadBatchSave.CopyAll(plans, replaceExisting: true);

            Assert.Equal(DownloadBatchOutcome.Saved, results[0].Outcome);
            Assert.Equal("new talk", File.ReadAllText(Path.Combine(folder, "Talk.mp4")));
        }
        finally
        {
            Delete(source);
            Delete(folder);
        }
    }

    [Fact]
    public void OneMissingFileDoesNotStopTheNext()
    {
        var source = TempFolder();
        var folder = TempFolder();
        try
        {
            var gone = Path.Combine(source, "gone.mp4");
            var kept = Write(source, "kept.mp4", "still here");
            var plans = DownloadBatchSave.Plan(
            [
                new DownloadBatchFile("Gone", gone, false),
                new DownloadBatchFile("Kept", kept, false)
            ], folder);

            var results = DownloadBatchSave.CopyAll(plans, replaceExisting: true);

            Assert.Equal(DownloadBatchOutcome.Failed, results[0].Outcome);
            Assert.Contains("no longer there", results[0].Detail, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(DownloadBatchOutcome.Saved, results[1].Outcome);
            Assert.Equal("still here", File.ReadAllText(results[1].SavedPath!));
            Assert.False(File.Exists(Path.Combine(folder, "Gone.mp4")));
        }
        finally
        {
            Delete(source);
            Delete(folder);
        }
    }

    [Fact]
    public void AnExistingNumberedNameIsAlsoListed()
    {
        var source = TempFolder();
        var folder = TempFolder();
        try
        {
            Write(folder, "Talk.mp4", "old");
            Write(folder, "Talk (2).mp4", "old two");
            var plans = DownloadBatchSave.Plan(
            [
                new DownloadBatchFile("Talk", Write(source, "a.mp4", "a"), false),
                new DownloadBatchFile("Talk", Write(source, "b.mp4", "b"), false)
            ], folder);

            Assert.True(plans[0].AlreadyThere);
            Assert.Equal("Talk.mp4", plans[0].FileName);
            Assert.True(plans[1].AlreadyThere);
            Assert.Equal("Talk (2).mp4", plans[1].FileName);
        }
        finally
        {
            Delete(source);
            Delete(folder);
        }
    }

    [Fact]
    public void DescribeNamesTheSavedFilesAndTheFailedFiles()
    {
        var text = DownloadBatchSave.Describe(
        [
            new DownloadBatchResult(@"C:\ready\a.mp4", "Talk", "Talk.mp4", DownloadBatchOutcome.Saved, @"C:\out\Talk.mp4", null),
            new DownloadBatchResult(@"C:\ready\b.mp4", "Other", "Other.mp4", DownloadBatchOutcome.Failed, null, "That download is no longer there."),
            new DownloadBatchResult(@"C:\ready\c.mp4", "Extra", "Extra.mp4", DownloadBatchOutcome.Kept, null, null)
        ], "that folder");

        Assert.Equal(
            "Saved Talk.mp4 to that folder. Could not save Other (That download is no longer there). It is still ready. Left ready because a file with that name is already there: Extra.mp4.",
            text);
    }

    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pmp-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Write(string folder, string name, string text)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static void Delete(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
