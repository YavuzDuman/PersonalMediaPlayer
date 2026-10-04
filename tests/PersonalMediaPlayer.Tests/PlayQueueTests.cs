using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlayQueueTests : IDisposable
{
    public PlayQueueTests() => PlayQueue.Clear();

    public void Dispose() => PlayQueue.Clear();

    [Fact]
    public void VideosStayInTheOrderTheyWereAdded()
    {
        PlayQueue.AddFile("One", @"C:\clips\one.mp4");
        PlayQueue.AddFile("Two", @"C:\clips\two.mp4");
        var again = PlayQueue.AddFile("One", @"C:\clips\one.mp4");
        var page = PlayQueue.AddPage("Harbor", "https://www.youtube.com/watch?v=abcdefghijk", "https://cdn.example/media.m3u8");

        Assert.Equal(4, PlayQueue.Count);
        Assert.Equal(new[] { "One", "Two", "One", "Harbor" }, PlayQueue.Snapshot().Select(item => item.Title));
        Assert.NotEqual(PlayQueue.Snapshot()[0].Id, again!.Id);
        Assert.Equal(PlayQueueKind.Page, page!.Kind);
        Assert.Equal("https://www.youtube.com/watch?v=abcdefghijk", page.Location);
        Assert.Equal("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg", page.Thumbnail);
        Assert.DoesNotContain("m3u8", page.Location, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("One", PlayQueue.TakeNext()!.Title);
        Assert.Equal("Two", PlayQueue.TakeNext()!.Title);
        Assert.Equal(@"C:\clips\one.mp4", PlayQueue.TakeNext()!.Location);
        Assert.Equal("Harbor", PlayQueue.TakeNext()!.Title);
        Assert.Null(PlayQueue.TakeNext());
    }

    [Fact]
    public void RemoveDropsOnlyThatVideo()
    {
        PlayQueue.AddFile("A", @"C:\a.mp4");
        var middle = PlayQueue.AddFile("B", @"C:\b.mp4");
        PlayQueue.AddFile("C", @"C:\c.mp4");

        Assert.True(PlayQueue.Remove(middle!.Id));
        Assert.False(PlayQueue.Remove(middle.Id));
        Assert.Equal(new[] { "A", "C" }, PlayQueue.Snapshot().Select(item => item.Title));
    }

    [Fact]
    public void ClosingTheAppDropsTheQueue()
    {
        PlayQueue.AddFile("A", @"C:\a.mp4");
        PlayQueue.Add(PlaylistEntry.Page("https://example.com/watch/harbor", "Harbor"));

        Assert.True(PlayQueue.ClearWaiting());
        PlayQueue.Clear();

        Assert.Equal(0, PlayQueue.Count);
        Assert.Empty(PlayQueue.Snapshot());
        Assert.False(PlayQueue.IsPending(out _));
        Assert.False(PlayQueue.Undo());
        Assert.Null(PlayQueue.TakeNext());
    }

    [Fact]
    public void BlankPathsAndFilePathsAreRefusedAsPages()
    {
        Assert.Null(PlayQueue.AddFile(" ", "  "));
        Assert.Null(PlayQueue.AddPage("Clip", @"C:\clips\clip.mp4", null));
        Assert.Null(PlayQueue.Add(PlaylistEntry.ForFile("  ")));
        Assert.Equal(0, PlayQueue.Count);
    }

    [Fact]
    public void SeveralAddsCanNotifyOnce()
    {
        var calls = 0;
        void Count(object? sender, EventArgs e) => calls++;
        PlayQueue.Changed += Count;
        try
        {
            PlayQueue.AddFile("A", @"C:\a.mp4", notify: false);
            PlayQueue.AddFile("B", @"C:\b.mp4", notify: false);
            Assert.Equal(0, calls);
            Assert.Equal(2, PlayQueue.Count);
            PlayQueue.Notify();
            Assert.Equal(1, calls);
        }
        finally
        {
            PlayQueue.Changed -= Count;
        }
    }

    [Fact]
    public void QueuePlaysBeforeShuffleRepeatAndThenThePlaylistContinues()
    {
        var keys = new[] { "a", "b", "c" };
        var run = new PlaylistRun();
        run.SetShuffle(true);
        run.CycleRepeat();
        run.CycleRepeat();
        var index = 0;
        PlayQueue.AddFile("Q1", @"C:\q1.mp4");
        PlayQueue.AddFile("Q2", @"C:\q2.mp4");

        Assert.Equal(PlayAdvanceKind.Queue, PlayQueue.Advance(PlayQueue.Count, hasPlaylist: true));
        Assert.Equal("Q1", PlayQueue.TakeNext()!.Title);
        Assert.Equal(0, index);

        Assert.Equal(PlayAdvanceKind.Queue, PlayQueue.Advance(PlayQueue.Count, hasPlaylist: true));
        Assert.Equal("Q2", PlayQueue.TakeNext()!.Title);

        Assert.Equal(PlaylistRepeat.One, run.Repeat);
        Assert.True(run.Shuffle);
        Assert.Equal(PlayAdvanceKind.Playlist, PlayQueue.Advance(PlayQueue.Count, hasPlaylist: true));
        var replay = run.Move(keys, All, index, playNext: false, fromEnd: true, forward: true, new Random(1));
        Assert.Equal(PlaylistStepKind.Replay, replay.Kind);
        Assert.Equal(0, replay.Index);
        Assert.Equal(new[] { "a", "b", "c" }, keys);

        PlayQueue.AddFile("Q3", @"C:\q3.mp4");
        Assert.Equal(PlayAdvanceKind.Queue, PlayQueue.Advance(PlayQueue.Count, hasPlaylist: true));
        PlayQueue.Clear();
        Assert.Equal(PlayAdvanceKind.Playlist, PlayQueue.Advance(0, hasPlaylist: true));
        var skipped = run.Move(keys, All, index, playNext: true, fromEnd: false, forward: true, new Random(1));
        Assert.Equal(PlaylistStepKind.Open, skipped.Kind);
        Assert.NotEqual(index, skipped.Index);

        Assert.Equal(PlayAdvanceKind.Stop, PlayQueue.Advance(0, hasPlaylist: false));
        Assert.Equal(new[] { "a", "b", "c" }, keys);
    }

    [Fact]
    public void PlayNextPutsThatVideoFirstAndLeavesTheRestWaiting()
    {
        PlayQueue.AddFile("Later", @"C:\later.mp4");
        PlayQueue.AddFile("After", @"C:\after.mp4");
        var first = PlayQueue.PlayNextFile("Now", @"C:\now.mp4");
        var page = PlayQueue.PlayNextPage("Harbor", "https://www.youtube.com/watch?v=abcdefghijk", "https://cdn.example/media.m3u8");

        Assert.NotNull(first);
        Assert.Equal(PlayQueueKind.Page, page!.Kind);
        Assert.Equal("https://www.youtube.com/watch?v=abcdefghijk", page.Location);
        Assert.Equal("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg", page.Thumbnail);
        Assert.DoesNotContain("m3u8", page.Location, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Harbor", "Now", "Later", "After" }, Titles());
        Assert.Equal("Harbor", PlayQueue.TakeNext()!.Title);
        Assert.Equal(new[] { "Now", "Later", "After" }, Titles());
        Assert.Null(PlayQueue.PlayNextFile(" ", "  "));
        Assert.Null(PlayQueue.PlayNext(PlaylistEntry.ForFile("  ")));
        Assert.Equal(3, PlayQueue.Count);
    }

    [Fact]
    public void ASelectionPlayedNextStaysInOrderAtTheFront()
    {
        PlayQueue.AddFile("Waiting", @"C:\waiting.mp4");
        var calls = 0;
        void Count(object? sender, EventArgs e) => calls++;
        PlayQueue.Changed += Count;
        try
        {
            var added = PlayQueue.AddFiles(
                [("One", @"C:\one.mp4"), (" ", "  "), ("Two", @"C:\two.mp4")],
                next: true);
            Assert.Equal(2, added);
            Assert.Equal(1, calls);
            Assert.Equal(new[] { "One", "Two", "Waiting" }, Titles());

            Assert.Equal(1, PlayQueue.AddFiles([("Three", @"C:\three.mp4")], next: false, notify: false));
            Assert.Equal(1, calls);
        }
        finally
        {
            PlayQueue.Changed -= Count;
        }

        Assert.Equal(new[] { "One", "Two", "Waiting", "Three" }, Titles());
    }

    [Fact]
    public void DraggingChangesOnlyTheQueueOrder()
    {
        PlayQueue.AddFile("A", @"C:\a.mp4");
        PlayQueue.AddFile("B", @"C:\b.mp4");
        PlayQueue.AddFile("C", @"C:\c.mp4");
        PlayQueue.AddFile("D", @"C:\d.mp4");

        Assert.True(PlayQueue.Move(2, 0));
        Assert.Equal(new[] { "C", "A", "B", "D" }, Titles());
        Assert.True(PlayQueue.Move(3, 1));
        Assert.Equal(new[] { "C", "D", "A", "B" }, Titles());

        var calls = 0;
        void Count(object? sender, EventArgs e) => calls++;
        PlayQueue.Changed += Count;
        try
        {
            Assert.False(PlayQueue.Move(1, 1));
            Assert.False(PlayQueue.Move(1, 2));
            Assert.False(PlayQueue.Move(-1, 0));
            Assert.Equal(0, calls);
        }
        finally
        {
            PlayQueue.Changed -= Count;
        }

        Assert.Equal(new[] { "C", "D", "A", "B" }, Titles());
        Assert.Equal("C", PlayQueue.TakeNext()!.Title);
        Assert.Equal(new[] { "D", "A", "B" }, Titles());
    }

    [Fact]
    public void PlayNextOnAPlaylistEntryKeepsThePageAddress()
    {
        var entry = PlaylistEntry.Page("https://example.com/watch/harbor", "Harbor");
        entry.Thumbnail = "https://cdn.example/harbor.jpg";
        PlayQueue.AddFile("Waiting", @"C:\waiting.mp4");
        var queued = PlayQueue.PlayNext(entry);
        var file = PlayQueue.PlayNext(PlaylistEntry.ForFile(@"C:\clips\harbor.mp4"));

        Assert.Equal(PlayQueueKind.File, file!.Kind);
        Assert.Equal(@"C:\clips\harbor.mp4", file.Location);
        Assert.Equal(PlayQueueKind.Page, queued!.Kind);
        Assert.Equal("https://example.com/watch/harbor", queued.Location);
        Assert.Equal("https://cdn.example/harbor.jpg", queued.Thumbnail);
        Assert.Equal(new[] { "harbor.mp4", "Harbor", "Waiting" }, Titles());
    }

    [Fact]
    public void OnlinePlaylistEntryKeepsThePageAddress()
    {
        var entry = PlaylistEntry.Page("https://example.com/watch/harbor", "Harbor");
        entry.Thumbnail = "https://cdn.example/harbor.jpg";
        var queued = PlayQueue.Add(entry);
        var file = PlayQueue.Add(PlaylistEntry.ForFile(@"C:\clips\harbor.mp4"));

        Assert.Equal(PlayQueueKind.Page, queued!.Kind);
        Assert.Equal("https://example.com/watch/harbor", queued.Location);
        Assert.Equal("https://cdn.example/harbor.jpg", queued.Thumbnail);
        Assert.Equal(PlayQueueKind.File, file!.Kind);
        Assert.Equal("harbor.mp4", file.Title);
        Assert.Equal(@"C:\clips\harbor.mp4", file.Location);
    }

    [Fact]
    public void ClearRemovesEveryWaitingVideoAndUndoRestoresThatOrder()
    {
        var first = PlayQueue.AddFile("One", @"C:\clips\one.mp4");
        var page = PlayQueue.AddPage("Harbor", "https://example.com/watch/harbor", "https://cdn.example/harbor.jpg");
        var again = PlayQueue.AddFile("One", @"C:\clips\one.mp4");
        var calls = 0;
        void Count(object? sender, EventArgs e) => calls++;
        PlayQueue.Changed += Count;
        try
        {
            Assert.True(PlayQueue.ClearWaiting());
            Assert.Equal(1, calls);
            Assert.False(PlayQueue.ClearWaiting());
            Assert.Equal(1, calls);
        }
        finally
        {
            PlayQueue.Changed -= Count;
        }

        Assert.Equal(0, PlayQueue.Count);
        Assert.True(PlayQueue.IsPending(out var message));
        Assert.Equal("Cleared the queue.", message);
        Assert.Null(PlayQueue.TakeNext());
        Assert.True(PlayQueue.IsPending(out _));

        Assert.True(PlayQueue.Undo());
        Assert.False(PlayQueue.IsPending(out _));
        Assert.False(PlayQueue.Undo());
        Assert.Same(first, PlayQueue.Snapshot()[0]);
        Assert.Same(page, PlayQueue.Snapshot()[1]);
        Assert.Same(again, PlayQueue.Snapshot()[2]);
        Assert.Equal(new[] { "One", "Harbor", "One" }, Titles());
        Assert.Equal("https://example.com/watch/harbor", page!.Location);
        Assert.Equal("https://cdn.example/harbor.jpg", page.Thumbnail);
        Assert.Equal("One", PlayQueue.TakeNext()!.Title);
        Assert.Equal("Harbor", PlayQueue.TakeNext()!.Title);
        Assert.Equal(again!.Id, PlayQueue.Snapshot()[0].Id);
    }

    [Fact]
    public void UndoPutsARemovedVideoBackInTheSamePlace()
    {
        var first = PlayQueue.AddFile("A", @"C:\a.mp4");
        var middle = PlayQueue.AddPage("Harbor", "https://example.com/watch/harbor", "https://cdn.example/harbor.jpg");
        var last = PlayQueue.AddFile("C", @"C:\c.mp4");

        Assert.True(PlayQueue.Remove(middle!.Id));
        Assert.Equal(new[] { "A", "C" }, Titles());
        Assert.True(PlayQueue.IsPending(out var message));
        Assert.Equal("Removed \"Harbor\".", message);

        Assert.True(PlayQueue.Undo());
        Assert.Same(first, PlayQueue.Snapshot()[0]);
        Assert.Same(middle, PlayQueue.Snapshot()[1]);
        Assert.Same(last, PlayQueue.Snapshot()[2]);
        Assert.Equal(PlayQueueKind.Page, middle.Kind);
        Assert.Equal("https://example.com/watch/harbor", middle.Location);
        Assert.Equal("https://cdn.example/harbor.jpg", middle.Thumbnail);
    }

    [Fact]
    public void ASecondRemovalReplacesTheUndo()
    {
        PlayQueue.AddFile("A", @"C:\a.mp4");
        var middle = PlayQueue.AddFile("B", @"C:\b.mp4");
        var last = PlayQueue.AddFile("C", @"C:\c.mp4");

        Assert.True(PlayQueue.Remove(middle!.Id));
        Assert.True(PlayQueue.Remove(last!.Id));
        Assert.True(PlayQueue.IsPending(out var message));
        Assert.Equal("Removed \"C\".", message);

        Assert.True(PlayQueue.Undo());
        Assert.Equal(new[] { "A", "C" }, Titles());
        Assert.Same(last, PlayQueue.Snapshot()[1]);
        Assert.DoesNotContain(middle.Id, Ids());
        Assert.False(PlayQueue.IsPending(out _));
    }

    [Fact]
    public void StartingTheNextQueuedVideoIsNotAnUndo()
    {
        PlayQueue.AddFile("A", @"C:\a.mp4");
        PlayQueue.AddFile("B", @"C:\b.mp4");
        PlayQueue.AddFile("C", @"C:\c.mp4");

        Assert.Equal("A", PlayQueue.TakeNext()!.Title);
        Assert.False(PlayQueue.IsPending(out _));
        Assert.False(PlayQueue.Undo());
        Assert.Equal(new[] { "B", "C" }, Titles());

        Assert.True(PlayQueue.Remove(PlayQueue.Snapshot()[0].Id));
        Assert.Equal("C", PlayQueue.TakeNext()!.Title);
        Assert.False(PlayQueue.IsPending(out _));
        Assert.False(PlayQueue.Undo());
        Assert.Empty(Titles());
    }

    [Fact]
    public void AddingOrReorderingDropsAPendingUndo()
    {
        var first = PlayQueue.AddFile("A", @"C:\a.mp4");
        var middle = PlayQueue.AddFile("B", @"C:\b.mp4");
        var last = PlayQueue.AddFile("C", @"C:\c.mp4");
        Assert.True(PlayQueue.Remove(middle!.Id));
        Assert.False(PlayQueue.Move(0, 1));
        Assert.True(PlayQueue.IsPending(out _));

        Assert.True(PlayQueue.Move(0, 2));
        Assert.False(PlayQueue.IsPending(out _));
        Assert.False(PlayQueue.Undo());
        Assert.Equal(new[] { last!.Id, first!.Id }, Ids());

        Assert.True(PlayQueue.Remove(last.Id));
        PlayQueue.AddFile("D", @"C:\d.mp4");
        Assert.False(PlayQueue.Undo());
        Assert.Equal(new[] { "A", "D" }, Titles());

        Assert.True(PlayQueue.Remove(PlayQueue.Snapshot()[0].Id));
        PlayQueue.PlayNextFile("Now", @"C:\now.mp4");
        Assert.False(PlayQueue.IsPending(out _));
        Assert.Equal(new[] { "Now", "D" }, Titles());

        Assert.True(PlayQueue.ClearWaiting());
        Assert.Equal(2, PlayQueue.AddFiles([("One", @"C:\one.mp4"), ("Two", @"C:\two.mp4")], next: true));
        Assert.False(PlayQueue.IsPending(out _));
        Assert.False(PlayQueue.Undo());
        Assert.Equal(new[] { "One", "Two" }, Titles());
    }

    [Fact]
    public void AFailedEditLeavesTheUndo()
    {
        var first = PlayQueue.AddFile("A", @"C:\a.mp4");
        var second = PlayQueue.AddFile("B", @"C:\b.mp4");
        Assert.True(PlayQueue.Remove(second!.Id));

        var calls = 0;
        void Count(object? sender, EventArgs e) => calls++;
        PlayQueue.Changed += Count;
        try
        {
            Assert.False(PlayQueue.Remove(second.Id));
            Assert.False(PlayQueue.Remove(null));
            Assert.False(PlayQueue.Remove(string.Empty));
            Assert.Null(PlayQueue.AddFile(" ", " "));
            Assert.Null(PlayQueue.PlayNextFile(" ", " "));
            Assert.Null(PlayQueue.PlayNextPage("Clip", @"C:\clips\clip.mp4", null));
            Assert.Equal(0, PlayQueue.AddFiles([(" ", " ")], next: true));
            Assert.False(PlayQueue.Move(0, 0));
            Assert.False(PlayQueue.Move(0, 1));
            Assert.False(PlayQueue.Move(-1, 0));
            PlayQueue.Notify();
            Assert.Equal(1, calls);
        }
        finally
        {
            PlayQueue.Changed -= Count;
        }

        Assert.True(PlayQueue.IsPending(out var message));
        Assert.Equal("Removed \"B\".", message);
        Assert.True(PlayQueue.Undo());
        Assert.Equal(new[] { first!.Id, second.Id }, Ids());
    }

    [Fact]
    public void DismissLeavesTheQueueWithoutAWayBack()
    {
        PlayQueue.AddFile("A", @"C:\a.mp4");
        var calls = 0;
        void Count(object? sender, EventArgs e) => calls++;
        PlayQueue.Changed += Count;
        try
        {
            Assert.True(PlayQueue.ClearWaiting());
            PlayQueue.Dismiss();
            PlayQueue.Dismiss();
            Assert.Equal(2, calls);
        }
        finally
        {
            PlayQueue.Changed -= Count;
        }

        Assert.False(PlayQueue.IsPending(out _));
        Assert.False(PlayQueue.Undo());
        Assert.Empty(Titles());
    }

    private static bool All(int _) => true;

    private static string[] Titles() => PlayQueue.Snapshot().Select(item => item.Title).ToArray();

    private static string[] Ids() => PlayQueue.Snapshot().Select(item => item.Id).ToArray();
}
