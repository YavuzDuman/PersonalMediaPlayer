using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PersonalMediaPlayer.App.Playback;

namespace PersonalMediaPlayer.App.Download;

internal static class DownloadPoster
{
    private const int MinimumBytes = 2_048;

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly ConcurrentDictionary<string, Task<string?>> InFlight = new(StringComparer.OrdinalIgnoreCase);

    internal static event Action<string>? Saved;

    internal static string? DirectoryOverride { get; set; }

    internal static IReadOnlyList<string> CandidateAddresses(string? page)
    {
        var id = StreamThumbnail.YoutubeId(page);
        if (id is null)
        {
            return [];
        }

        return
        [
            "https://i.ytimg.com/vi/" + id + "/maxresdefault.jpg",
            "https://i.ytimg.com/vi/" + id + "/sddefault.jpg",
            "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg"
        ];
    }

    internal static bool IsPosterImage(ReadOnlySpan<byte> bytes)
        => bytes.Length >= MinimumBytes && bytes[0] == 0xFF && bytes[1] == 0xD8;

    internal static string? PageFromHistory(string json, string mediaPath)
    {
        var full = Normalize(mediaPath);
        if (full is null || string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (root is not JsonArray entries)
        {
            return null;
        }

        foreach (var entry in entries)
        {
            if (entry is not JsonObject item)
            {
                continue;
            }

            var location = item["Location"]?.GetValue<string>();
            var candidate = Normalize(location);
            if (candidate is null || !string.Equals(candidate, full, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var page = item["Url"]?.GetValue<string>();
            if (CandidateAddresses(page).Count > 0)
            {
                return page;
            }
        }

        return null;
    }

    internal static string? RewriteHistory(string json, string oldPath, string newPath)
    {
        var oldFull = Normalize(oldPath);
        var newFull = Normalize(newPath);
        if (oldFull is null || newFull is null || string.Equals(oldFull, newFull, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (root is not JsonArray entries)
        {
            return null;
        }

        var changed = false;
        foreach (var entry in entries)
        {
            if (entry is not JsonObject item)
            {
                continue;
            }

            var candidate = Normalize(item["Location"]?.GetValue<string>());
            if (candidate is null || !string.Equals(candidate, oldFull, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            item["Location"] = newFull;
            item["Name"] = Path.GetFileName(newFull);
            changed = true;
        }

        return changed ? entries.ToJsonString() : null;
    }

    internal static string? FindCached(string mediaPath)
    {
        var full = Normalize(mediaPath);
        if (full is null)
        {
            return null;
        }

        var path = CacheFile(full);
        return File.Exists(path) ? path : null;
    }

    internal static void Move(string oldPath, string newPath)
    {
        var oldFull = Normalize(oldPath);
        var newFull = Normalize(newPath);
        if (oldFull is null || newFull is null)
        {
            return;
        }

        var from = CacheFile(oldFull);
        var to = CacheFile(newFull);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || !File.Exists(from))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static Task<string?> EnsureAsync(string mediaPath, string? page)
    {
        var full = Normalize(mediaPath);
        if (full is null)
        {
            return Task.FromResult<string?>(null);
        }

        if (FindCached(full) is string cached)
        {
            return Task.FromResult<string?>(cached);
        }

        if (CandidateAddresses(page).Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        return InFlight.GetOrAdd(full, key => DownloadAsync(key, page!));
    }

    private static async Task<string?> DownloadAsync(string fullPath, string page)
    {
        try
        {
            foreach (var address in CandidateAddresses(page))
            {
                var bytes = await ReadImageAsync(address);
                if (bytes is null || !IsPosterImage(bytes))
                {
                    continue;
                }

                var destination = CacheFile(fullPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var partial = destination + ".part";
                await File.WriteAllBytesAsync(partial, bytes);
                File.Move(partial, destination, overwrite: true);
                Saved?.Invoke(fullPath);
                return destination;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
        }

        InFlight.TryRemove(fullPath, out _);
        return null;
    }

    private static async Task<byte[]?> ReadImageAsync(string address)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        request.Headers.Accept.ParseAdd("image/jpeg");
        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    private static string CacheFile(string fullPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant()))).ToLowerInvariant();
        return Path.Combine(Folder(), hash + ".jpg");
    }

    private static string Folder()
        => DirectoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalMediaPlayer",
            "thumbnails");

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
