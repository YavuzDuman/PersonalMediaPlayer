using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class SubtitleLookTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;

    public SubtitleLookTests()
    {
        Monitor.Enter(Gate);
        _store = Path.Combine(Path.GetTempPath(), "pmp-subtitle-look-" + Guid.NewGuid().ToString("N") + ".json");
        SubtitleLook.StoreOverride = _store;
    }

    public void Dispose()
    {
        SubtitleLook.StoreOverride = null;
        if (File.Exists(_store))
        {
            File.Delete(_store);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void AMissingFileUsesTheOriginalLook()
    {
        var look = SubtitleLook.Current;
        Assert.Equal(SubtitleStyle.Default, look);
        Assert.Equal(0, look.DelayMs);
        Assert.Equal(SubtitleSize.Medium, look.Size);
        Assert.Equal(22, SubtitleLook.FontSize(look.Size));
        Assert.Equal(0xFFFFFFFFu, SubtitleLook.TextArgb(look.Color));
        Assert.Equal(0xCC000000u, SubtitleLook.PlateArgb(look.Plate));
        Assert.Equal(SubtitleSpot.Bottom, look.Spot);
        Assert.Equal("In sync", SubtitleLook.DelayLabel(0));
    }

    [Fact]
    public void ASavedLookRoundTripsAndABadFieldKeepsTheRest()
    {
        var style = new SubtitleStyle(-2_500, SubtitleSize.Large, SubtitleTextColor.Yellow, SubtitlePlate.None, SubtitleSpot.Top);
        SubtitleLook.Save(style);
        Assert.Equal(style, SubtitleLook.Current);
        Assert.Equal(style, SubtitleLook.Parse(File.ReadAllText(_store)));
        Assert.Equal(34, SubtitleLook.FontSize(SubtitleSize.Large));
        Assert.Equal("-2.5 s", SubtitleLook.DelayLabel(-2_500));
        Assert.Equal("+1.5 s", SubtitleLook.DelayLabel(1_500));

        File.WriteAllText(_store, """{"DelayMs":150,"Color":"nope","Size":"Large","Background":"Light","Position":"1"}""");
        SubtitleLook.StoreOverride = _store;
        var kept = SubtitleLook.Current;
        Assert.Equal(200, kept.DelayMs);
        Assert.Equal(SubtitleSize.Large, kept.Size);
        Assert.Equal(SubtitleTextColor.White, kept.Color);
        Assert.Equal(SubtitlePlate.Light, kept.Plate);
        Assert.Equal(SubtitleSpot.Bottom, kept.Spot);

        File.WriteAllText(_store, "{");
        SubtitleLook.StoreOverride = _store;
        Assert.Equal(SubtitleStyle.Default, SubtitleLook.Current);

        File.WriteAllText(_store, """{"DelayMs":999999,"Size":"Small","Color":"Black","Background":"None","Position":"Middle"}""");
        SubtitleLook.StoreOverride = _store;
        var clamped = SubtitleLook.Current;
        Assert.Equal(15_000, clamped.DelayMs);
        Assert.Equal(SubtitleLook.MinDelayMs, SubtitleLook.ClampDelay(-999_999));
        Assert.Equal(SubtitleSize.Small, clamped.Size);
        Assert.Equal(SubtitleTextColor.Black, clamped.Color);
        Assert.Equal(SubtitlePlate.None, clamped.Plate);
        Assert.Equal(SubtitleSpot.Middle, clamped.Spot);
    }

    [Fact]
    public void DelayMovesTheLineEarlierOrLaterAndResetRestoresIt()
    {
        var cues = new SubtitleCue[]
        {
            new(0, 1_000, "A"),
            new(1_000, 2_000, "B")
        };

        Assert.Equal(0, SubtitleLook.CueAt(cues, 0, 0));
        Assert.Equal(1, SubtitleLook.CueAt(cues, 1_000, 0));
        Assert.Equal(-1, SubtitleLook.CueAt(cues, 2_000, 0));
        Assert.Equal(-1, SubtitleLook.CueAt(cues, 0, 500));
        Assert.Equal(0, SubtitleLook.CueAt(cues, 500, 500));
        Assert.Equal(0, SubtitleLook.CueAt(cues, 1_000, 500));
        Assert.Equal(1, SubtitleLook.CueAt(cues, 1_500, 500));
        Assert.Equal(0, SubtitleLook.CueAt(cues, 0, -500));
        Assert.Equal(1, SubtitleLook.CueAt(cues, 500, -500));
        Assert.Equal(-1, SubtitleLook.CueAt(cues, 1_500, -500));
        Assert.Equal(0, SubtitleLook.CueAt(cues, 400, 0));
        Assert.Equal(-1, SubtitleLook.CueAt([], 400, -1_000));
        Assert.Equal(6_000, SubtitleLook.PlaybackMs(1_000, 5_000));
        Assert.Equal(0, SubtitleLook.PlaybackMs(1_000, -5_000));
        Assert.Equal(1_000, SubtitleLook.PlaybackMs(1_000, 0));
        Assert.Equal(16_000, SubtitleLook.PlaybackMs(1_000, 99_999));

        Assert.Equal(15_000, SubtitleLook.Step(15_000, 500));
        Assert.Equal(-15_000, SubtitleLook.Step(-15_000, -500));
        Assert.Equal(-500, SubtitleLook.Step(0, -500));
        Assert.Equal(1_500, SubtitleLook.FromSeconds(1.5));
        Assert.Equal(0, SubtitleLook.FromSeconds(double.NaN));

        SubtitleLook.Save(new SubtitleStyle(500, SubtitleSize.Small, SubtitleTextColor.Cyan, SubtitlePlate.Light, SubtitleSpot.Middle));
        SubtitleLook.Reset();
        Assert.Equal(SubtitleStyle.Default, SubtitleLook.Current);
        Assert.Equal(SubtitleStyle.Default, SubtitleLook.Parse(File.ReadAllText(_store)));

        var other = Path.Combine(Path.GetTempPath(), "pmp-subtitle-look-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SubtitleLook.StoreOverride = other;
            Assert.Equal(SubtitleStyle.Default, SubtitleLook.Current);
        }
        finally
        {
            SubtitleLook.StoreOverride = _store;
            if (File.Exists(other))
            {
                File.Delete(other);
            }
        }
    }
}
