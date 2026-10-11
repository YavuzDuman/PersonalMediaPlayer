using PersonalMediaPlayer.App.Library;
using PersonalMediaPlayer.Core.Models;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class FolderRunTests
{
    [Fact]
    public void LaterVideosFollowTheCurrentSortAndSkipPhotos()
    {
        var ordered = new[]
        {
            Item("a.mp4", MediaKind.Video),
            Item("photo.png", MediaKind.Image),
            Item("b.mp4", MediaKind.Video),
            Item("missing.mp4", MediaKind.Video, missing: true),
            Item("b.mp4", MediaKind.Video),
            Item("c.mp4", MediaKind.Recording)
        };

        var rest = FolderRun.After(ordered, @"C:\clips\a.mp4");

        Assert.Equal(["b.mp4", "c.mp4"], rest.Select(item => item.DisplayName).ToArray());
        Assert.Empty(FolderRun.After(ordered, @"C:\clips\c.mp4"));
        Assert.Empty(FolderRun.After(ordered, @"C:\other\a.mp4"));
        Assert.Empty(FolderRun.After(ordered, ""));
    }

    [Fact]
    public void TheSameFolderKeepsOnlyThatDirectory()
    {
        var library = new[]
        {
            Item("one.mp4", MediaKind.Video, @"D:\Shows\one.mp4"),
            Item("two.mp4", MediaKind.Video, @"D:\Shows\two.mp4"),
            Item("other.mp4", MediaKind.Video, @"D:\Other\other.mp4"),
            Item("shot.png", MediaKind.Image, @"D:\Shows\shot.png")
        };

        var folder = FolderRun.InDirectory(library, @"d:\shows\one.mp4");

        Assert.Equal(["one.mp4", "two.mp4", "shot.png"], folder.Select(item => item.DisplayName).ToArray());
        Assert.Empty(FolderRun.InDirectory(library, ""));
    }

    private static MediaItem Item(string name, MediaKind kind, string? path = null, bool missing = false)
        => new()
        {
            Id = path ?? name,
            DisplayName = name,
            Kind = kind,
            FilePath = path ?? Path.Combine(@"C:\clips", name),
            IsLinked = missing,
            FolderName = "Shows"
        };
}
