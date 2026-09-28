using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PersonalMediaPlayer.App.Subtitles;

internal readonly record struct SubtitleCue(long StartMs, long EndMs, string Text);

internal static class SubtitleCues
{
    public static IReadOnlyList<SubtitleCue> LoadFor(string mediaPath)
    {
        var beside = FindBeside(mediaPath);
        if (beside is not null)
        {
            return Parse(File.ReadAllText(beside));
        }

        var extracted = Extract(mediaPath);
        return extracted is null ? [] : Parse(extracted);
    }

    private static string? FindBeside(string mediaPath)
    {
        var directory = Path.GetDirectoryName(mediaPath);
        var stem = Path.GetFileNameWithoutExtension(mediaPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, stem + "*.srt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static string? Extract(string mediaPath)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null || !File.Exists(mediaPath))
        {
            return null;
        }

        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "subtitle-cache");
        Directory.CreateDirectory(cache);
        var info = new FileInfo(mediaPath);
        var target = Path.Combine(cache, $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}.srt");
        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            return File.ReadAllText(target);
        }

        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(mediaPath);
        start.ArgumentList.Add("-map");
        start.ArgumentList.Add("0:s:0");
        start.ArgumentList.Add(target);
        using var process = Process.Start(start);
        if (process is null)
        {
            return null;
        }

        process.WaitForExit(15000);
        return File.Exists(target) && new FileInfo(target).Length > 0 ? File.ReadAllText(target) : null;
    }

    public static IReadOnlyList<SubtitleCue> Parse(string srt)
    {
        var cues = new List<SubtitleCue>();
        var blocks = Regex.Split(srt.Replace("\r\n", "\n").Trim(), @"\n\s*\n");
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var timeLine = lines.FirstOrDefault(line => line.Contains("-->", StringComparison.Ordinal));
            if (timeLine is null)
            {
                continue;
            }

            var parts = timeLine.Split("-->", StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !TryTime(parts[0], out var start) || !TryTime(parts[1], out var end))
            {
                continue;
            }

            var text = string.Join(" ", lines.SkipWhile(line => !line.Contains("-->", StringComparison.Ordinal)).Skip(1));
            text = Regex.Replace(text, "<[^>]+>", string.Empty).Trim();
            if (text.Length > 0 && end > start)
            {
                cues.Add(new SubtitleCue(start, end, text));
            }
        }

        return cues;
    }

    private static bool TryTime(string text, out long milliseconds)
    {
        milliseconds = 0;
        var clock = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        var match = Regex.Match(clock, @"(\d+):(\d+):(\d+)[,.](\d+)");
        if (!match.Success)
        {
            return false;
        }

        milliseconds = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3_600_000L
            + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60_000L
            + int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) * 1_000L
            + int.Parse(match.Groups[4].Value.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        return true;
    }

    private static string? FindFfmpeg()
    {
        var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "tools", "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : null;
    }
}
