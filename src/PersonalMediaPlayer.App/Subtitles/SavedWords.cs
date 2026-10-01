using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalMediaPlayer.App.Subtitles;

internal static class SavedWords
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "saved-words.json");

    internal static string? StoreOverride { get; set; }

    private static string FilePath => StoreOverride ?? DefaultFilePath;

    public static IReadOnlyList<SavedWord> All()
    {
        return Read()
            .OrderByDescending(item => DateTimeOffset.TryParse(item.SavedAt, out var saved) ? saved : DateTimeOffset.MinValue)
            .ToArray();
    }

    public static bool Contains(string english, string sentence, string? videoPath, long? timeMs, string? pageUrl = null)
    {
        var path = CleanPath(videoPath);
        var page = path is null ? CleanPath(pageUrl) : null;
        var time = path is null && page is null ? null : timeMs;
        return Read().Any(item => Same(item, english, sentence, path, time, page));
    }

    public static void Add(string english, string turkish, string sentence, string? videoPath, long? timeMs, string? pageUrl = null, bool resolvePage = false, string? sourceName = null, string? audioLanguage = null, string? captionLanguage = null)
    {
        var path = CleanPath(videoPath);
        var page = path is null ? CleanPath(pageUrl) : null;
        var time = path is null && page is null ? null : timeMs;
        var audio = CleanPath(audioLanguage);
        var caption = CleanPath(captionLanguage);
        var words = Read();
        var existing = words.FirstOrDefault(item => Same(item, english, sentence, path, time, page));
        if (existing is not null)
        {
            var changed = false;
            if (audio is not null && !string.Equals(existing.AudioLanguage, audio, StringComparison.OrdinalIgnoreCase))
            {
                existing.AudioLanguage = audio;
                changed = true;
            }

            if (caption is not null && !string.Equals(existing.CaptionLanguage, caption, StringComparison.OrdinalIgnoreCase))
            {
                existing.CaptionLanguage = caption;
                changed = true;
            }

            if (changed)
            {
                Write(words);
            }

            return;
        }

        if (path is not null || page is not null)
        {
            var earlier = words.FirstOrDefault(item => Same(item, english, sentence, null, null, null));
            if (earlier is not null)
            {
                earlier.VideoPath = path;
                earlier.PageUrl = page;
                earlier.ResolvePage = page is not null && resolvePage;
                earlier.SourceName = CleanPath(sourceName);
                earlier.AudioLanguage = audio;
                earlier.CaptionLanguage = caption;
                earlier.TimeMs = time;
                if (!string.IsNullOrWhiteSpace(turkish))
                {
                    earlier.Turkish = turkish.Trim();
                }

                Write(words);
                return;
            }
        }

        words.Add(new SavedWord
        {
            English = english.Trim(),
            Turkish = turkish.Trim(),
            Sentence = sentence.Trim(),
            VideoPath = path,
            PageUrl = page,
            ResolvePage = page is not null && resolvePage,
            SourceName = CleanPath(sourceName),
            AudioLanguage = audio,
            CaptionLanguage = caption,
            TimeMs = time,
            SavedAt = DateTimeOffset.Now.ToString("O")
        });
        Write(words);
    }

    public static void Remove(SavedWord word)
    {
        var words = Read();
        if (words.RemoveAll(item => Same(item, word.English, word.Sentence, CleanPath(word.VideoPath), word.TimeMs, CleanPath(word.PageUrl))) == 0)
        {
            return;
        }

        Write(words);
    }

    public static void Move(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath)
            || string.IsNullOrWhiteSpace(newPath)
            || string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var words = Read();
        var changed = false;
        foreach (var word in words)
        {
            if (!string.Equals(word.VideoPath, oldPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            word.VideoPath = newPath;
            changed = true;
        }

        if (changed)
        {
            Write(words);
        }
    }

    internal static string SourceTitle(SavedWord word)
    {
        if (!string.IsNullOrWhiteSpace(word.PageUrl))
        {
            if (!string.IsNullOrWhiteSpace(word.SourceName))
            {
                return word.SourceName.Trim();
            }

            return Uri.TryCreate(word.PageUrl, UriKind.Absolute, out var page) && page.Host.Length > 0
                ? page.Host
                : word.PageUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(word.VideoPath))
        {
            return "No video";
        }

        var name = Path.GetFileName(word.VideoPath);
        return File.Exists(word.VideoPath) ? name : $"{name} · not on this PC";
    }

    internal static bool CanOpen(SavedWord word)
    {
        if (word.TimeMs is not long)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(word.PageUrl))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(word.VideoPath) && File.Exists(word.VideoPath);
    }

    private static bool Same(SavedWord item, string english, string sentence, string? videoPath, long? timeMs, string? pageUrl)
    {
        return string.Equals(item.English, english.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Sentence, sentence.Trim(), StringComparison.Ordinal)
            && string.Equals(CleanPath(item.VideoPath), CleanPath(videoPath), StringComparison.OrdinalIgnoreCase)
            && string.Equals(CleanPath(item.PageUrl), CleanPath(pageUrl), StringComparison.OrdinalIgnoreCase)
            && item.TimeMs == timeMs;
    }

    private static string? CleanPath(string? path)
    {
        var trimmed = path?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static List<SavedWord> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<SavedWord>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void Write(List<SavedWord> words)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(words, JsonOptions));
    }
}

internal sealed class SavedWord
{
    public string English { get; set; } = string.Empty;

    public string Turkish { get; set; } = string.Empty;

    public string Sentence { get; set; } = string.Empty;

    public string? VideoPath { get; set; }

    public string? PageUrl { get; set; }

    public bool ResolvePage { get; set; }

    public string? SourceName { get; set; }

    public string? AudioLanguage { get; set; }

    public string? CaptionLanguage { get; set; }

    public long? TimeMs { get; set; }

    public string SavedAt { get; set; } = string.Empty;
}
