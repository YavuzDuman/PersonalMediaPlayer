using System.Text.Json;

namespace PersonalMediaPlayer.App.Playback;

internal sealed record VideoChapter(string Title, long StartMs);

internal readonly record struct ChapterListChoice(bool ShowButton, bool ShowList);

/// <summary>
/// Chapter names and start times for the video that is open. Nothing here is written to disk.
/// Bookmarks stay in their own file.
/// </summary>
internal static class VideoChapters
{
    public const long MaxStartMs = 24L * 60 * 60 * 1000;

    /// <summary>
    /// The chapter list starts open. This choice lasts while the app is open and is not saved.
    /// </summary>
    public static bool ListOpen { get; set; } = true;

    public static ChapterListChoice Choose(bool listOpen, bool mini, bool hasSession, int count)
    {
        var showButton = !mini && hasSession && count > 0;
        return new ChapterListChoice(showButton, showButton && listOpen);
    }

    public static IReadOnlyList<VideoChapter> FromLookup(JsonElement root)
    {
        if (!root.TryGetProperty("chapters", out var chapters) || chapters.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var raw = new List<(long StartMs, string? Title, int Order)>();
        var order = 0;
        foreach (var item in chapters.EnumerateArray())
        {
            var index = order++;
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("start_time", out var start)
                || !start.TryGetDouble(out var seconds)
                || !TryMilliseconds(seconds, out var startMs))
            {
                continue;
            }

            var title = item.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString()
                : null;
            raw.Add((startMs, title, index));
        }

        return Clean(raw);
    }

    public static IReadOnlyList<VideoChapter> FromOffsets(IEnumerable<(long StartMs, string? Title)> chapters)
    {
        var raw = new List<(long StartMs, string? Title, int Order)>();
        var order = 0;
        foreach (var chapter in chapters)
        {
            var index = order++;
            if (chapter.StartMs < 0 || chapter.StartMs > MaxStartMs)
            {
                continue;
            }

            raw.Add((chapter.StartMs, chapter.Title, index));
        }

        return Clean(raw);
    }

    public static int Current(IReadOnlyList<VideoChapter> chapters, long timeMs)
    {
        var index = -1;
        for (var i = 0; i < chapters.Count; i++)
        {
            if (chapters[i].StartMs > timeMs)
            {
                break;
            }

            index = i;
        }

        return index;
    }

    public static bool Same(IReadOnlyList<VideoChapter> left, IReadOnlyList<VideoChapter> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].StartMs != right[i].StartMs || !string.Equals(left[i].Title, right[i].Title, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static string Format(long startMs)
    {
        if (startMs < 0)
        {
            startMs = 0;
        }

        var totalSeconds = startMs / 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }

    private static bool TryMilliseconds(double seconds, out long startMs)
    {
        startMs = 0;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            return false;
        }

        var ms = seconds * 1000d;
        if (ms > MaxStartMs)
        {
            return false;
        }

        startMs = (long)Math.Round(ms, MidpointRounding.AwayFromZero);
        return true;
    }

    private static IReadOnlyList<VideoChapter> Clean(List<(long StartMs, string? Title, int Order)> raw)
    {
        raw.Sort(static (left, right) =>
        {
            var byTime = left.StartMs.CompareTo(right.StartMs);
            return byTime != 0 ? byTime : left.Order.CompareTo(right.Order);
        });

        var kept = new List<(long StartMs, string? Title)>();
        foreach (var item in raw)
        {
            if (kept.Count > 0 && kept[^1].StartMs == item.StartMs)
            {
                if (string.IsNullOrWhiteSpace(kept[^1].Title) && !string.IsNullOrWhiteSpace(item.Title))
                {
                    kept[^1] = (item.StartMs, item.Title);
                }

                continue;
            }

            kept.Add((item.StartMs, item.Title));
        }

        // One unnamed chapter at the start is what LibVLC reports when a file has no chapters.
        if (kept.Count == 1 && kept[0].StartMs == 0 && string.IsNullOrWhiteSpace(kept[0].Title))
        {
            return [];
        }

        var chapters = new VideoChapter[kept.Count];
        for (var i = 0; i < kept.Count; i++)
        {
            chapters[i] = new VideoChapter(CleanTitle(kept[i].Title, i + 1), kept[i].StartMs);
        }

        return chapters;
    }

    private static string CleanTitle(string? title, int number)
    {
        var name = string.IsNullOrWhiteSpace(title) ? $"Chapter {number}" : title.Trim();
        name = name.Replace('\r', ' ').Replace('\n', ' ');
        while (name.Contains("  ", StringComparison.Ordinal))
        {
            name = name.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (name.Length > 120)
        {
            name = name[..120].Trim();
        }

        return string.IsNullOrWhiteSpace(name) ? $"Chapter {number}" : name;
    }
}
