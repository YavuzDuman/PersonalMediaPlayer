using System.Text.Json;

namespace PersonalMediaPlayer.App.Capture;

internal static class ScreenshotTextIndex
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "screenshot-text.json");

    public static bool Matches(string filePath, string term)
    {
        var all = Read();
        return all.TryGetValue(filePath, out var stored)
            && TextOf(stored).Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCurrent(string filePath)
    {
        if (!File.Exists(filePath) || !Read().TryGetValue(filePath, out var stored))
        {
            return false;
        }

        return TryReadStamp(stored, out var ticks)
            && ticks == File.GetLastWriteTimeUtc(filePath).Ticks;
    }

    public static async Task StoreAsync(string filePath, byte[] pngBytes)
    {
        try
        {
            var text = await ScreenshotOcr.ReadAsync(pngBytes);
            Remember(filePath, text);
        }
        catch (InvalidOperationException)
        {
            // No OCR language is installed. Leave the file unindexed so a later search can try again.
        }
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
        if (!all.TryGetValue(oldPath, out var text))
        {
            return;
        }

        all.Remove(oldPath);
        all[newPath] = text;
        Write(all);
    }

    public static void Remove(string filePath)
    {
        var all = Read();
        if (!all.Remove(filePath))
        {
            return;
        }

        Write(all);
    }

    private static void Remember(string filePath, string text)
    {
        var all = Read();
        var ticks = File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath).Ticks : 0;
        all[filePath] = "2|" + ticks + "|" + text;
        Write(all);
    }

    private static string TextOf(string stored)
        => TryReadStamp(stored, out _) ? stored[(stored.LastIndexOf('|') + 1)..] : stored;

    private static bool TryReadStamp(string stored, out long ticks)
    {
        var parts = stored.Split('|', 3);
        if (parts.Length == 3 && parts[0] == "2" && long.TryParse(parts[1], out ticks))
        {
            return true;
        }

        ticks = 0;
        return false;
    }

    private static Dictionary<string, string> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Write(Dictionary<string, string> text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(text));
        }
        catch
        {
            // A missed text index should not stop saving or searching.
        }
    }
}
