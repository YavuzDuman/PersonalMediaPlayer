using System.Text.Json;

namespace PersonalMediaPlayer.App.Playback;

internal static class PlaybackBookmarks
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "playback-bookmarks.json");

    public static IReadOnlyList<Bookmark> Load(string filePath)
    {
        var all = Read();
        return all.TryGetValue(filePath, out var marks)
            ? marks.OrderBy(mark => mark.TimeMs).ToArray()
            : [];
    }

    public static bool Add(string filePath, long timeMs, string? note)
    {
        var all = Read();
        if (!all.TryGetValue(filePath, out var marks))
        {
            marks = [];
            all[filePath] = marks;
        }

        if (marks.Any(mark => Math.Abs(mark.TimeMs - timeMs) < 1000))
        {
            return false;
        }

        marks.Add(new Bookmark { TimeMs = timeMs, Note = Clean(note) });
        marks.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));
        Write(all);
        return true;
    }

    public static void UpdateNote(string filePath, long timeMs, string? note)
    {
        var all = Read();
        if (!all.TryGetValue(filePath, out var marks))
        {
            return;
        }

        var mark = marks.FirstOrDefault(item => item.TimeMs == timeMs);
        if (mark is null)
        {
            return;
        }

        mark.Note = Clean(note);
        Write(all);
    }

    public static void Remap(string filePath, IReadOnlyList<(long StartMs, long EndMs)> removed, double rate)
    {
        if (string.IsNullOrWhiteSpace(filePath) || rate <= 0)
        {
            return;
        }

        var all = Read();
        if (!all.TryGetValue(filePath, out var marks) || marks.Count == 0)
        {
            return;
        }

        var cuts = removed.Where(cut => cut.EndMs > cut.StartMs).OrderBy(cut => cut.StartMs).ToList();
        var kept = new List<Bookmark>();
        foreach (var mark in marks)
        {
            if (cuts.Any(cut => mark.TimeMs >= cut.StartMs && mark.TimeMs < cut.EndMs))
            {
                continue;
            }

            var shift = cuts.Where(cut => cut.EndMs <= mark.TimeMs).Sum(cut => cut.EndMs - cut.StartMs);
            mark.TimeMs = Math.Max(0, (long)((mark.TimeMs - shift) / rate));
            kept.Add(mark);
        }

        kept.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));
        all[filePath] = kept;
        Write(all);
    }

    public static void Scale(string filePath, double rate)
    {
        if (string.IsNullOrWhiteSpace(filePath) || rate <= 0 || Math.Abs(rate - 1d) < 0.001)
        {
            return;
        }

        var all = Read();
        if (!all.TryGetValue(filePath, out var marks) || marks.Count == 0)
        {
            return;
        }

        foreach (var mark in marks)
        {
            mark.TimeMs = Math.Max(0, (long)(mark.TimeMs / rate));
        }

        marks.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));
        Write(all);
    }

    public static void Move(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath)
            || string.IsNullOrWhiteSpace(newPath)
            || string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var all = Read();
        if (!all.TryGetValue(oldPath, out var marks))
        {
            return;
        }

        all.Remove(oldPath);
        if (!all.TryGetValue(newPath, out var existing))
        {
            all[newPath] = marks;
        }
        else
        {
            foreach (var mark in marks)
            {
                if (existing.All(saved => Math.Abs(saved.TimeMs - mark.TimeMs) >= 1000))
                {
                    existing.Add(mark);
                }
            }

            existing.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));
        }

        Write(all);
    }

    public static void Remove(string filePath, long timeMs)
    {
        var all = Read();
        if (!all.TryGetValue(filePath, out var marks))
        {
            return;
        }

        marks.RemoveAll(mark => mark.TimeMs == timeMs);
        if (marks.Count == 0)
        {
            all.Remove(filePath);
        }

        Write(all);
    }

    private static string Clean(string? note) => (note ?? string.Empty).Trim();

    private static Dictionary<string, List<Bookmark>> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new Dictionary<string, List<Bookmark>>(StringComparer.OrdinalIgnoreCase);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            var bookmarks = new Dictionary<string, List<Bookmark>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in document.RootElement.EnumerateObject())
            {
                var marks = new List<Bookmark>();
                foreach (var item in file.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var time))
                    {
                        marks.Add(new Bookmark { TimeMs = time });
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        marks.Add(item.Deserialize<Bookmark>() ?? new Bookmark());
                    }
                }

                bookmarks[file.Name] = marks;
            }

            return bookmarks;
        }
        catch
        {
            return new Dictionary<string, List<Bookmark>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Write(Dictionary<string, List<Bookmark>> bookmarks)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(bookmarks));
        }
        catch
        {
            // A missed bookmark should not stop playback.
        }
    }
}

internal sealed class Bookmark
{
    public long TimeMs { get; set; }

    public string Note { get; set; } = string.Empty;
}
