using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PersonalMediaPlayer.App.Subtitles;

internal static class SubtitleEdit
{
    public static IReadOnlyList<SubtitleCue> Keep(IReadOnlyList<SubtitleCue> cues, IReadOnlyList<(long StartMs, long EndMs)> kept, double speed = 1)
    {
        if (cues.Count == 0 || kept.Count == 0)
        {
            return [];
        }

        var rate = speed is >= 0.25 and <= 4 ? speed : 1;
        var output = new List<SubtitleCue>();
        long cursor = 0;
        foreach (var span in kept)
        {
            var spanStart = Math.Max(0, span.StartMs);
            var spanEnd = span.EndMs;
            if (spanEnd <= spanStart)
            {
                continue;
            }

            foreach (var cue in cues)
            {
                var start = Math.Max(cue.StartMs, spanStart);
                var end = Math.Min(cue.EndMs, spanEnd);
                if (end <= start)
                {
                    continue;
                }

                output.Add(new SubtitleCue(
                    Scale(cursor + start - spanStart, rate),
                    Scale(cursor + end - spanStart, rate),
                    cue.Text));
            }

            cursor += spanEnd - spanStart;
        }

        output.Sort((left, right) => left.StartMs.CompareTo(right.StartMs));
        return output;
    }

    public static IReadOnlyList<SubtitleCue> Scale(IReadOnlyList<SubtitleCue> cues, double speed)
    {
        if (cues.Count == 0 || speed is < 0.25 or > 4 || Math.Abs(speed - 1) < 0.001)
        {
            return cues;
        }

        return cues
            .Select(cue => new SubtitleCue(Scale(cue.StartMs, speed), Scale(cue.EndMs, speed), cue.Text))
            .Where(cue => cue.EndMs > cue.StartMs)
            .ToArray();
    }

    public static IReadOnlyList<SubtitleCue> Join(IReadOnlyList<(IReadOnlyList<SubtitleCue> Cues, long StartMs, long EndMs)> clips)
    {
        var output = new List<SubtitleCue>();
        long cursor = 0;
        foreach (var clip in clips)
        {
            var length = Math.Max(0, clip.EndMs - Math.Max(0, clip.StartMs));
            foreach (var cue in Keep(clip.Cues, [(Math.Max(0, clip.StartMs), Math.Max(0, clip.EndMs))]))
            {
                output.Add(new SubtitleCue(cursor + cue.StartMs, cursor + cue.EndMs, cue.Text));
            }

            cursor += length;
        }

        return output;
    }

    public static string SuggestedName(string? title)
    {
        var source = string.IsNullOrWhiteSpace(title) ? "Captions" : title.Trim();
        var slash = Math.Max(source.LastIndexOf('\\'), source.LastIndexOf('/'));
        if (slash >= 0 && slash < source.Length - 1)
        {
            source = source[(slash + 1)..];
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(source.Select(character => invalid.Contains(character) ? ' ' : character).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        foreach (var extension in new[] { ".mp4", ".mkv", ".mov", ".webm", ".m4v", ".avi", ".wmv", ".m4a", ".srt" })
        {
            if (cleaned.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^extension.Length].Trim();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "Captions";
        }

        return cleaned.Length > 120 ? cleaned[..120].Trim() : cleaned;
    }

    public static string ToSrt(IReadOnlyList<SubtitleCue> cues)
    {
        var text = new StringBuilder();
        var index = 1;
        foreach (var cue in cues)
        {
            if (cue.EndMs <= cue.StartMs || string.IsNullOrWhiteSpace(cue.Text))
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append("\r\n\r\n");
            }

            text.Append(index.ToString(CultureInfo.InvariantCulture));
            text.Append("\r\n");
            text.Append(Clock(cue.StartMs));
            text.Append(" --> ");
            text.Append(Clock(cue.EndMs));
            text.Append("\r\n");
            text.Append(cue.Text.Trim());
            index++;
        }

        return text.ToString();
    }

    public static Task CarryAsync(string sourcePath, string destinationPath, IReadOnlyList<(long StartMs, long EndMs)>? kept = null, double speed = 1)
        => Task.Run(() =>
        {
            try
            {
                var cues = SubtitleCues.LoadFor(sourcePath);
                var mapped = kept is { Count: > 0 } ? Keep(cues, kept, speed) : Scale(cues, speed);
                Attach(destinationPath, mapped);
            }
            catch (Exception)
            {
                // The edited picture is already saved. A missed caption should not undo it.
            }
        });

    public static Task CarryJoinAsync(string destinationPath, IReadOnlyList<(string Path, long StartMs, long EndMs)> clips)
        => Task.Run(() =>
        {
            try
            {
                var parts = clips
                    .Select(clip => (SubtitleCues.LoadFor(clip.Path), clip.StartMs, clip.EndMs))
                    .ToArray();
                Attach(destinationPath, Join(parts));
            }
            catch (Exception)
            {
            }
        });

    private static void Attach(string videoPath, IReadOnlyList<SubtitleCue> cues)
    {
        if (cues.Count == 0 || !File.Exists(videoPath))
        {
            return;
        }

        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "pmp-subs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var srt = Path.Combine(folder, "captions.srt");
        var muxed = Path.Combine(folder, "video.mp4");
        try
        {
            File.WriteAllText(srt, ToSrt(cues), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var start = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-y");
            start.ArgumentList.Add("-hide_banner");
            start.ArgumentList.Add("-loglevel");
            start.ArgumentList.Add("error");
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(videoPath);
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(srt);
            start.ArgumentList.Add("-map");
            start.ArgumentList.Add("0:v:0");
            start.ArgumentList.Add("-map");
            start.ArgumentList.Add("0:a?");
            start.ArgumentList.Add("-map");
            start.ArgumentList.Add("1:0");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("copy");
            start.ArgumentList.Add("-c:s");
            start.ArgumentList.Add("mov_text");
            start.ArgumentList.Add(muxed);
            using var process = Process.Start(start);
            if (process is null)
            {
                return;
            }

            process.WaitForExit(20_000);
            if (process.ExitCode != 0 || !File.Exists(muxed) || new FileInfo(muxed).Length < 1024)
            {
                return;
            }

            File.Copy(muxed, videoPath, overwrite: true);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static long Scale(long milliseconds, double speed)
        => Math.Abs(speed - 1) < 0.001 ? Math.Max(0, milliseconds) : Math.Max(0, (long)Math.Round(milliseconds / speed));

    private static string Clock(long milliseconds)
    {
        if (milliseconds < 0)
        {
            milliseconds = 0;
        }

        var hours = milliseconds / 3_600_000;
        milliseconds %= 3_600_000;
        var minutes = milliseconds / 60_000;
        milliseconds %= 60_000;
        var seconds = milliseconds / 1_000;
        var millis = milliseconds % 1_000;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00},{millis:000}");
    }

    private static string? FindFfmpeg()
    {
        var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "tools", "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : null;
    }
}
