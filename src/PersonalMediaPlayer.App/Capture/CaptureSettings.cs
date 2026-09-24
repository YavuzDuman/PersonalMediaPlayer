namespace PersonalMediaPlayer.App.Capture;

internal static class CaptureSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "include-cursor.txt");

    public static bool LoadIncludeCursor()
    {
        try
        {
            return File.Exists(FilePath)
                && bool.TryParse(File.ReadAllText(FilePath).Trim(), out var value)
                && value;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveIncludeCursor(bool includeCursor)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, includeCursor ? "true" : "false");
    }

    private static readonly string SystemAudioPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "include-system-audio.txt");

    public static bool LoadIncludeSystemAudio()
    {
        try
        {
            return File.Exists(SystemAudioPath)
                && bool.TryParse(File.ReadAllText(SystemAudioPath).Trim(), out var value)
                && value;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveIncludeSystemAudio(bool includeSystemAudio)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SystemAudioPath)!);
        File.WriteAllText(SystemAudioPath, includeSystemAudio ? "true" : "false");
    }

    private static readonly string DelayPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "capture-delay.txt");

    public static readonly int[] DelayChoices = [0, 3, 5, 10];

    public static int LoadDelaySeconds()
    {
        try
        {
            if (!File.Exists(DelayPath))
            {
                return 0;
            }

            return int.TryParse(File.ReadAllText(DelayPath).Trim(), out var seconds) && DelayChoices.Contains(seconds)
                ? seconds
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static void SaveDelaySeconds(int seconds)
    {
        if (!DelayChoices.Contains(seconds))
        {
            seconds = 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DelayPath)!);
        File.WriteAllText(DelayPath, seconds.ToString());
    }

    public static int IndexFromSeconds(int seconds)
    {
        var index = Array.IndexOf(DelayChoices, seconds);
        return index < 0 ? 0 : index;
    }
}
