using PersonalMediaPlayer.App.Library;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class LibrarySortChoiceTests : IDisposable
{
    private readonly string _directory;

    public LibrarySortChoiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pmp-library-sort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        LibrarySortChoice.StoreOverride = Path.Combine(_directory, LibrarySortChoice.FileName);
    }

    [Fact]
    public void AMissingFileUsesNewest()
    {
        Assert.Equal(LibrarySort.Newest, LibrarySortChoice.Current);
        Assert.Equal(LibrarySort.Newest, LibrarySortChoice.Normalize(-1));
        Assert.Equal(LibrarySort.Newest, LibrarySortChoice.Normalize(9));
        Assert.Equal(LibrarySort.Type, LibrarySortChoice.Normalize(LibrarySort.Type));
    }

    [Fact]
    public void TheLastSortComesBackWithItsDirection()
    {
        LibrarySortChoice.Save(LibrarySort.NameDescending);
        Assert.Equal(LibrarySort.NameDescending, LibrarySortChoice.Current);

        LibrarySortChoice.Save(LibrarySort.Longest);
        LibrarySortChoice.StoreOverride = Path.Combine(_directory, "other.txt");
        LibrarySortChoice.StoreOverride = Path.Combine(_directory, LibrarySortChoice.FileName);

        Assert.Equal(LibrarySort.Longest, LibrarySortChoice.Current);
        Assert.Equal("6", File.ReadAllText(Path.Combine(_directory, LibrarySortChoice.FileName)));
    }

    [Fact]
    public void ABadFileStaysOnNewest()
    {
        var path = Path.Combine(_directory, LibrarySortChoice.FileName);
        File.WriteAllText(path, "nope");
        LibrarySortChoice.StoreOverride = path;

        Assert.Equal(LibrarySort.Newest, LibrarySortChoice.Current);

        File.WriteAllText(path, "  3  ");
        LibrarySortChoice.StoreOverride = Path.Combine(_directory, "other.txt");
        LibrarySortChoice.StoreOverride = path;

        Assert.Equal(LibrarySort.NameDescending, LibrarySortChoice.Current);
    }

    public void Dispose()
    {
        LibrarySortChoice.StoreOverride = null;
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
