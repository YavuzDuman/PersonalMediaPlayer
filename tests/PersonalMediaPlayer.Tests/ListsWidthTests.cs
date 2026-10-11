using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class ListsWidthTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;

    public ListsWidthTests()
    {
        Monitor.Enter(Gate);
        _store = Path.Combine(Path.GetTempPath(), "pmp-lists-width-" + Guid.NewGuid().ToString("N") + ".json");
        ListsWidth.StoreOverride = _store;
    }

    public void Dispose()
    {
        ListsWidth.StoreOverride = null;
        if (File.Exists(_store))
        {
            File.Delete(_store);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void AMissingFileUsesTheUsualWidth()
    {
        Assert.Equal(ListsWidth.Default, ListsWidth.Current);
        Assert.Equal(340, ListsWidth.Current);
    }

    [Fact]
    public void ASavedWidthRoundTripsAndABadFileUsesTheUsualWidth()
    {
        ListsWidth.Save(480);
        Assert.Equal(480, ListsWidth.Current);
        Assert.Equal(480, ListsWidth.Parse(File.ReadAllText(_store)));

        File.WriteAllText(_store, "{");
        ListsWidth.StoreOverride = _store;
        Assert.Equal(ListsWidth.Default, ListsWidth.Current);

        File.WriteAllText(_store, """{"Width":"wide"}""");
        ListsWidth.StoreOverride = _store;
        Assert.Equal(ListsWidth.Default, ListsWidth.Current);
    }

    [Fact]
    public void TheWidthStaysInsideTheReadableRange()
    {
        Assert.Equal(ListsWidth.Min, ListsWidth.Clamp(40, 1600));
        Assert.Equal(ListsWidth.Max, ListsWidth.Clamp(2000, 1600));
        Assert.Equal(ListsWidth.Default, ListsWidth.Clamp(0, 1600));
        Assert.Equal(ListsWidth.Default, ListsWidth.Clamp(double.NaN, 0));
        Assert.Equal(480, ListsWidth.Clamp(480, 0));

        ListsWidth.Save(20);
        Assert.Equal(ListsWidth.Min, ListsWidth.Current);
        ListsWidth.Save(900);
        Assert.Equal(ListsWidth.Max, ListsWidth.Current);
    }

    [Fact]
    public void ANarrowWindowKeepsRoomForThePicture()
    {
        Assert.Equal(368, ListsWidth.Clamp(500, 800));
        Assert.Equal(220, ListsWidth.Clamp(340, 500));
        Assert.Equal(ListsWidth.Max, ListsWidth.Clamp(ListsWidth.Max, 2000));
    }
}
