using System.Text.Json;

namespace PersonalMediaPlayer.App.Playback;

internal static class PlaybackProgress
{
    private const long ResumeAfterMs = 5_000;
    private const long FinishedTailMs = 10_000;
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "playback-positions.json");

    public static long Load(string filePath)
    {
        var positions = Read();
        return positions.TryGetValue(filePath, out var time) ? time : 0;
    }

    public static void Save(string filePath, long timeMs, long durationMs)
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

        positions[filePath] = timeMs;
        Write(positions);
    }

    private static Dictionary<string, long> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            }

            return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(FilePath))
                ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Write(Dictionary<string, long> positions)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(positions));
        }
        catch
        {
            // A missed resume point should not stop playback.
        }
    }
}
