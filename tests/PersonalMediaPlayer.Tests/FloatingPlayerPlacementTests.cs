using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class FloatingPlayerPlacementTests
{
    [Fact]
    public void ThePlayerSitsAtTheBottomRightOfTheWorkArea()
    {
        var spot = FloatingPlayerPlacement.Place(0, 0, 1920, 1080, 1);

        Assert.Equal(360, spot.Width);
        Assert.Equal(368, spot.Height);
        Assert.Equal(1920 - 360 - 16, spot.X);
        Assert.Equal(1080 - 368 - 16, spot.Y);
    }

    [Fact]
    public void AScaledScreenKeepsTheSameDipSize()
    {
        var spot = FloatingPlayerPlacement.Place(100, 40, 2560, 1440, 1.5);

        Assert.Equal(540, spot.Width);
        Assert.Equal(552, spot.Height);
        Assert.Equal(100 + 2560 - 540 - 24, spot.X);
        Assert.Equal(40 + 1440 - 552 - 24, spot.Y);
    }

    [Fact]
    public void ASmallWorkAreaStillKeepsThePlayerOnScreen()
    {
        var spot = FloatingPlayerPlacement.Place(0, 0, 300, 200, 1);

        Assert.True(spot.Width < 300);
        Assert.True(spot.Height < 200);
        Assert.True(spot.X >= 0);
        Assert.True(spot.Y >= 0);
        Assert.True(spot.X + spot.Width <= 300);
        Assert.True(spot.Y + spot.Height <= 200);
    }
}
