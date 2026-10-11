using PersonalMediaPlayer.App.Editing;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class WorkDraftsTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;

    public WorkDraftsTests()
    {
        Monitor.Enter(Gate);
        _store = Path.Combine(Path.GetTempPath(), "pmp-drafts-" + Guid.NewGuid().ToString("N") + ".json");
        WorkDrafts.StoreOverride = _store;
    }

    public void Dispose()
    {
        WorkDrafts.StoreOverride = null;
        if (File.Exists(_store))
        {
            File.Delete(_store);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void AMissingFileAndABadFileLeaveTheListEmpty()
    {
        Assert.Empty(WorkDrafts.Edits());
        Assert.Empty(WorkDrafts.Merges());

        File.WriteAllText(_store, "{");
        Assert.Empty(WorkDrafts.Edits());
        Assert.Empty(WorkDrafts.Merges());
    }

    [Fact]
    public void AnEditDraftKeepsTheWorkAndANewPathKeepsItToo()
    {
        var saved = WorkDrafts.SaveEdit(SampleEdit(@"C:\clips\one.mp4"));
        Assert.NotNull(saved);
        var again = WorkDrafts.SaveEdit(saved!);
        Assert.Equal(saved!.Id, again!.Id);
        Assert.Single(WorkDrafts.Edits());

        var other = WorkDrafts.SaveEdit(SampleEdit(@"C:\clips\two.mp4"));
        Assert.Equal(2, WorkDrafts.Edits().Count);

        var opened = WorkDrafts.Edit(saved.Id);
        Assert.Equal("one", opened!.Name);
        Assert.Equal(1.5, opened.Speed);
        Assert.Equal(80, opened.Volume);
        Assert.Equal(2_000, opened.TrimStartMs);
        Assert.Equal(8_000, opened.TrimEndMs);
        Assert.Equal(3_000, opened.Cuts[0].StartMs);
        Assert.Equal(4_000, opened.Parts[0].EndMs);
        Assert.Equal(30, opened.Parts[0].VolumePercent);
        Assert.Equal(0.1, opened.CropLeft);
        Assert.Equal(1, opened.QuarterTurns);
        Assert.True(opened.FlipHorizontal);
        Assert.True(opened.VideoFadeIn);
        Assert.Equal(2, opened.VideoFadeInSeconds);
        Assert.True(opened.AudioFadeOut);

        var moved = WorkDrafts.PointEdit(saved.Id, @"D:\moved\one.mp4");
        Assert.Equal(@"D:\moved\one.mp4", moved!.VideoPath);
        Assert.Equal("one.mp4", moved.VideoName);
        Assert.Equal(1.5, moved.Speed);
        Assert.Equal(3_000, moved.Cuts[0].StartMs);
        Assert.Equal(other!.Id, WorkDrafts.Edit(other.Id)!.Id);

        WorkDrafts.RemoveEdit(saved.Id);
        Assert.Single(WorkDrafts.Edits());
        Assert.Equal(other.Id, WorkDrafts.Edits()[0].Id);
    }

    [Fact]
    public void AMergeDraftKeepsOrderAndAFoundClipKeepsTheRest()
    {
        var saved = WorkDrafts.SaveMerge(new MergeDraft
        {
            Name = "Evening",
            Clips =
            [
                Clip(@"C:\clips\a.mp4", 1, 4, videoIn: true),
                Clip(@"C:\clips\b.mp4", 0, 6, audioOut: true)
            ]
        });
        Assert.NotNull(saved);

        var opened = WorkDrafts.Merge(saved!.Id);
        Assert.Equal("Evening", opened!.Name);
        Assert.Equal(@"C:\clips\a.mp4", opened.Clips[0].Path);
        Assert.Equal(@"C:\clips\b.mp4", opened.Clips[1].Path);
        Assert.Equal(1, opened.Clips[0].StartSeconds);
        Assert.Equal(4, opened.Clips[0].EndSeconds);
        Assert.True(opened.Clips[0].VideoFadeIn);
        Assert.True(opened.Clips[1].AudioFadeOut);
        Assert.Equal(2, opened.Clips[1].AudioFadeOutSeconds);

        var found = WorkDrafts.PointMerge(saved.Id, 0, @"E:\later\a.mp4");
        Assert.Equal(@"E:\later\a.mp4", found!.Clips[0].Path);
        Assert.Equal("a.mp4", found.Clips[0].Name);
        Assert.Equal(1, found.Clips[0].StartSeconds);
        Assert.True(found.Clips[0].VideoFadeIn);
        Assert.Equal(@"C:\clips\b.mp4", found.Clips[1].Path);
        Assert.True(found.Clips[1].AudioFadeOut);

        Assert.Null(WorkDrafts.SaveMerge(new MergeDraft()));
        Assert.Single(WorkDrafts.Merges());
    }

    [Fact]
    public void ASavedEditMatchesUntilTheSpeedChanges()
    {
        var saved = SampleEdit(@"C:\clips\one.mp4");
        saved.Speed = 2;
        var same = WorkDrafts.CopyEdit(saved);
        same.Id = "other-id";
        same.Name = "Renamed";
        same.SavedUtc = "not-a-time";
        same.VideoPath = @"c:\clips\one.mp4";
        Assert.False(WorkDrafts.EditDiffersFromSaved(saved, same, differsFromBlank: true));

        same.Speed = 1.5;
        Assert.True(WorkDrafts.EditDiffersFromSaved(saved, same, differsFromBlank: true));
        same.Speed = 2;
        same.Cuts[0].EndMs = 5_000;
        Assert.True(WorkDrafts.EditDiffersFromSaved(saved, same, differsFromBlank: true));

        Assert.False(WorkDrafts.EditDiffersFromSaved(null, saved, differsFromBlank: false));
        Assert.True(WorkDrafts.EditDiffersFromSaved(null, saved, differsFromBlank: true));
    }

    [Fact]
    public void AMergeListMatchesUntilTheClipsChange()
    {
        var saved = new MergeDraft
        {
            Id = "keep",
            Name = "Evening",
            Clips =
            [
                Clip(@"C:\clips\a.mp4", 0, 10),
                Clip(@"C:\clips\b.mp4", 1, 4, videoIn: true)
            ]
        };
        var same = WorkDrafts.CopyMerge(saved);
        same.Id = "ignored";
        same.Name = "Other name";
        same.SavedUtc = "later";
        Assert.False(WorkDrafts.MergeDiffersFromSaved(saved, same));

        var trimmed = WorkDrafts.CopyMerge(saved);
        trimmed.Clips[0].StartSeconds = 2;
        Assert.True(WorkDrafts.MergeDiffersFromSaved(saved, trimmed));

        var reordered = WorkDrafts.CopyMerge(saved);
        (reordered.Clips[0], reordered.Clips[1]) = (reordered.Clips[1], reordered.Clips[0]);
        Assert.True(WorkDrafts.MergeDiffersFromSaved(saved, reordered));

        Assert.False(WorkDrafts.MergeDiffersFromSaved(saved, new MergeDraft()));
        Assert.True(WorkDrafts.MergeDiffersFromSaved(null, saved));
    }

    [Fact]
    public void ADifferentListKeepsTheOldMergeDraft()
    {
        var first = WorkDrafts.SaveMerge(new MergeDraft
        {
            Id = "keep",
            Clips =
            [
                Clip(@"C:\clips\a.mp4", 0, 4),
                Clip(@"C:\clips\b.mp4", 0, 4)
            ]
        });
        var second = WorkDrafts.SaveMerge(new MergeDraft
        {
            Clips = [Clip(@"C:\clips\c.mp4", 0, 4)]
        });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.Id, second!.Id);
        Assert.Equal(2, WorkDrafts.Merges().Count);
        Assert.Equal(@"C:\clips\a.mp4", WorkDrafts.Merge(first.Id)!.Clips[0].Path);
        Assert.Equal(@"C:\clips\c.mp4", WorkDrafts.Merge(second.Id)!.Clips[0].Path);

        var updated = WorkDrafts.SaveMerge(new MergeDraft
        {
            Id = first.Id,
            Clips = [Clip(@"C:\clips\a.mp4", 1, 4), Clip(@"C:\clips\b.mp4", 0, 4)]
        });
        Assert.Equal(first.Id, updated!.Id);
        Assert.Equal(2, WorkDrafts.Merges().Count);
        Assert.Equal(1, WorkDrafts.Merge(first.Id)!.Clips[0].StartSeconds);
    }

    [Fact]
    public void AnotherVideoIsNotTheMergeClip()
    {
        Assert.True(WorkDrafts.SameVideo(@"C:\clips\a.mp4", @"c:\clips\a.mp4"));
        Assert.False(WorkDrafts.SameVideo(@"C:\clips\a.mp4", @"C:\clips\b.mp4"));
        Assert.False(WorkDrafts.SameVideo(@"C:\clips\a.mp4", " "));
        Assert.False(WorkDrafts.SameVideo(null, null));
    }

    private static EditDraft SampleEdit(string path) => new()
    {
        Name = Path.GetFileNameWithoutExtension(path),
        VideoPath = path,
        VideoName = Path.GetFileName(path),
        Speed = 1.5,
        Volume = 80,
        TrimStartMs = 2_000,
        TrimEndMs = 8_000,
        Cuts = [new TimeMark { StartMs = 3_000, EndMs = 3_500 }],
        Parts = [new LevelMark { StartMs = 3_000, EndMs = 4_000, VolumePercent = 30 }],
        MarkStartMs = 3_000,
        MarkEndMs = 3_500,
        CropLeft = 0.1,
        CropTop = 0.2,
        CropRight = 0.9,
        CropBottom = 0.8,
        QuarterTurns = 1,
        FlipHorizontal = true,
        VideoFadeIn = true,
        VideoFadeInSeconds = 2,
        AudioFadeOut = true,
        AudioFadeOutSeconds = 1.5
    };

    private static MergeClipDraft Clip(string path, double start, double end, bool videoIn = false, bool audioOut = false) => new()
    {
        Path = path,
        Name = Path.GetFileName(path),
        DurationSeconds = 10,
        StartSeconds = start,
        EndSeconds = end,
        HasAudio = true,
        Width = 1280,
        Height = 720,
        VideoFadeIn = videoIn,
        VideoFadeInSeconds = 2,
        AudioFadeOut = audioOut,
        AudioFadeOutSeconds = 2
    };
}
