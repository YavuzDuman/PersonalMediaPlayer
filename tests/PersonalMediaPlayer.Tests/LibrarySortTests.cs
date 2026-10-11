using PersonalMediaPlayer.App.Library;
using PersonalMediaPlayer.Core.Models;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class LibrarySortTests
{
    private static readonly DateTimeOffset Older = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Newer = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheMenuOffersEachKindInBothDirections()
    {
        Assert.Equal(
            [
                "Date added, newest",
                "Date added, oldest",
                "Name A–Z",
                "Name Z–A",
                "File size, largest",
                "File size, smallest",
                "Video length, longest",
                "Video length, shortest",
                "Type"
            ],
            LibrarySort.Options);
        Assert.True(LibrarySort.NeedsLength(LibrarySort.Longest));
        Assert.True(LibrarySort.NeedsLength(LibrarySort.Shortest));
        Assert.False(LibrarySort.NeedsLength(LibrarySort.Newest));
        Assert.False(LibrarySort.NeedsLength(LibrarySort.NameAscending));
    }

    [Fact]
    public void DateAddedNameAndSizeEachRunBothWays()
    {
        var items = new[]
        {
            Item("Bravo", MediaKind.Video, 20, Older, "b.mp4"),
            Item("alpha", MediaKind.Image, 50, Newer, "a.png"),
            Item("Alpha", MediaKind.Video, 10, Newer, "a.mp4")
        };

        Assert.Equal(["Alpha", "alpha", "Bravo"], Names(LibrarySort.Order(items, LibrarySort.Newest)));
        Assert.Equal(["Bravo", "Alpha", "alpha"], Names(LibrarySort.Order(items, LibrarySort.Oldest)));
        Assert.Equal(["Alpha", "alpha", "Bravo"], Names(LibrarySort.Order(items, LibrarySort.NameAscending)));
        Assert.Equal(["Bravo", "Alpha", "alpha"], Names(LibrarySort.Order(items, LibrarySort.NameDescending)));
        Assert.Equal(["alpha", "Bravo", "Alpha"], Names(LibrarySort.Order(items, LibrarySort.Largest)));
        Assert.Equal(["Alpha", "Bravo", "alpha"], Names(LibrarySort.Order(items, LibrarySort.Smallest)));
    }

    [Fact]
    public void VideoLengthPutsKnownVideosFirstInBothDirections()
    {
        var items = new[]
        {
            Item("Photo", MediaKind.Image, 5, Older, "photo.png"),
            Item("Unknown", MediaKind.Video, 5, Older, "unknown.mp4"),
            Item("Short", MediaKind.Video, 5, Newer, "short.mp4"),
            Item("Long", MediaKind.Recording, 5, Older, "long.mp4")
        };

        Assert.Equal(["Long", "Short", "Photo", "Unknown"], Names(LibrarySort.Order(items, LibrarySort.Longest, Length)));
        Assert.Equal(["Short", "Long", "Photo", "Unknown"], Names(LibrarySort.Order(items, LibrarySort.Shortest, Length)));
    }

    [Fact]
    public void AFilteredListKeepsItsMatchesWhenSorted()
    {
        var items = new[]
        {
            Item("Cat long", MediaKind.Video, 30, Older, "long.mp4"),
            Item("Dog", MediaKind.Image, 80, Newer, "dog.png"),
            Item("Cat short", MediaKind.Video, 10, Newer, "short.mp4")
        };
        var matches = items.Where(item =>
            item.IsVideo
            && item.DisplayName.Contains("cat", StringComparison.OrdinalIgnoreCase)).ToArray();

        var ordered = LibrarySort.Order(matches, LibrarySort.Longest, Length);

        Assert.Equal(["Cat long", "Cat short"], Names(ordered));
        Assert.Equal(2, ordered.Count);
    }

    [Fact]
    public void TypeGroupsImagesAndVideosThenUsesTheName()
    {
        var items = new[]
        {
            Item("Zebra", MediaKind.Video, 1, Older, "z.mp4"),
            Item("Apple", MediaKind.Image, 1, Newer, "a.png"),
            Item("Mango", MediaKind.Video, 1, Newer, "m.mp4")
        };

        Assert.Equal(["Apple", "Mango", "Zebra"], Names(LibrarySort.Order(items, LibrarySort.Type)));
    }

    private static long Length(MediaItem item) => item.FilePath switch
    {
        "long.mp4" => 90_000,
        "short.mp4" => 5_000,
        _ => 0
    };

    private static IReadOnlyList<string> Names(IEnumerable<MediaItem> items)
        => items.Select(item => item.DisplayName).ToArray();

    private static MediaItem Item(string name, MediaKind kind, long size, DateTimeOffset added, string path)
        => new()
        {
            Id = path,
            DisplayName = name,
            Kind = kind,
            FileSizeBytes = size,
            ImportedAt = added,
            FilePath = path,
            FolderName = "Library"
        };
}
