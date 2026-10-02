using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalMediaPlayer.App.Playback;

internal readonly record struct ResumePoint(string Key, long TimeMs, long DurationMs, string? Title, DateTimeOffset Seen, string? Thumbnail);

internal static class PlaybackProgress
{
    private const long ResumeAfterMs = 5_000;
    private const long FinishedTailMs = 10_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "playback-positions.json");

    internal static string? StoreOverride { get; set; }

    private static string FilePath => StoreOverride ?? DefaultFilePath;

    public static long Load(string filePath)
    {
        var positions = Read();
        return positions.TryGetValue(filePath, out var point) ? point.TimeMs : 0;
    }

    public static IReadOnlyList<ResumePoint> Unfinished()
    {
        return Read()
            .Where(pair => pair.Value.TimeMs >= ResumeAfterMs && IsAvailable(pair.Key))
            .Select(pair => new ResumePoint(pair.Key, pair.Value.TimeMs, pair.Value.DurationMs, pair.Value.Title, pair.Value.Seen, pair.Value.Thumbnail))
            .OrderByDescending(point => point.Seen)
            .ThenBy(point => point.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void Save(string filePath, long timeMs, long durationMs, string? title = null, string? thumbnail = null)
    {
        var positions = Read();
        if (timeMs < ResumeAfterMs || durationMs <= 0 || timeMs >= durationMs - FinishedTailMs)
        {
            if (positions.Remove(filePath))
            {
                Write(positions);
            }

            return;
        }

        positions.TryGetValue(filePath, out var previous);
        var name = Clean(title) ?? previous.Title;
        var image = CleanUrl(thumbnail) ?? previous.Thumbnail;
        positions[filePath] = new StoredPoint(timeMs, durationMs, name, DateTimeOffset.Now, image);
        Write(positions);
    }

    public static void Move(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath)
            || string.IsNullOrWhiteSpace(newPath)
            || string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var positions = Read();
        if (!positions.Remove(oldPath, out var point))
        {
            return;
        }

        positions[newPath] = point;
        Write(positions);
    }

    private static bool IsAvailable(string key)
    {
        if (key.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || key.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(key, UriKind.Absolute, out var page) && page.Scheme is "http" or "https" && page.Host.Length > 0;
        }

        return File.Exists(key);
    }

    private static Dictionary<string, StoredPoint> Read()
    {
        var positions = new Dictionary<string, StoredPoint>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(FilePath))
            {
                return positions;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return positions;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (TryRead(property.Value, out var point))
                {
                    positions[property.Name] = point;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        return positions;
    }

    private static bool TryRead(JsonElement value, out StoredPoint point)
    {
        point = default;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var time) && time > 0)
        {
            point = new StoredPoint(time, 0, null, DateTimeOffset.MinValue, null);
            return true;
        }

        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("time", out var timeElement)
            || !timeElement.TryGetInt64(out var stored)
            || stored <= 0)
        {
            return false;
        }

        var duration = value.TryGetProperty("duration", out var durationElement) && durationElement.TryGetInt64(out var length) ? length : 0;
        var title = value.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
            ? Clean(titleElement.GetString())
            : null;
        var seen = DateTimeOffset.MinValue;
        if (value.TryGetProperty("seen", out var seenElement)
            && seenElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(seenElement.GetString(), out var parsed))
        {
            seen = parsed;
        }

        var thumbnail = value.TryGetProperty("thumbnail", out var thumbnailElement) && thumbnailElement.ValueKind == JsonValueKind.String
            ? CleanUrl(thumbnailElement.GetString())
            : null;
        point = new StoredPoint(stored, duration, title, seen, thumbnail);
        return true;
    }

    private static void Write(Dictionary<string, StoredPoint> positions)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var stored = positions.ToDictionary(
                pair => pair.Key,
                pair => new StoredPosition
                {
                    Time = pair.Value.TimeMs,
                    Duration = pair.Value.DurationMs > 0 ? pair.Value.DurationMs : null,
                    Title = pair.Value.Title,
                    Thumbnail = pair.Value.Thumbnail,
                    Seen = pair.Value.Seen == DateTimeOffset.MinValue ? null : pair.Value.Seen.ToString("O")
                },
                StringComparer.Ordinal);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(stored, JsonOptions));
        }
        catch (IOException)
        {
        }
    }

    private static string? Clean(string? title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return trimmed.Length <= 120 ? trimmed : trimmed[..120].Trim();
    }

    private static string? CleanUrl(string? thumbnail)
    {
        var trimmed = thumbnail?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Length > 2_000
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private readonly record struct StoredPoint(long TimeMs, long DurationMs, string? Title, DateTimeOffset Seen, string? Thumbnail);

    private sealed class StoredPosition
    {
        [JsonPropertyName("time")]
        public long Time { get; set; }

        [JsonPropertyName("duration")]
        public long? Duration { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; set; }

        [JsonPropertyName("seen")]
        public string? Seen { get; set; }
    }
}
