using PersonalMediaPlayer.Core.Storage;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class ConnectedFileGrowthTests
{
    [Fact]
    public void AFileIsStableOnlyAfterItsLengthStopsChanging()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long length = 0;
        var since = default(DateTime);

        Assert.False(ConnectedFileGrowth.IsStable(ref length, ref since, 0, now));
        Assert.False(ConnectedFileGrowth.IsStable(ref length, ref since, 10, now));
        Assert.False(ConnectedFileGrowth.IsStable(ref length, ref since, 10, now.AddSeconds(2)));
        Assert.False(ConnectedFileGrowth.IsStable(ref length, ref since, 10, now.Add(ConnectedFileGrowth.QuietPeriod) - TimeSpan.FromMilliseconds(1)));
        Assert.True(ConnectedFileGrowth.IsStable(ref length, ref since, 10, now.Add(ConnectedFileGrowth.QuietPeriod)));

        var changed = now.Add(ConnectedFileGrowth.QuietPeriod);
        Assert.False(ConnectedFileGrowth.IsStable(ref length, ref since, 25, changed));
        Assert.False(ConnectedFileGrowth.IsStable(ref length, ref since, 25, changed.AddSeconds(2)));
        Assert.True(ConnectedFileGrowth.IsStable(ref length, ref since, 25, changed.Add(ConnectedFileGrowth.QuietPeriod)));
    }
}
