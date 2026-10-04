using System.Text.Json;

namespace PersonalMediaPlayer.App.Playback;

internal readonly record struct PlaybackVolumeState(double Level, double Audible);

internal static class PlaybackVolume
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "playback-volume.json");

    // The download preview is a different player. Its level stays in memory for this
    // session and must not read or write the watching player's file.
    private static double _previewLevel = 80;
    private static double _previewAudible = 80;

    public static PlaybackVolumeState Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new PlaybackVolumeState(80, 80);
            }

            var saved = JsonSerializer.Deserialize<SavedVolume>(File.ReadAllText(FilePath));
            if (saved is null)
            {
                return new PlaybackVolumeState(80, 80);
            }

            var level = Clamp(saved.Level, 80);
            var audible = Clamp(saved.Audible, 80);
            if (audible <= 0)
            {
                audible = 80;
            }

            return new PlaybackVolumeState(level, audible);
        }
        catch
        {
            return new PlaybackVolumeState(80, 80);
        }
    }

    public static void Save(double level)
    {
        var audible = level > 0 ? Clamp(level, 80) : Load().Audible;
        if (audible <= 0)
        {
            audible = 80;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new SavedVolume(Clamp(level, 0), audible)));
        }
        catch
        {
            // A missed volume should not stop playback.
        }
    }

    public static PlaybackVolumeState LoadPreview()
    {
        var audible = Clamp(_previewAudible, 80);
        if (audible <= 0)
        {
            audible = 80;
        }

        return new PlaybackVolumeState(Clamp(_previewLevel, 80), audible);
    }

    public static void SavePreview(double level)
    {
        var audible = level > 0 ? Clamp(level, 80) : _previewAudible;
        if (audible <= 0)
        {
            audible = 80;
        }

        _previewLevel = Clamp(level, 0);
        _previewAudible = audible;
    }

    private static double Clamp(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(Math.Round(value), 0, 100);
    }

    private sealed record SavedVolume(double Level, double Audible);
}
