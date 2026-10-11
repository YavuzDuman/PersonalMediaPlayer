using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaybackBookmarkTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;
    private readonly string _folder;

    public PlaybackBookmarkTests()
    {
        Monitor.Enter(Gate);
        _folder = Path.Combine(Path.GetTempPath(), "pmp-bookmarks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _store = Path.Combine(_folder, "playback-bookmarks.json");
        PlaybackBookmarks.StoreOverride = _store;
    }

    public void Dispose()
    {
        PlaybackBookmarks.StoreOverride = null;
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void ANoteStaysWithTheMarkUntilItIsCleared()
    {
        var video = Path.Combine(_folder, "clip.mp4");
        Assert.True(PlaybackBookmarks.Add(video, 12_000, "  hello mark  "));
        Assert.False(PlaybackBookmarks.Add(video, 12_400, "too close"));

        var saved = Assert.Single(PlaybackBookmarks.Load(video));
        Assert.Equal(12_000, saved.TimeMs);
        Assert.Equal("hello mark", saved.Note);
        Assert.Contains("hello mark", File.ReadAllText(_store));

        PlaybackBookmarks.UpdateNote(video, 12_000, "edited note");
        var edited = Assert.Single(PlaybackBookmarks.Load(video));
        Assert.Equal(12_000, edited.TimeMs);
        Assert.Equal("edited note", edited.Note);

        PlaybackBookmarks.UpdateNote(video, 12_000, "   ");
        var cleared = Assert.Single(PlaybackBookmarks.Load(video));
        Assert.Equal(12_000, cleared.TimeMs);
        Assert.Equal(string.Empty, cleared.Note);

        PlaybackBookmarks.Remove(video, 12_000);
        Assert.Empty(PlaybackBookmarks.Load(video));
    }

    [Fact]
    public void ALongNoteIsShortenedAndAnOldTimeStillLoads()
    {
        var video = Path.Combine(_folder, "clip.mp4");
        var longNote = new string('a', PlaybackBookmarks.MaxNoteLength + 40);
        Assert.True(PlaybackBookmarks.Add(video, 5_000, longNote));
        Assert.Equal(PlaybackBookmarks.MaxNoteLength, PlaybackBookmarks.Load(video)[0].Note.Length);

        File.WriteAllText(_store, """{"C:\\clip.mp4":[1500,{"TimeMs":9000,"Note":"kept"}]}""");
        var marks = PlaybackBookmarks.Load(@"C:\clip.mp4");
        Assert.Equal(2, marks.Count);
        Assert.Equal(1_500, marks[0].TimeMs);
        Assert.Equal(string.Empty, marks[0].Note);
        Assert.Equal(9_000, marks[1].TimeMs);
        Assert.Equal("kept", marks[1].Note);
    }

    [Fact]
    public void RemapAndRenameKeepTheNote()
    {
        var video = Path.Combine(_folder, "clip.mp4");
        var renamed = Path.Combine(_folder, "renamed.mp4");
        Assert.True(PlaybackBookmarks.Add(video, 20_000, "inside"));
        Assert.True(PlaybackBookmarks.Add(video, 40_000, "after the cut"));

        PlaybackBookmarks.Remap(video, [(10_000, 30_000)], 2);
        var remapped = PlaybackBookmarks.Load(video);
        var kept = Assert.Single(remapped);
        Assert.Equal("after the cut", kept.Note);
        Assert.Equal(10_000, kept.TimeMs);

        PlaybackBookmarks.Move(video, renamed);
        var moved = Assert.Single(PlaybackBookmarks.Load(renamed));
        Assert.Equal("after the cut", moved.Note);
        Assert.Empty(PlaybackBookmarks.Load(video));
    }

    [Fact]
    public void ANoteSearchShowsMatchesUntilTheBoxIsCleared()
    {
        var marks = new Bookmark[]
        {
            new() { TimeMs = 8_000, Note = "Alpha gate" },
            new() { TimeMs = 18_000, Note = "" },
            new() { TimeMs = 28_000, Note = "Beta harbor" }
        };

        Assert.Equal(3, PlaybackBookmarks.Matching(marks, null).Count);
        Assert.Equal(3, PlaybackBookmarks.Matching(marks, "   ").Count);

        var gate = PlaybackBookmarks.Matching(marks, " GATE ");
        var found = Assert.Single(gate);
        Assert.Equal("Alpha gate", found.Note);
        Assert.Equal(8_000, found.TimeMs);

        Assert.Empty(PlaybackBookmarks.Matching(marks, "missing"));
        Assert.Equal(3, PlaybackBookmarks.Matching(marks, "").Count);
    }
}
