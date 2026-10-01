using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using PersonalMediaPlayer.App.Subtitles;

namespace PersonalMediaPlayer.App.Editing;

internal readonly record struct MergeSource(string Path, double DurationSeconds, bool HasAudio, int Width, int Height, double TrimStartSeconds = 0, double TrimEndSeconds = 0, double VideoFadeInSeconds = 0, double VideoFadeOutSeconds = 0, double AudioFadeInSeconds = 0, double AudioFadeOutSeconds = 0);

internal static class VideoMerger
{
    public static async Task<MergeSource> ProbeAsync(string path)
    {
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException("Merging needs ffmpeg, and it is not available.");
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The video could not be read.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var duration = ParseDuration(error);
        var size = ParseSize(error);
        if (duration <= 0 || size is null)
        {
            throw new InvalidOperationException(Path.GetFileName(path) + " has no picture.");
        }

        return new MergeSource(path, duration, error.Contains("Audio:", StringComparison.OrdinalIgnoreCase), size.Value.Width, size.Value.Height);
    }

    public static async Task MergeAsync(IReadOnlyList<MergeSource> clips, string destination, CancellationToken cancellationToken)
    {
        if (clips.Count < 2)
        {
            throw new InvalidOperationException("Add at least two videos.");
        }

        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException("Merging needs ffmpeg, and it is not available.");
        var (width, height) = TargetSize(clips);
        string? error = null;
        foreach (var encoder in new[] { "h264_mf", "h264_nvenc", "h264_qsv", "h264_amf", "libx264" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (ok, detail) = await RunAsync(ffmpeg, clips, destination, width, height, encoder, cancellationToken);
            if (ok)
            {
                await SubtitleEdit.CarryJoinAsync(destination, clips.Select(CaptionSpan).ToArray());
                return;
            }

            error = detail;
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "The videos could not be joined." : error);
    }

    private static (string Path, long StartMs, long EndMs) CaptionSpan(MergeSource clip)
    {
        var start = (long)Math.Round(Math.Max(0, clip.TrimStartSeconds) * 1000);
        var endSeconds = clip.TrimEndSeconds > clip.TrimStartSeconds
            ? Math.Min(clip.TrimEndSeconds, clip.DurationSeconds)
            : clip.DurationSeconds;
        var end = (long)Math.Round(Math.Max(endSeconds, clip.TrimStartSeconds) * 1000);
        return (clip.Path, start, Math.Max(start, end));
    }

    private static (int Width, int Height) TargetSize(IReadOnlyList<MergeSource> clips)
    {
        var width = clips.Max(clip => clip.Width);
        var height = clips.Max(clip => clip.Height);
        var longest = Math.Max(width, height);
        if (longest > 1920)
        {
            var scale = 1920d / longest;
            width = (int)Math.Round(width * scale);
            height = (int)Math.Round(height * scale);
        }

        width = Math.Max(2, width + width % 2);
        height = Math.Max(2, height + height % 2);
        return (width, height);
    }

    private static async Task<(bool Ok, string? Error)> RunAsync(string ffmpeg, IReadOnlyList<MergeSource> clips, string destination, int width, int height, string encoder, CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

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
        foreach (var clip in clips)
        {
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(clip.Path);
        }

        start.ArgumentList.Add("-filter_complex");
        start.ArgumentList.Add(Graph(clips, width, height));
        start.ArgumentList.Add("-map");
        start.ArgumentList.Add("[v]");
        start.ArgumentList.Add("-map");
        start.ArgumentList.Add("[a]");
        start.ArgumentList.Add("-c:v");
        start.ArgumentList.Add(encoder);
        if (encoder == "libx264")
        {
            start.ArgumentList.Add("-preset");
            start.ArgumentList.Add("veryfast");
            start.ArgumentList.Add("-crf");
            start.ArgumentList.Add("20");
        }

        start.ArgumentList.Add("-c:a");
        start.ArgumentList.Add("aac");
        start.ArgumentList.Add("-b:a");
        start.ArgumentList.Add("160k");
        start.ArgumentList.Add("-ar");
        start.ArgumentList.Add("48000");
        start.ArgumentList.Add("-ac");
        start.ArgumentList.Add("2");
        start.ArgumentList.Add(destination);
        using var process = Process.Start(start);
        if (process is null)
        {
            return (false, "The merge could not be started.");
        }

        await using var cancel = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        });
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode == 0 && File.Exists(destination) && new FileInfo(destination).Length >= 1024)
        {
            return (true, null);
        }

        return (false, string.IsNullOrWhiteSpace(error) ? "The videos could not be joined." : TrimError(error));
    }

    private static string Graph(IReadOnlyList<MergeSource> clips, int width, int height)
    {
        var culture = CultureInfo.InvariantCulture;
        var parts = new List<string>();
        var joined = new List<string>();
        for (var index = 0; index < clips.Count; index++)
        {
            var clip = clips[index];
            var start = Math.Max(0, clip.TrimStartSeconds);
            var end = clip.TrimEndSeconds > start ? Math.Min(clip.TrimEndSeconds, clip.DurationSeconds) : clip.DurationSeconds;
            var kept = Math.Max(0.1, end - start);
            var startText = start.ToString("0.000", culture);
            var endText = end.ToString("0.000", culture);
            var keptText = kept.ToString("0.000", culture);
            parts.Add($"[{index}:v]trim=start={startText}:end={endText},setpts=PTS-STARTPTS,scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,fps=30,format=yuv420p{FadeFilters(kept, clip.VideoFadeInSeconds, clip.VideoFadeOutSeconds, audio: false)}[v{index}]");
            if (clip.HasAudio)
            {
                parts.Add($"[{index}:a]atrim=start={startText}:end={endText},asetpts=PTS-STARTPTS,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo{FadeFilters(kept, clip.AudioFadeInSeconds, clip.AudioFadeOutSeconds, audio: true)},apad=whole_dur={keptText}[a{index}]");
            }
            else
            {
                var silence = (kept + 0.5).ToString("0.000", culture);
                parts.Add($"anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration={silence},asetpts=PTS-STARTPTS[a{index}]");
            }

            joined.Add($"[v{index}][a{index}]");
        }

        parts.Add($"{string.Join(string.Empty, joined)}concat=n={clips.Count}:v=1:a=1[v][a]");
        return string.Join(';', parts);
    }

    private static string FadeFilters(double kept, double fadeIn, double fadeOut, bool audio)
    {
        var name = audio ? "afade" : "fade";
        var filters = new List<string>();
        if (fadeIn > 0)
        {
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"{name}=t=in:st=0:d={Math.Min(fadeIn, kept):0.###}"));
        }

        if (fadeOut > 0)
        {
            var length = Math.Min(fadeOut, kept);
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"{name}=t=out:st={Math.Max(0, kept - length):0.###}:d={length:0.###}"));
        }

        return filters.Count == 0 ? string.Empty : "," + string.Join(',', filters);
    }

    private static double ParseDuration(string details)
    {
        var match = Regex.Match(details, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (!match.Success)
        {
            return 0;
        }

        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600d
            + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60d
            + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    private static (int Width, int Height)? ParseSize(string details)
    {
        var match = Regex.Match(details, @"Video:.*?(\d{2,5})x(\d{2,5})");
        if (!match.Success)
        {
            return null;
        }

        return (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private static string TrimError(string error)
    {
        var line = error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? error;
        return line.Length <= 240 ? line : line[..240];
    }

    private static string? FindFfmpeg()
    {
        var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "tools", "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
