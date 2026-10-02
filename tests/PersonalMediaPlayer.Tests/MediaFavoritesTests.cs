using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class MediaFavoritesTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;

    public MediaFavoritesTests()
    {
        Monitor.Enter(Gate);
        _store = Path.Combine(Path.GetTempPath(), "pmp-favorites-" + Guid.NewGuid().ToString("N") + ".json");
        MediaFavorites.StoreOverride = _store;
    }

    public void Dispose()
    {
        MediaFavorites.StoreOverride = null;
        if (File.Exists(_store))
        {
            File.Delete(_store);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void AddMany_writes_the_whole_selection_once()
    {
        var changes = 0;
        void OnChanged(object? sender, EventArgs e) => changes++;
        MediaFavorites.Changed += OnChanged;
        try
        {
            var added = MediaFavorites.AddMany(
            [
                @"C:\library\one.mp4",
                @"C:\library\two.mp4",
                @"c:\library\one.mp4",
                " ",
                @"C:\library\photo.png"
            ]);

            Assert.Equal(3, added);
            Assert.Equal(1, changes);
            Assert.Equal(0, MediaFavorites.AddMany([@"C:\library\ONE.mp4"]));
            Assert.Equal(1, changes);
            Assert.True(MediaFavorites.Contains(@"C:\LIBRARY\photo.png"));
            var saved = File.ReadAllText(_store);
            Assert.Contains("one.mp4", saved);
            Assert.Contains("two.mp4", saved);
            Assert.Contains("photo.png", saved);
        }
        finally
        {
            MediaFavorites.Changed -= OnChanged;
        }
    }

    [Fact]
    public void RemoveMany_drops_the_selection_in_one_write()
    {
        MediaFavorites.AddMany([@"C:\library\one.mp4", @"C:\library\two.mp4", @"C:\library\three.mp4"]);
        var changes = 0;
        void OnChanged(object? sender, EventArgs e) => changes++;
        MediaFavorites.Changed += OnChanged;
        try
        {
            var removed = MediaFavorites.RemoveMany([@"C:\library\ONE.mp4", @"C:\library\missing.mp4", @"C:\library\three.mp4"]);
            Assert.Equal(2, removed);
            Assert.Equal(1, changes);
            Assert.False(MediaFavorites.Contains(@"C:\library\one.mp4"));
            Assert.True(MediaFavorites.Contains(@"C:\library\two.mp4"));
            Assert.Equal(0, MediaFavorites.RemoveMany([@"C:\library\one.mp4"]));
            Assert.Equal(1, changes);
        }
        finally
        {
            MediaFavorites.Changed -= OnChanged;
        }
    }
}
