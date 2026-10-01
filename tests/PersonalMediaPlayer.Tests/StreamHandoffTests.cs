using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class StreamHandoffTests
{
    [Fact]
    public void EncodedAddressIsExtractedOnce()
    {
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString("https://example.com/a.mp4");
        Assert.True(StreamHandoff.TryParseProtocol(argument, out var address));
        Assert.Equal("https://example.com/a.mp4", address);
    }

    [Fact]
    public void UnencodedAddressStillReadsTheQuery()
    {
        const string argument = "personalmediaplayer://play/?url=https://example.com/a.mp4";
        Assert.True(StreamHandoff.TryParseProtocol(argument, out var address));
        Assert.Equal("https://example.com/a.mp4", address);
    }

    [Fact]
    public void AddressWithoutAFileEndingKeepsItsQuery()
    {
        const string inner = "https://cdn.example.com/v/12345?id=9&x=1";
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString(inner);
        Assert.True(StreamHandoff.TryParseProtocol(argument, out var address));
        Assert.Equal(inner, address);
    }

    [Fact]
    public void EncodedPercentStaysEncodedAfterOnePass()
    {
        const string inner = "https://example.com/a%20b.mp4";
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString(inner);
        Assert.True(StreamHandoff.TryParseProtocol(argument, out var address));
        Assert.Equal(inner, address);
    }

    [Fact]
    public void PlusInTheAddressStaysAPlus()
    {
        const string argument = "personalmediaplayer://play/?url=https://example.com/a+b.mp4";
        Assert.True(StreamHandoff.TryParseProtocol(argument, out var address));
        Assert.Equal("https://example.com/a+b.mp4", address);
    }

    [Fact]
    public void ReferrerAndStartTravelWithTheMediaAddress()
    {
        const string media = "https://cdn.example.com/v/12345?id=9&x=1";
        const string page = "https://example.com/watch?v=1";
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString(media)
            + "&referrer=" + Uri.EscapeDataString(page)
            + "&t=12.5";
        Assert.True(StreamHandoff.TryParseProtocol(argument, out var address));
        Assert.Equal(media, address);
        Assert.True(StreamHandoff.TryParseProtocol(argument, out address, out var referrer, out var start));
        Assert.Equal(media, address);
        Assert.Equal(page, referrer);
        Assert.Equal(12.5, start);
        Assert.True(StreamHandoff.TryReadLaunch(argument, out var launch));
        Assert.True(StreamLink.TryNormalize(media, out var expectedMedia));
        Assert.True(StreamLink.TryNormalize(page, out var expectedPage));
        Assert.Equal(expectedMedia.AbsoluteUri, launch.Url!.AbsoluteUri);
        Assert.Equal(expectedPage.AbsoluteUri, launch.Referrer!.AbsoluteUri);
        Assert.Equal(12_500, launch.StartMs);
    }

    [Fact]
    public void MissingReferrerAndStartStayEmpty()
    {
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString("https://example.com/a.mp4");
        Assert.True(StreamHandoff.TryParseProtocol(argument, out var address, out var referrer, out var start));
        Assert.Equal("https://example.com/a.mp4", address);
        Assert.Null(referrer);
        Assert.Null(start);
        Assert.True(StreamHandoff.TryReadLaunch(argument, out var launch));
        Assert.NotNull(launch.Url);
        Assert.Null(launch.Referrer);
        Assert.Null(launch.StartMs);
    }

    [Theory]
    [InlineData("file:///C:/video.mp4")]
    [InlineData("blob:https://example.com/8c3a")]
    [InlineData("example.com/watch")]
    public void AReferrerThatIsNotHttpIsOmitted(string referrer)
    {
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString("https://example.com/a.mp4")
            + "&referrer=" + Uri.EscapeDataString(referrer)
            + "&t=3";
        Assert.True(StreamHandoff.TryReadLaunch(argument, out var launch));
        Assert.Equal("https://example.com/a.mp4", launch.Url!.AbsoluteUri);
        Assert.Null(launch.Referrer);
        Assert.Equal(3_000, launch.StartMs);
    }

    [Fact]
    public void PlusInTheReferrerStaysAPlus()
    {
        var page = "https://example.com/a+b";
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString("https://example.com/a.mp4")
            + "&referrer=" + Uri.EscapeDataString(page);
        Assert.True(StreamHandoff.TryParseProtocol(argument, out var address, out var referrer, out _));
        Assert.Equal("https://example.com/a.mp4", address);
        Assert.Equal(page, referrer);
        Assert.True(StreamHandoff.TryReadLaunch(argument, out var launch));
        Assert.True(StreamLink.TryNormalize(page, out var expected));
        Assert.Equal(expected.AbsoluteUri, launch.Referrer!.AbsoluteUri);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("nope")]
    [InlineData("12,5")]
    public void UnusableStartTimesAreIgnored(string raw)
    {
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString("https://example.com/a.mp4")
            + "&t=" + raw;
        Assert.True(StreamHandoff.TryParseProtocol(argument, out _, out _, out var start));
        if (raw == "0")
        {
            Assert.Equal(0d, start);
        }
        else
        {
            Assert.Null(start);
        }

        Assert.True(StreamHandoff.TryReadLaunch(argument, out var launch));
        Assert.NotNull(launch.Url);
        Assert.Null(launch.StartMs);
    }

    [Fact]
    public void StartTimeBeyondAWeekIsIgnored()
    {
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString("https://example.com/a.mp4")
            + "&t=" + (StreamHandoff.MaxStartSeconds + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(StreamHandoff.TryParseProtocol(argument, out _, out _, out var start));
        Assert.Equal(StreamHandoff.MaxStartSeconds + 1, start.GetValueOrDefault());
        Assert.True(StreamHandoff.TryReadLaunch(argument, out var launch));
        Assert.NotNull(launch.Url);
        Assert.Null(launch.StartMs);
    }

    [Theory]
    [InlineData("file:///C:/video.mp4")]
    [InlineData("blob:https://example.com/8c3a")]
    public void NonHttpAddressesAreReturnedAndRejectedByTheLinkCheck(string inner)
    {
        var argument = "personalmediaplayer://play/?url=" + Uri.EscapeDataString(inner);
        Assert.True(StreamHandoff.TryParseProtocol(argument, out var address));
        Assert.Equal(inner, address);
        Assert.False(StreamLink.TryNormalize(address, out _));
    }

    [Fact]
    public void MissingAddressIsAProtocolLaunchWithNothingToPlay()
    {
        Assert.True(StreamHandoff.TryParseProtocol("personalmediaplayer://play/", out var address));
        Assert.Null(address);
    }

    [Fact]
    public void OrdinaryArgumentsAreNotAProtocolLaunch()
    {
        var args = new[] { @"C:\app\PersonalMediaPlayer.App.exe", "--library" };
        Assert.Null(StreamHandoff.ProtocolArgument(args));
        Assert.False(StreamHandoff.TryParseProtocol("https://example.com/a.mp4", out _));
    }

    [Fact]
    public void ProtocolArgumentSkipsTheExecutableAndQuotes()
    {
        var args = new[]
        {
            "personalmediaplayer://play/?url=https://ignored.example/a.mp4",
            "\"personalmediaplayer://play/?url=https://example.com/a.mp4\""
        };
        Assert.Equal("personalmediaplayer://play/?url=https://example.com/a.mp4", StreamHandoff.ProtocolArgument(args));
    }

    [Fact]
    public void LaunchCommandQuotesTheExecutable()
    {
        var command = StreamHandoff.LaunchCommand(@"C:\Program Files\PersonalMediaPlayer.App.exe");
        Assert.Equal("\"C:\\Program Files\\PersonalMediaPlayer.App.exe\" \"%1\"", command);
    }

    [Fact]
    public async Task PipeDeliversEachLine()
    {
        var name = "PersonalMediaPlayer.Test." + Guid.NewGuid().ToString("N");
        var lines = new List<string>();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listen = StreamHandoff.ListenAsync(name, line =>
        {
            lines.Add(line);
            (lines.Count == 1 ? first : second).TrySetResult();
            return Task.CompletedTask;
        }, cts.Token);

        try
        {
            await StreamHandoff.SendAsync(name, "personalmediaplayer://play/?url=https://example.com/a.mp4");
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await StreamHandoff.SendAsync(name, "https://cdn.example.com/v/12345?id=9");
            await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(
                [
                    "personalmediaplayer://play/?url=https://example.com/a.mp4",
                    "https://cdn.example.com/v/12345?id=9"
                ],
                lines);
        }
        finally
        {
            cts.Cancel();
            await listen.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
