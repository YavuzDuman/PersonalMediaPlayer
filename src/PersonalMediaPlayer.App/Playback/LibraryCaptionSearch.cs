using PersonalMediaPlayer.App.Subtitles;
using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.Playback;

internal sealed record CaptionVideo(string Title, string FilePath, IReadOnlyList<SubtitleCue> Cues);

internal sealed record LibraryCaptionHit(string Title, string FilePath, long StartMs, string Line);

internal sealed record LibraryCaptionMatch(IReadOnlyList<LibraryCaptionHit> Hits, bool Truncated);

internal static class LibraryCaptionSearch
{
    internal const int MaxHits = 80;

    internal const int MaxPerVideo = 8;

    private static readonly LibraryCaptionMatch Empty = new([], false);

    private static readonly Dictionary<string, Held> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    private readonly record struct Held(ReadyStamp Stamp, IReadOnlyList<SubtitleCue> Cues);

    public static LibraryCaptionMatch Find(string? query, IEnumerable<CaptionVideo> videos)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Empty;
        }

        var ranked = videos
            .Where(video => video.Cues.Count > 0 && !string.IsNullOrWhiteSpace(video.FilePath))
            .OrderBy(DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(video => video.FilePath, StringComparer.OrdinalIgnoreCase);

        var hits = new List<LibraryCaptionHit>();
        var truncated = false;
        foreach (var video in ranked)
        {
            var found = SubtitleSearch.Find(video.Cues, query);
            if (found.Count == 0)
            {
                continue;
            }

            var room = MaxHits - hits.Count;
            if (room <= 0)
            {
                truncated = true;
                break;
            }

            var take = Math.Min(found.Count, Math.Min(MaxPerVideo, room));
            if (take < found.Count)
            {
                truncated = true;
            }

            var title = DisplayTitle(video);
            for (var index = 0; index < take; index++)
            {
                var cue = found[index];
                hits.Add(new LibraryCaptionHit(title, video.FilePath, cue.StartMs, cue.Text));
            }
        }

        return new LibraryCaptionMatch(hits, truncated);
    }

    public static LibraryCaptionMatch Search(string? query, IEnumerable<MediaItem> items)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Empty;
        }

        var videos = new List<CaptionVideo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!item.IsVideo || string.IsNullOrWhiteSpace(item.FilePath) || item.IsMissing || !File.Exists(item.FilePath))
            {
                continue;
            }

            string key;
            try
            {
                key = Path.GetFullPath(item.FilePath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!seen.Add(key))
            {
                continue;
            }

            var cues = ReadyCues(key);
            if (cues.Count == 0)
            {
                continue;
            }

            videos.Add(new CaptionVideo(item.DisplayName, key, cues));
        }

        return Find(query, videos);
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            Cache.Clear();
        }
    }

    private static IReadOnlyList<SubtitleCue> ReadyCues(string fullPath)
    {
        if (!SubtitleCues.TryReady(fullPath, read: false, out var described))
        {
            return [];
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(fullPath, out var held) && held.Stamp == described.Stamp)
            {
                return held.Cues;
            }
        }

        if (!SubtitleCues.TryReady(fullPath, read: true, out var loaded))
        {
            return [];
        }

        var cues = string.IsNullOrEmpty(loaded.Text) ? Array.Empty<SubtitleCue>() : SubtitleCues.Parse(loaded.Text);
        lock (Gate)
        {
            Cache[fullPath] = new Held(loaded.Stamp, cues);
        }

        return cues;
    }

    private static string DisplayTitle(CaptionVideo video)
    {
        if (!string.IsNullOrWhiteSpace(video.Title))
        {
            return video.Title.Trim();
        }

        try
        {
            var name = Path.GetFileName(video.FilePath);
            return string.IsNullOrWhiteSpace(name) ? video.FilePath : name;
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            return video.FilePath;
        }
    }
}
