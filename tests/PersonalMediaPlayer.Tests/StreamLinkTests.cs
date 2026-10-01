using System.Net;
using System.Net.Http.Headers;
using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class StreamLinkTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///C:/video.mp4")]
    [InlineData("blob:https://example.com/video")]
    [InlineData("ftp://example.com/video.mp4")]
    [InlineData(@"C:\videos\clip.mp4")]
    [InlineData("C:/videos/clip.mp4")]
    public void ShapeCheckRejectsNonHttpAddresses(string text)
    {
        Assert.False(StreamLink.TryNormalize(text, out _));
    }

    [Theory]
    [InlineData("https://cdn.example.com/v/12345?id=9", "https://cdn.example.com/v/12345?id=9")]
    [InlineData("cdn.example.com/v/12345?id=9", "https://cdn.example.com/v/12345?id=9")]
    [InlineData("http://example.com/clip.mp4", "http://example.com/clip.mp4")]
    public void ShapeCheckAcceptsHttpAddressesWithoutAFileEnding(string text, string expected)
    {
        Assert.True(StreamLink.TryNormalize(text, out var url));
        Assert.Equal(expected, url.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://example.com/clip.mp4", "clip.mp4")]
    [InlineData("https://example.com/my%20clip.webm", "my clip.webm")]
    [InlineData("https://cdn.example.com/v/12345?id=9", "cdn.example.com")]
    [InlineData("https://example.com/", "example.com")]
    public void DisplayNameUsesAReadablePathOrTheHost(string text, string expected)
    {
        Assert.True(StreamLink.TryNormalize(text, out var url));
        Assert.Equal(expected, StreamLink.DisplayName(url));
    }

    [Theory]
    [InlineData("video/mp4", true)]
    [InlineData("video/webm; charset=utf-8", true)]
    [InlineData("application/vnd.apple.mpegurl", true)]
    [InlineData("application/x-mpegurl", true)]
    [InlineData("text/html", false)]
    [InlineData("application/octet-stream", false)]
    [InlineData(null, false)]
    public void DirectMediaTypeIgnoresTheFileEnding(string? mediaType, bool media)
    {
        Assert.Equal(media, StreamLink.IsDirectMediaType(mediaType));
    }

    [Fact]
    public void ContainerBytesRecognizeMp4WebMAndOgg()
    {
        Assert.True(StreamLink.LooksLikeContainer([0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p']));
        Assert.True(StreamLink.LooksLikeContainer([0x1A, 0x45, 0xDF, 0xA3, 0, 0]));
        Assert.True(StreamLink.LooksLikeContainer("OggSrest"u8.ToArray()));
        Assert.False(StreamLink.LooksLikeContainer("<!DOCTYPE html>"u8.ToArray()));
        Assert.False(StreamLink.LooksLikeContainer([0, 1]));
    }

    [Fact]
    public async Task HeadVideoTypePassesWithoutReadingTheBody()
    {
        var handler = new ScriptHandler();
        handler.Enqueue(HttpStatusCode.OK, "video/mp4", readThrows: true);

        var result = await StreamLink.CheckAsync(new Uri("https://cdn.example.com/v/12345"), handler, CancellationToken.None);

        Assert.Equal(StreamCheckStatus.Media, result.Status);
        Assert.Equal("https://cdn.example.com/v/12345", result.Url.AbsoluteUri);
        Assert.Equal(HttpMethod.Head, handler.Requests[0].Method);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task HtmlPageIsRefusedBeforeThePlayerOpens()
    {
        var handler = new ScriptHandler();
        handler.Enqueue(HttpStatusCode.OK, "text/html", readThrows: true);

        var result = await StreamLink.CheckAsync(new Uri("https://example.com/watch"), handler, CancellationToken.None);

        Assert.Equal(StreamCheckStatus.NotVideo, result.Status);
        Assert.Equal(StreamLink.NotVideoMessage, result.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RefusedHeadFallsBackToAShortRangedRead()
    {
        var handler = new ScriptHandler();
        handler.Enqueue(HttpStatusCode.MethodNotAllowed, null);
        handler.Enqueue(HttpStatusCode.OK, "application/octet-stream", [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p']);

        var result = await StreamLink.CheckAsync(new Uri("https://cdn.example.com/media"), handler, CancellationToken.None);

        Assert.Equal(StreamCheckStatus.Media, result.Status);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("bytes=0-31", handler.Requests[1].Headers.Range?.ToString());
    }

    [Fact]
    public async Task GenericBytesThatAreNotAContainerAreRefused()
    {
        var handler = new ScriptHandler();
        handler.Enqueue(HttpStatusCode.OK, "application/octet-stream");
        handler.Enqueue(HttpStatusCode.OK, "application/octet-stream", "<html>no</html>"u8.ToArray());

        var result = await StreamLink.CheckAsync(new Uri("https://example.com/watch"), handler, CancellationToken.None);

        Assert.Equal(StreamCheckStatus.NotVideo, result.Status);
    }

    [Fact]
    public async Task RedirectStaysOnHttpAndUsesTheFinalAddress()
    {
        var handler = new ScriptHandler();
        handler.Enqueue(HttpStatusCode.Redirect, null, location: "https://cdn.example.com/v/12345");
        handler.Enqueue(HttpStatusCode.OK, "video/mp4", readThrows: true);

        var result = await StreamLink.CheckAsync(new Uri("https://example.com/go"), handler, CancellationToken.None);

        Assert.Equal(StreamCheckStatus.Media, result.Status);
        Assert.Equal("https://cdn.example.com/v/12345", result.Url.AbsoluteUri);
    }

    [Fact]
    public async Task RedirectOffHttpIsRefused()
    {
        var handler = new ScriptHandler();
        handler.Enqueue(HttpStatusCode.Redirect, null, location: "file:///C:/video.mp4");

        var result = await StreamLink.CheckAsync(new Uri("https://example.com/go"), handler, CancellationToken.None);

        Assert.Equal(StreamCheckStatus.NotVideo, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CancelledCheckDoesNotReturnMedia()
    {
        var handler = new ScriptHandler { Hold = true };
        using var cancel = new CancellationTokenSource();
        var task = StreamLink.CheckAsync(new Uri("https://example.com/clip.mp4"), handler, cancel.Token);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private sealed class ScriptHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        public bool Hold { get; init; }

        public void Enqueue(HttpStatusCode status, string? mediaType, byte[]? body = null, string? location = null, bool readThrows = false)
        {
            _responses.Enqueue(_ =>
            {
                var response = new HttpResponseMessage(status);
                if (location is not null)
                {
                    response.Headers.Location = new Uri(location, UriKind.Absolute);
                }

                if (readThrows)
                {
                    response.Content = new GuardContent(mediaType);
                }
                else if (mediaType is not null || body is not null)
                {
                    var content = new ByteArrayContent(body ?? []);
                    if (mediaType is not null)
                    {
                        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
                    }

                    response.Content = content;
                }

                return response;
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Hold)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return _responses.Dequeue()(request);
        }
    }

    private sealed class GuardContent : HttpContent
    {
        public GuardContent(string? mediaType)
        {
            if (mediaType is not null)
            {
                Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("The check read the body.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
