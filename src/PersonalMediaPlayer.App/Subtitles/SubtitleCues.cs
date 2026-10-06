using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalMediaPlayer.App.Subtitles;

internal readonly record struct SubtitleCue(long StartMs, long EndMs, string Text);

internal static class SubtitleCues
{
    internal static string? CacheDirectoryOverride { get; set; }

    public static IReadOnlyList<SubtitleCue> LoadFor(string mediaPath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return [];
        }

        var beside = FindBeside(mediaPath);
        if (beside is not null)
        {
            return Parse(File.ReadAllText(beside));
        }

        var extracted = Extract(mediaPath, cancellationToken);
        return extracted is null ? [] : Parse(extracted);
    }

    internal static string CacheFile(string mediaPath)
    {
        var info = new FileInfo(mediaPath);
        return Path.Combine(CacheDirectory(), $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}.srt");
    }

    private static string CacheDirectory()
        => CacheDirectoryOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "subtitle-cache");

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

    private static string? Extract(string mediaPath, CancellationToken cancellationToken)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null || !File.Exists(mediaPath) || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        Directory.CreateDirectory(CacheDirectory());
        var target = CacheFile(mediaPath);
        var miss = target + ".none";
        if (File.Exists(miss))
        {
            return null;
        }

        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            return File.ReadAllText(target);
        }

        var errors = new StringBuilder();
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

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                errors.AppendLine(args.Data);
            }
        };
        try
        {
            process.BeginErrorReadLine();
        }
        catch (InvalidOperationException)
        {
            // The process can exit before the error reader is attached.
        }

        var started = Environment.TickCount64;
        while (!process.WaitForExit(200))
        {
            if (!cancellationToken.IsCancellationRequested && Environment.TickCount64 - started < 15_000)
            {
                continue;
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // The process can already have exited.
            }

            process.WaitForExit(2_000);
            DeleteQuiet(target);
            return null;
        }

        process.WaitForExit();
        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            return File.ReadAllText(target);
        }

        DeleteQuiet(target);
        if (HasNoSubtitle(errors.ToString()))
        {
            try
            {
                File.WriteAllBytes(miss, []);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static bool HasNoSubtitle(string errors)
        => errors.Contains("matches no streams", StringComparison.OrdinalIgnoreCase)
            || errors.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase);

    private static void DeleteQuiet(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
        var match = Regex.Match(clock, @"^(?:(\d+):)?(\d+):(\d+)[,.](\d+)");
        if (!match.Success)
        {
            return false;
        }

        var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var fraction = match.Groups[4].Value.PadRight(3, '0');
        milliseconds = hours * 3_600_000L
            + minutes * 60_000L
            + seconds * 1_000L
            + int.Parse(fraction[..3], CultureInfo.InvariantCulture);
        return true;
    }

    private static string? FindFfmpeg()
    {
        var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "tools", "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : null;
    }
}
