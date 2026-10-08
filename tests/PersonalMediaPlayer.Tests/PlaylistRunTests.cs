using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaylistRunTests
{
    [Fact]
    public void ShuffleVisitsEveryOtherVideoOnce()
    {
        var run = new PlaylistRun();
        run.SetShuffle(true);
        var keys = new[] { "a", "b", "c", "d" };
        var seen = new List<string>();
        var current = 1;

        for (var i = 0; i < 6; i++)
        {
            var step = run.Move(keys, All, current, playNext: true, fromEnd: false, forward: true, new Random(2));
            if (step.Kind == PlaylistStepKind.Stop)
            {
                break;
            }

            Assert.Equal(PlaylistStepKind.Open, step.Kind);
            seen.Add(keys[step.Index]);
            current = step.Index;
        }

        Assert.Equal(3, seen.Count);
        Assert.Equal(seen.Distinct().Count(), seen.Count);
        Assert.DoesNotContain("b", seen);
        Assert.Equal(new[] { "a", "b", "c", "d" }, keys);
        Assert.Equal("b", run.Order[0]);
    }

    [Fact]
    public void SavedOrderIsUnchangedAndNextFollowsIt()
    {
        var run = new PlaylistRun();
        var keys = new[] { "a", "b", "c" };

        var first = run.Move(keys, All, 0, playNext: true, fromEnd: false, forward: true, new Random(1));
        var second = run.Move(keys, All, first.Index, playNext: true, fromEnd: false, forward: true, new Random(1));
        var done = run.Move(keys, All, second.Index, playNext: true, fromEnd: false, forward: true, new Random(1));

        Assert.Equal(1, first.Index);
        Assert.Equal(2, second.Index);
        Assert.Equal(PlaylistStepKind.Stop, done.Kind);
        Assert.Equal(new[] { "a", "b", "c" }, keys);
    }

    [Fact]
    public void RepeatOneReplaysUntilNextIsPressed()
    {
        var run = new PlaylistRun();
        run.CycleRepeat();
        run.CycleRepeat();
        var keys = new[] { "a", "b" };

        var again = run.Move(keys, All, 0, playNext: false, fromEnd: true, forward: true, new Random(1));
        var still = run.Move(keys, All, 0, playNext: false, fromEnd: true, forward: true, new Random(1));
        var next = run.Move(keys, All, 0, playNext: false, fromEnd: false, forward: true, new Random(1));

        Assert.Equal(PlaylistRepeat.One, run.Repeat);
        Assert.Equal(PlaylistStepKind.Replay, again.Kind);
        Assert.Equal(0, again.Index);
        Assert.Equal(PlaylistStepKind.Replay, still.Kind);
        Assert.Equal(PlaylistStepKind.Open, next.Kind);
        Assert.Equal(1, next.Index);
    }

    [Fact]
    public void RepeatOffStopsAtTheEndUnlessPlayNextIsOn()
    {
        var run = new PlaylistRun();
        var keys = new[] { "a", "b" };

        var stayed = run.Move(keys, All, 0, playNext: false, fromEnd: true, forward: true, new Random(1));
        var moved = run.Move(keys, All, 0, playNext: true, fromEnd: true, forward: true, new Random(1));

        Assert.Equal(PlaylistStepKind.Stop, stayed.Kind);
        Assert.Equal(PlaylistStepKind.Open, moved.Kind);
        Assert.Equal(1, moved.Index);
    }

    [Fact]
    public void RepeatPlaylistWrapsInSavedOrder()
    {
        var run = new PlaylistRun();
        run.CycleRepeat();
        var keys = new[] { "a", "b", "c" };

        var wrapped = run.Move(keys, All, 2, playNext: false, fromEnd: true, forward: true, new Random(1));
        var back = run.Move(keys, All, 0, playNext: true, fromEnd: false, forward: false, new Random(1));

        Assert.Equal(PlaylistRepeat.All, run.Repeat);
        Assert.Equal(0, wrapped.Index);
        Assert.Equal(PlaylistStepKind.Open, wrapped.Kind);
        Assert.Equal(2, back.Index);
    }

    [Fact]
    public void TheNextShuffleCycleDoesNotStartWithTheVideoThatJustFinished()
    {
        var run = new PlaylistRun();
        run.SetShuffle(true);
        run.CycleRepeat();
        var keys = new[] { "a", "b", "c", "d" };
        var random = new Random(5);
        var current = 0;
        string finished = "a";

        for (var i = 0; i < 8; i++)
        {
            var step = run.Move(keys, All, current, playNext: false, fromEnd: true, forward: true, random);
            Assert.NotEqual(PlaylistStepKind.Stop, step.Kind);
            if (step.Kind == PlaylistStepKind.Replay)
            {
                continue;
            }

            if (i == 3)
            {
                finished = keys[current];
                Assert.NotEqual(finished, keys[step.Index]);
            }

            current = step.Index;
        }

        var cycle = new HashSet<string> { keys[current] };
        for (var i = 0; i < 3; i++)
        {
            var step = run.Move(keys, All, current, playNext: false, fromEnd: false, forward: true, random);
            Assert.Equal(PlaylistStepKind.Open, step.Kind);
            cycle.Add(keys[step.Index]);
            current = step.Index;
        }

        Assert.Equal(4, cycle.Count);
    }

    [Fact]
    public void PreviousWalksBackThroughTheRandomOrder()
    {
        var run = new PlaylistRun();
        run.SetShuffle(true);
        var keys = new[] { "a", "b", "c", "d" };
        var random = new Random(8);
        var first = run.Move(keys, All, 0, playNext: true, fromEnd: false, forward: true, random);
        var second = run.Move(keys, All, first.Index, playNext: true, fromEnd: false, forward: true, random);
        var back = run.Move(keys, All, second.Index, playNext: true, fromEnd: false, forward: false, random);
        var start = run.Move(keys, All, back.Index, playNext: true, fromEnd: false, forward: false, random);
        var none = run.Move(keys, All, start.Index, playNext: true, fromEnd: false, forward: false, random);

        Assert.Equal(first.Index, back.Index);
        Assert.Equal(0, start.Index);
        Assert.Equal(PlaylistStepKind.Stop, none.Kind);
    }

    [Fact]
    public void ReorderingTheSavedListKeepsTheShuffle()
    {
        var run = new PlaylistRun();
        run.SetShuffle(true);
        var keys = new[] { "a", "b", "c", "d" };
        var random = new Random(11);
        var first = run.Move(keys, All, 0, playNext: true, fromEnd: false, forward: true, random);
        var firstKey = keys[first.Index];
        var expected = run.Order[run.Order.ToList().IndexOf(firstKey) + 1];
        var rotated = new[] { "d", "c", "b", "a" };
        var current = Array.IndexOf(rotated, firstKey);

        var second = run.Move(rotated, All, current, playNext: true, fromEnd: false, forward: true, random);

        Assert.Equal(expected, rotated[second.Index]);
        Assert.Equal(new[] { "a", "b", "c", "d" }, keys);
    }

    [Fact]
    public void ShuffleSkipsAVideoTheFilterLeavesOut()
    {
        var run = new PlaylistRun();
        run.SetShuffle(true);
        var keys = new[] { "a", "b", "c" };
        bool Include(int index) => index != 1;
        var step = run.Move(keys, Include, 0, playNext: true, fromEnd: false, forward: true, new Random(3));
        var done = run.Move(keys, Include, step.Index, playNext: true, fromEnd: false, forward: true, new Random(3));

        Assert.Equal("c", keys[step.Index]);
        Assert.Equal(PlaylistStepKind.Stop, done.Kind);
        Assert.DoesNotContain("b", run.Order);
    }

    [Fact]
    public void TurningShuffleOffFollowsTheSavedOrderAgain()
    {
        var run = new PlaylistRun();
        run.SetShuffle(true);
        var keys = new[] { "a", "b", "c", "d" };
        run.Move(keys, All, 0, playNext: true, fromEnd: false, forward: true, new Random(4));
        run.SetShuffle(false);

        var step = run.Move(keys, All, 1, playNext: true, fromEnd: false, forward: true, new Random(4));

        Assert.Equal(2, step.Index);
        Assert.Equal(PlaylistStepKind.Open, step.Kind);
    }

    [Fact]
    public void ASingleVideoRepeatsWhenThePlaylistRepeats()
    {
        var run = new PlaylistRun();
        run.CycleRepeat();
        var keys = new[] { "only" };

        var step = run.Move(keys, All, 0, playNext: false, fromEnd: true, forward: true, new Random(1));

        Assert.Equal(PlaylistStepKind.Replay, step.Kind);
        Assert.Equal(0, step.Index);
    }

    [Fact]
    public void NextIsReadyWhenAQueueOrAnotherPlaylistVideoIsWaiting()
    {
        var first = PlaylistEntry.Page("https://www.youtube.com/watch?v=abcdefghijk", "One");
        var second = PlaylistEntry.Page("https://www.youtube.com/watch?v=bbcdefghijk", "Two");
        var list = new Playlist
        {
            Id = "list",
            Name = "Evening",
            Videos = [first, second]
        };
        var run = new PlaylistRun();

        Assert.True(PlaylistRun.CanAdvance(2, null, null, -1, false));
        Assert.False(PlaylistRun.CanAdvance(0, null, null, -1, false));
        Assert.True(PlaylistRun.CanAdvance(0, list, run, 0, false));
        Assert.False(PlaylistRun.CanAdvance(0, list, run, 1, false));

        run.CycleRepeat();
        Assert.True(PlaylistRun.CanAdvance(0, list, run, 1, false));

        var only = new Playlist { Id = "one", Name = "One", Videos = [first] };
        var solo = new PlaylistRun();
        solo.CycleRepeat();
        Assert.False(PlaylistRun.CanAdvance(0, only, solo, 0, false));
        Assert.True(PlaylistRun.CanAdvance(1, only, solo, 0, false));

        var missing = new Playlist
        {
            Id = "gone",
            Name = "Gone",
            Videos = [PlaylistEntry.ForFile(@"C:\clips\gone.mp4")]
        };
        Assert.False(PlaylistRun.CanAdvance(0, missing, new PlaylistRun(), 0, false));
    }

    private static bool All(int _) => true;
}
