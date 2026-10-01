using System.Net;
using System.Net.Http.Headers;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Subtitles;

namespace PersonalMediaPlayer.App.Playback;

internal enum StreamCheckStatus
{
    Media,
    NotVideo,
    Failed
}

internal sealed record StreamCheckResult(StreamCheckStatus Status, Uri Url, string Message);

internal sealed record StreamOpenRequest(
    Uri Url,
    string DisplayName,
    Uri? Referrer = null,
    long? StartMs = null,
    Uri? Page = null,
    Uri? Audio = null,
    IReadOnlyList<DownloadSubtitle>? Subtitles = null,
    DownloadQuality? Quality = null,
    SavedWord? Focus = null);

internal static class StreamLink
{
    internal const string EnterAddressMessage = "Enter an http or https video address.";

    internal const string NotVideoMessage = "That address is not a video.";

    internal const string OpenFailedMessage = "The video could not be opened.";

    private const int MaxRedirects = 5;

    private const int MaxSteps = 8;

    public static bool TryNormalize(string? text, out Uri url)
    {
        url = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Contains('\\') || LooksLikeDiskPath(trimmed))
        {
            return false;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            if (trimmed.StartsWith('/') || trimmed.StartsWith('.'))
            {
                return false;
            }

            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) || !IsHttp(parsed) || string.IsNullOrEmpty(parsed.Host))
        {
            return false;
        }

        url = parsed;
        return true;
    }

    public static string DisplayName(Uri url)
    {
        var path = url.AbsolutePath;
        var slash = path.LastIndexOf('/');
        var name = Uri.UnescapeDataString(slash >= 0 ? path[(slash + 1)..] : path).Trim();
        if (name.Length is > 0 and <= 80 && name.Any(char.IsLetter))
        {
            return name;
        }

        return url.Host;
    }

    public static async Task<StreamCheckResult> CheckAsync(Uri url, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        return await CheckAsync(url, handler, cancellationToken);
    }

    internal static async Task<StreamCheckResult> CheckAsync(Uri url, HttpMessageHandler handler, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PersonalMediaPlayer/1.0");
        try
        {
            return await CheckCoreAsync(client, url, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(url);
        }
        catch (HttpRequestException)
        {
            return Failed(url);
        }
    }

    internal static bool IsDirectMediaType(string? mediaType)
    {
        var type = MediaType(mediaType);
        return type.StartsWith("video/", StringComparison.Ordinal)
            || type is "application/vnd.apple.mpegurl" or "application/x-mpegurl";
    }

    internal static bool ShouldSniff(string? mediaType)
    {
        var type = MediaType(mediaType);
        return type.Length == 0
            || type is "application/octet-stream" or "application/binary" or "binary/octet-stream";
    }

    internal static bool LooksLikeContainer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4
            && bytes[0] == 0x1A
            && bytes[1] == 0x45
            && bytes[2] == 0xDF
            && bytes[3] == 0xA3)
        {
            return true;
        }

        if (bytes.Length >= 4
            && bytes[0] == (byte)'O'
            && bytes[1] == (byte)'g'
            && bytes[2] == (byte)'g'
            && bytes[3] == (byte)'S')
        {
            return true;
        }

        return bytes.Length >= 8
            && bytes[4] == (byte)'f'
            && bytes[5] == (byte)'t'
            && bytes[6] == (byte)'y'
            && bytes[7] == (byte)'p';
    }

    private static async Task<StreamCheckResult> CheckCoreAsync(HttpClient client, Uri start, CancellationToken cancellationToken)
    {
        var current = start;
        var method = HttpMethod.Head;
        var redirects = 0;
        for (var step = 0; step < MaxSteps; step++)
        {
            using var request = new HttpRequestMessage(method, current);
            if (method == HttpMethod.Get)
            {
                request.Headers.Range = new RangeHeaderValue(0, 31);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (IsRedirect(response.StatusCode))
            {
                if (redirects == MaxRedirects)
                {
                    return Failed(current);
                }

                if (!TryFollow(current, response, out var next, out var leftHttp))
                {
                    return leftHttp ? NotVideo(current) : Failed(current);
                }

                redirects++;
                current = next;
                if (response.StatusCode is not (HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                {
                    method = HttpMethod.Get;
                }

                continue;
            }

            if (method == HttpMethod.Head && IsHeadRefused(response.StatusCode))
            {
                method = HttpMethod.Get;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failed(current);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (IsDirectMediaType(mediaType))
            {
                return Media(current);
            }

            if (!ShouldSniff(mediaType))
            {
                return NotVideo(current);
            }

            if (method == HttpMethod.Head)
            {
                method = HttpMethod.Get;
                continue;
            }

            var prefix = await ReadPrefixAsync(response, cancellationToken);
            return LooksLikeContainer(prefix) ? Media(current) : NotVideo(current);
        }

        return Failed(current);
    }

    private static bool TryFollow(Uri current, HttpResponseMessage response, out Uri next, out bool leftHttp)
    {
        next = null!;
        leftHttp = false;
        var location = response.Headers.Location;
        if (location is null)
        {
            return false;
        }

        var resolved = location.IsAbsoluteUri ? location : new Uri(current, location);
        if (!IsHttp(resolved))
        {
            leftHttp = true;
            return false;
        }

        next = resolved;
        return true;
    }

    private static async Task<byte[]> ReadPrefixAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var buffer = new byte[32];
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return buffer[..read];
    }

    private static bool IsRedirect(HttpStatusCode status)
        => status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.PermanentRedirect
            or HttpStatusCode.TemporaryRedirect;

    private static bool IsHeadRefused(HttpStatusCode status)
        => status is HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented;

    private static bool IsHttp(Uri url)
        => url.Scheme is "http" or "https";

    private static bool LooksLikeDiskPath(string text)
        => text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' && (text[2] == '\\' || text[2] == '/');

    private static string MediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return string.Empty;
        }

        var separator = mediaType.IndexOf(';');
        var type = (separator >= 0 ? mediaType[..separator] : mediaType).Trim();
        return type.ToLowerInvariant();
    }

    private static StreamCheckResult Media(Uri url)
        => new(StreamCheckStatus.Media, url, string.Empty);

    private static StreamCheckResult NotVideo(Uri url)
        => new(StreamCheckStatus.NotVideo, url, NotVideoMessage);

    private static StreamCheckResult Failed(Uri url)
        => new(StreamCheckStatus.Failed, url, OpenFailedMessage);
}
