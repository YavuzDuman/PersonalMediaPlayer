using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using PersonalMediaPlayer.App.Subtitles;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace PersonalMediaPlayer.App.Editing;

internal static class VideoTrimmer
{
    public static async Task TrimAsync(string sourcePath, string destinationPath, TimeSpan start, TimeSpan end)
    {
        using var opened = await OpenClipAsync(sourcePath);
        var clip = opened.Clip;
        var duration = clip.OriginalDuration;
        if (start < TimeSpan.Zero)
        {
            start = TimeSpan.Zero;
        }

        if (end > duration)
        {
            end = duration;
        }

        if (end - start < TimeSpan.FromMilliseconds(400))
        {
            throw new InvalidOperationException("Keep at least half a second.");
        }

        clip.TrimTimeFromStart = start;
        clip.TrimTimeFromEnd = duration - end;
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        var folderPath = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The video folder is missing.");
        var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
        var destination = await folder.CreateFileAsync(Path.GetFileName(destinationPath), CreationCollisionOption.ReplaceExisting);
        var profile = ProfileMatching(clip);
        await composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, profile);
        await SubtitleEdit.CarryAsync(sourcePath, destinationPath, [(Milliseconds(start), Milliseconds(end))]);
    }

    public static async Task RemoveSectionsAsync(string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> removed, TimeSpan? keepStart = null, TimeSpan? keepEnd = null)
    {
        using var opened = await OpenClipAsync(sourcePath);
        var probe = opened.Clip;
        var duration = probe.OriginalDuration;
        var kept = KeptSpans(duration, removed);
        if (keepStart is not null || keepEnd is not null)
        {
            var from = keepStart ?? TimeSpan.Zero;
            var to = keepEnd ?? duration;
            kept = kept
                .Select(span => (Start: span.Start < from ? from : span.Start, End: span.End > to ? to : span.End))
                .Where(span => span.End > span.Start)
                .ToList();
        }
        if (kept.Count == 0 || kept.Sum(span => (span.End - span.Start).TotalMilliseconds) < 400)
        {
            throw new InvalidOperationException("Keep at least half a second.");
        }

        var composition = new MediaComposition();
        foreach (var span in kept)
        {
            var part = probe.Clone();
            part.TrimTimeFromStart = span.Start;
            part.TrimTimeFromEnd = duration - span.End;
            composition.Clips.Add(part);
        }

        var folderPath = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The video folder is missing.");
        var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
        var destination = await folder.CreateFileAsync(Path.GetFileName(destinationPath), CreationCollisionOption.ReplaceExisting);
        await composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, ProfileMatching(probe));
        await SubtitleEdit.CarryAsync(sourcePath, destinationPath, Milliseconds(kept));
    }

    public static Task RenderAsync(string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> removed, TimeSpan? keepStart = null, TimeSpan? keepEnd = null, (int X, int Y, int Width, int Height)? crop = null, string? videoFilters = null)
        => RenderCompositionAsync(sourcePath, destinationPath, removed, keepStart, keepEnd, crop, videoFilters);

    private static async Task RenderCompositionAsync(string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> removed, TimeSpan? keepStart, TimeSpan? keepEnd, (int X, int Y, int Width, int Height)? crop, string? videoFilters = null)
    {
        List<(TimeSpan Start, TimeSpan End)> kept;
        var frameWidth = 0;
        var frameHeight = 0;
        using (var opened = await OpenClipAsync(sourcePath))
        {
            var probe = opened.Clip;
            var duration = probe.OriginalDuration;
            kept = KeptSpans(duration, removed);
            if (keepStart is not null || keepEnd is not null)
            {
                var from = keepStart ?? TimeSpan.Zero;
                var to = keepEnd ?? duration;
                kept = kept
                    .Select(span => (Start: span.Start < from ? from : span.Start, End: span.End > to ? to : span.End))
                    .Where(span => span.End > span.Start)
                    .ToList();
            }

            if (kept.Count == 0 || kept.Sum(span => (span.End - span.Start).TotalMilliseconds) < 400)
            {
                throw new InvalidOperationException("Keep at least half a second.");
            }

            if (crop is { Width: >= 2, Height: >= 2 } || !string.IsNullOrEmpty(videoFilters))
            {
                var frame = probe.GetVideoEncodingProperties();
                frameWidth = (int)frame.Width;
                frameHeight = (int)frame.Height;
            }
            else
            {
                var composition = new MediaComposition();
                foreach (var span in kept)
                {
                    var part = probe.Clone();
                    part.TrimTimeFromStart = span.Start;
                    part.TrimTimeFromEnd = duration - span.End;
                    composition.Clips.Add(part);
                }

                var profile = ProfileMatching(probe);
                var folderPath = Path.GetDirectoryName(destinationPath)
                    ?? throw new InvalidOperationException("The video folder is missing.");
                var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
                var destination = await folder.CreateFileAsync(Path.GetFileName(destinationPath), CreationCollisionOption.ReplaceExisting);
                if (!await TryTranscodeAsync(composition, destination, profile))
                {
                    var reason = await composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, profile);
                    if (reason != TranscodeFailureReason.None)
                    {
                        throw new InvalidOperationException("The edited video could not be written.");
                    }
                }

                await SubtitleEdit.CarryAsync(sourcePath, destinationPath, Milliseconds(kept));
                return;
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        await WriteCropAsync(sourcePath, destinationPath, kept, crop, frameWidth, frameHeight, videoFilters);
    }

    private static async Task WriteCropAsync(string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, (int X, int Y, int Width, int Height)? crop, int frameWidth, int frameHeight, string? videoFilters)
    {
        if (await TryFfmpegCropAsync(sourcePath, destinationPath, kept, crop, videoFilters))
        {
            await SubtitleEdit.CarryAsync(sourcePath, destinationPath, Milliseconds(kept));
            return;
        }

        if (!string.IsNullOrEmpty(videoFilters))
        {
            throw new InvalidOperationException("The rotated video could not be written.");
        }

        if (crop is not { Width: >= 2, Height: >= 2 } pixels)
        {
            throw new InvalidOperationException("The edited video could not be written.");
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        var width = Math.Max(1, frameWidth);
        var height = Math.Max(1, frameHeight);
        var fractions = new VideoSpeedEncoder.VideoCrop(
            pixels.X / (double)width,
            pixels.Y / (double)height,
            (pixels.X + pixels.Width) / (double)width,
            (pixels.Y + pixels.Height) / (double)height);
        var keep = kept.Select(span => (span.Start.Ticks, span.End.Ticks)).ToArray();
        await Task.Run(() => VideoSpeedEncoder.Write(sourcePath, destinationPath, 1, 1, fractions, keep));
        if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length < 1024)
        {
            throw new InvalidOperationException("The edited video could not be written.");
        }

        await SubtitleEdit.CarryAsync(sourcePath, destinationPath, Milliseconds(kept));
    }

    private static async Task<bool> TryFfmpegCropAsync(string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, (int X, int Y, int Width, int Height)? crop, string? videoFilters)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            return false;
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        foreach (var encoder in new[] { "h264_mf", "h264_nvenc", "h264_qsv", "h264_amf", "libx264" })
        {
            if (await RunFfmpegAsync(ffmpeg, sourcePath, destinationPath, kept, crop, videoFilters, encoder, audio: true)
                || await RunFfmpegAsync(ffmpeg, sourcePath, destinationPath, kept, crop, videoFilters, encoder, audio: false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> RunFfmpegAsync(string ffmpeg, string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, (int X, int Y, int Width, int Height)? crop, string? videoFilters, string encoder, bool audio)
    {
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-filter_complex");
        startInfo.ArgumentList.Add(CropGraph(kept, crop, videoFilters, audio));
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("[v]");
        if (audio)
        {
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("[a]");
            startInfo.ArgumentList.Add("-c:a");
            startInfo.ArgumentList.Add("aac");
        }
        else
        {
            startInfo.ArgumentList.Add("-an");
        }

        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add(encoder);
        if (encoder == "libx264")
        {
            startInfo.ArgumentList.Add("-preset");
            startInfo.ArgumentList.Add("veryfast");
            startInfo.ArgumentList.Add("-crf");
            startInfo.ArgumentList.Add("20");
        }

        startInfo.ArgumentList.Add(destinationPath);
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        await process.WaitForExitAsync();
        return process.ExitCode == 0 && File.Exists(destinationPath) && new FileInfo(destinationPath).Length >= 1024;
    }

    private static string CropGraph(IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, (int X, int Y, int Width, int Height)? crop, string? videoFilters, bool audio)
    {
        var culture = CultureInfo.InvariantCulture;
        var picture = videoFilters ?? string.Empty;
        if (crop is { Width: >= 2, Height: >= 2 } pixels)
        {
            if (picture.Length > 0)
            {
                picture += ",";
            }

            picture += string.Create(culture, $"crop={pixels.Width}:{pixels.Height}:{pixels.X}:{pixels.Y}");
        }

        var parts = new List<string>();
        var inputs = new List<string>();
        for (var index = 0; index < kept.Count; index++)
        {
            var start = kept[index].Start.TotalSeconds.ToString("0.000", culture);
            var end = kept[index].End.TotalSeconds.ToString("0.000", culture);
            var filters = string.IsNullOrEmpty(picture) ? string.Empty : "," + picture;
            parts.Add($"[0:v]trim=start={start}:end={end},setpts=PTS-STARTPTS{filters}[v{index}]");
            inputs.Add($"[v{index}]");
            if (audio)
            {
                parts.Add($"[0:a]atrim=start={start}:end={end},asetpts=PTS-STARTPTS[a{index}]");
                inputs.Add($"[a{index}]");
            }
        }

        parts.Add($"{string.Join(string.Empty, inputs)}concat=n={kept.Count}:v=1:a={(audio ? 1 : 0)}[v]{(audio ? "[a]" : string.Empty)}");
        return string.Join(';', parts);
    }

    public static async Task ApplyFadesAsync(string sourcePath, string destinationPath, double durationSeconds, double videoFadeIn, double videoFadeOut, double audioFadeIn, double audioFadeOut)
    {
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException("A fade needs ffmpeg, and it is not available.");
        var duration = Math.Max(0.1, durationSeconds);
        var video = videoFadeIn > 0 || videoFadeOut > 0;
        var audio = audioFadeIn > 0 || audioFadeOut > 0;
        string? error = null;
        if (video)
        {
            foreach (var encoder in new[] { "h264_mf", "h264_nvenc", "h264_qsv", "h264_amf", "libx264" })
            {
                var (ok, detail) = await RunFadeAsync(ffmpeg, sourcePath, destinationPath, duration, videoFadeIn, videoFadeOut, audioFadeIn, audioFadeOut, encoder, keepAudio: true);
                if (ok)
                {
                    await SubtitleEdit.CarryAsync(sourcePath, destinationPath);
                    return;
                }

                error = detail;
                if (!audio && MissingAudio(detail))
                {
                    var silent = await RunFadeAsync(ffmpeg, sourcePath, destinationPath, duration, videoFadeIn, videoFadeOut, 0, 0, encoder, keepAudio: false);
                    if (silent.Ok)
                    {
                        await SubtitleEdit.CarryAsync(sourcePath, destinationPath);
                        return;
                    }
                }
            }
        }
        else
        {
            var copied = await RunFadeAsync(ffmpeg, sourcePath, destinationPath, duration, 0, 0, audioFadeIn, audioFadeOut, "copy", keepAudio: true);
            if (copied.Ok)
            {
                await SubtitleEdit.CarryAsync(sourcePath, destinationPath);
                return;
            }

            error = copied.Error;
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "The fade could not be saved." : error);
    }

    private static async Task<(bool Ok, string? Error)> RunFadeAsync(string ffmpeg, string sourcePath, string destinationPath, double duration, double videoFadeIn, double videoFadeOut, double audioFadeIn, double audioFadeOut, string encoder, bool keepAudio)
    {
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
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
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(sourcePath);
        if (videoFadeIn > 0 || videoFadeOut > 0)
        {
            start.ArgumentList.Add("-vf");
            start.ArgumentList.Add(VideoFadeFilter(duration, videoFadeIn, videoFadeOut));
            start.ArgumentList.Add("-c:v");
            start.ArgumentList.Add(encoder);
            if (encoder == "libx264")
            {
                start.ArgumentList.Add("-preset");
                start.ArgumentList.Add("veryfast");
                start.ArgumentList.Add("-crf");
                start.ArgumentList.Add("20");
            }
        }
        else
        {
            start.ArgumentList.Add("-c:v");
            start.ArgumentList.Add("copy");
        }

        if (!keepAudio)
        {
            start.ArgumentList.Add("-an");
        }
        else if (audioFadeIn > 0 || audioFadeOut > 0)
        {
            start.ArgumentList.Add("-af");
            start.ArgumentList.Add(AudioFadeFilter(duration, audioFadeIn, audioFadeOut));
            start.ArgumentList.Add("-c:a");
            start.ArgumentList.Add("aac");
        }
        else
        {
            start.ArgumentList.Add("-c:a");
            start.ArgumentList.Add("copy");
        }

        start.ArgumentList.Add(destinationPath);
        using var process = Process.Start(start);
        if (process is null)
        {
            return (false, "The fade could not be started.");
        }

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode == 0 && File.Exists(destinationPath) && new FileInfo(destinationPath).Length >= 1024)
        {
            return (true, null);
        }

        return (false, string.IsNullOrWhiteSpace(error) ? "The fade could not be saved." : TrimError(error));
    }

    public static async Task SilenceAudioAsync(string sourcePath, string destinationPath, IReadOnlyList<AudioSilence.Span> spans, long durationMs)
    {
        var filter = AudioSilence.VolumeFilter(AudioSilence.Normalize(spans, durationMs))
            ?? throw new InvalidOperationException("Select at least half a second of audio.");
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException("Silencing audio needs ffmpeg, and it is not available.");
        var wrote = false;
        try
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            string? error = await RunSilenceAsync(ffmpeg, sourcePath, destinationPath, filter, copyVideo: true, encoder: null);
            if (error is not null)
            {
                foreach (var encoder in new[] { "h264_mf", "h264_nvenc", "h264_qsv", "h264_amf", "libx264" })
                {
                    error = await RunSilenceAsync(ffmpeg, sourcePath, destinationPath, filter, copyVideo: false, encoder);
                    if (error is null)
                    {
                        break;
                    }
                }
            }

            if (error is not null)
            {
                if (MissingAudio(error))
                {
                    throw new InvalidOperationException("This video has no audio track.");
                }

                throw new InvalidOperationException(error);
            }

            await SubtitleEdit.CarryAsync(sourcePath, destinationPath);
            wrote = true;
        }
        finally
        {
            if (!wrote)
            {
                TryDelete(destinationPath);
            }
        }
    }

    private static async Task<string?> RunSilenceAsync(string ffmpeg, string sourcePath, string destinationPath, string filter, bool copyVideo, string? encoder)
    {
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
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
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(sourcePath);
        start.ArgumentList.Add("-filter_complex");
        start.ArgumentList.Add($"[0:a]{filter}[a]");
        start.ArgumentList.Add("-map");
        start.ArgumentList.Add("0:v:0");
        start.ArgumentList.Add("-map");
        start.ArgumentList.Add("[a]");
        start.ArgumentList.Add("-c:v");
        start.ArgumentList.Add(copyVideo ? "copy" : encoder!);
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
        start.ArgumentList.Add("192k");
        start.ArgumentList.Add(destinationPath);
        using var process = Process.Start(start);
        if (process is null)
        {
            return "The audio part could not be saved.";
        }

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode == 0 && File.Exists(destinationPath) && new FileInfo(destinationPath).Length >= 1024)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(stderr) ? "The audio part could not be saved." : TrimError(stderr);
    }

    public static async Task ExtractAudioAsync(string sourcePath, string destinationPath, TimeSpan duration, IReadOnlyList<(TimeSpan Start, TimeSpan End)> removed, TimeSpan? keepStart, TimeSpan? keepEnd, double speed, double volume, int sampleRate, double fadeInSeconds = 0, double fadeOutSeconds = 0, IReadOnlyList<AudioSilence.Span>? silence = null)
    {
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException("Audio export needs ffmpeg, and it is not available.");
        var kept = KeptSpans(duration, removed);
        if (keepStart is not null || keepEnd is not null)
        {
            var from = keepStart ?? TimeSpan.Zero;
            var to = keepEnd ?? duration;
            kept = kept
                .Select(span => (Start: span.Start < from ? from : span.Start, End: span.End > to ? to : span.End))
                .Where(span => span.End > span.Start)
                .ToList();
        }

        if (kept.Count == 0 || kept.Sum(span => (span.End - span.Start).TotalMilliseconds) < 400)
        {
            throw new InvalidOperationException("Keep at least half a second.");
        }

        var outputSeconds = kept.Sum(span => (span.End - span.Start).TotalSeconds) / Math.Max(0.25, speed);
        var graph = AudioGraph(kept, sampleRate, speed, volume, outputSeconds, fadeInSeconds, fadeOutSeconds, silence, duration);
        var wav = destinationPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
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
        start.ArgumentList.Add(sourcePath);
        start.ArgumentList.Add("-filter_complex");
        start.ArgumentList.Add(graph);
        start.ArgumentList.Add("-map");
        start.ArgumentList.Add("[a]");
        start.ArgumentList.Add("-vn");
        if (wav)
        {
            start.ArgumentList.Add("-c:a");
            start.ArgumentList.Add("pcm_s16le");
        }
        else
        {
            start.ArgumentList.Add("-c:a");
            start.ArgumentList.Add("libmp3lame");
            start.ArgumentList.Add("-q:a");
            start.ArgumentList.Add("2");
        }

        start.ArgumentList.Add(destinationPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Audio export could not be started.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !File.Exists(destinationPath) || new FileInfo(destinationPath).Length < 128)
        {
            if (error.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase)
                || error.Contains("matches no streams", StringComparison.OrdinalIgnoreCase)
                || error.Contains("Output file does not contain any stream", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This video has no audio track.");
            }

            var detail = error.Trim();
            if (detail.Length > 280)
            {
                detail = detail[^280..];
            }

            throw new InvalidOperationException(detail.Length == 0 ? "The audio could not be saved." : detail);
        }
    }

    private static string VideoFadeFilter(double duration, double fadeIn, double fadeOut)
    {
        var culture = CultureInfo.InvariantCulture;
        var filters = new List<string>();
        if (fadeIn > 0)
        {
            filters.Add(string.Create(culture, $"fade=t=in:st=0:d={Math.Min(fadeIn, duration):0.###}"));
        }

        if (fadeOut > 0)
        {
            var length = Math.Min(fadeOut, duration);
            filters.Add(string.Create(culture, $"fade=t=out:st={Math.Max(0, duration - length):0.###}:d={length:0.###}"));
        }

        return string.Join(',', filters);
    }

    private static string AudioFadeFilter(double duration, double fadeIn, double fadeOut)
    {
        var culture = CultureInfo.InvariantCulture;
        var filters = new List<string>();
        if (fadeIn > 0)
        {
            filters.Add(string.Create(culture, $"afade=t=in:st=0:d={Math.Min(fadeIn, duration):0.###}"));
        }

        if (fadeOut > 0)
        {
            var length = Math.Min(fadeOut, duration);
            filters.Add(string.Create(culture, $"afade=t=out:st={Math.Max(0, duration - length):0.###}:d={length:0.###}"));
        }

        return string.Join(',', filters);
    }

    private static bool MissingAudio(string? error)
        => error is not null
            && (error.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase)
                || error.Contains("matches no streams", StringComparison.OrdinalIgnoreCase)
                || error.Contains("no audio", StringComparison.OrdinalIgnoreCase));

    private static string TrimError(string error)
    {
        var detail = error.Trim();
        return detail.Length > 280 ? detail[^280..] : detail;
    }

    private static string AudioGraph(IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, int sampleRate, double speed, double volume, double outputSeconds, double fadeInSeconds, double fadeOutSeconds, IReadOnlyList<AudioSilence.Span>? silence = null, TimeSpan? duration = null)
    {
        var culture = CultureInfo.InvariantCulture;
        var extras = new List<string>();
        var silenceFilter = silence is { Count: > 0 } && duration is TimeSpan length
            ? AudioSilence.VolumeFilter(AudioSilence.Normalize(silence, (long)length.TotalMilliseconds))
            : null;
        if (Math.Abs(speed - 1d) > 0.02)
        {
            var rate = Math.Max(8000, sampleRate);
            extras.Add(string.Create(culture, $"asetrate={rate * speed:0}"));
            extras.Add(string.Create(culture, $"aresample={rate}"));
        }

        if (volume <= 0.001)
        {
            extras.Add("volume=0");
        }
        else if (Math.Abs(volume - 1d) > 0.005)
        {
            extras.Add(string.Create(culture, $"volume={volume:0.###}"));
        }

        var tail = extras.Count == 0 ? string.Empty : "," + string.Join(',', extras);
        var parts = new List<string>();
        var inputs = new List<string>();
        var head = "[0:a]";
        if (silenceFilter is not null)
        {
            parts.Add($"[0:a]{silenceFilter}[sil]");
            head = "[sil]";
        }

        for (var index = 0; index < kept.Count; index++)
        {
            var start = kept[index].Start.TotalSeconds.ToString("0.000", culture);
            var end = kept[index].End.TotalSeconds.ToString("0.000", culture);
            parts.Add($"{head}atrim=start={start}:end={end},asetpts=PTS-STARTPTS{tail}[a{index}]");
            inputs.Add($"[a{index}]");
        }

        var fade = AudioFadeFilter(Math.Max(0.1, outputSeconds), fadeInSeconds, fadeOutSeconds);
        if (string.IsNullOrEmpty(fade))
        {
            parts.Add($"{string.Join(string.Empty, inputs)}concat=n={kept.Count}:v=0:a=1[a]");
        }
        else
        {
            parts.Add($"{string.Join(string.Empty, inputs)}concat=n={kept.Count}:v=0:a=1[af]");
            parts.Add($"[af]{fade}[a]");
        }

        return string.Join(';', parts);
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

    private static async Task<bool> TryTranscodeAsync(MediaComposition composition, StorageFile destination, MediaEncodingProfile profile)
    {
        var output = await destination.OpenAsync(FileAccessMode.ReadWrite);
        try
        {
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(composition.GenerateMediaStreamSource(), output, profile);
            if (!prepared.CanTranscode)
            {
                return false;
            }

            await prepared.TranscodeAsync();
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            output.Dispose();
        }
    }

    public static List<(TimeSpan Start, TimeSpan End)> KeptSpans(TimeSpan duration, IReadOnlyList<(TimeSpan Start, TimeSpan End)> removed)
    {
        var merged = MergeSpans(removed, duration);
        var kept = new List<(TimeSpan Start, TimeSpan End)>();
        var cursor = TimeSpan.Zero;
        foreach (var span in merged)
        {
            if (span.Start > cursor)
            {
                kept.Add((cursor, span.Start));
            }

            if (span.End > cursor)
            {
                cursor = span.End;
            }
        }

        if (cursor < duration)
        {
            kept.Add((cursor, duration));
        }

        return kept;
    }

    public static List<(TimeSpan Start, TimeSpan End)> MergeSpans(IReadOnlyList<(TimeSpan Start, TimeSpan End)> spans, TimeSpan duration)
    {
        var ordered = spans
            .Select(span => (
                Start: span.Start < TimeSpan.Zero ? TimeSpan.Zero : span.Start,
                End: span.End > duration ? duration : span.End))
            .Where(span => span.End - span.Start >= TimeSpan.FromMilliseconds(400))
            .OrderBy(span => span.Start)
            .ToList();
        var merged = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var span in ordered)
        {
            if (merged.Count == 0 || span.Start > merged[^1].End)
            {
                merged.Add(span);
            }
            else
            {
                merged[^1] = (merged[^1].Start, span.End > merged[^1].End ? span.End : merged[^1].End);
            }
        }

        return merged;
    }

    private static long Milliseconds(TimeSpan time)
        => (long)Math.Round(time.TotalMilliseconds);

    private static (long StartMs, long EndMs)[] Milliseconds(IReadOnlyList<(TimeSpan Start, TimeSpan End)> spans)
        => spans.Select(span => (Milliseconds(span.Start), Milliseconds(span.End))).ToArray();

    public static async Task ChangeSpeedAsync(string sourcePath, string destinationPath, double rate, double volume, VideoSpeedEncoder.VideoCrop? crop = null, IReadOnlyList<(long Start, long End)>? keep = null)
    {
        if (rate < 0.25 || rate > 4)
        {
            throw new InvalidOperationException("Choose a speed from 0.25× to 4×.");
        }

        if (volume < 0 || volume > 1)
        {
            throw new InvalidOperationException("Choose a volume from muted to 100%.");
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        await Task.Run(() => VideoSpeedEncoder.Write(sourcePath, destinationPath, rate, volume, crop, keep));
        if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length < 1024)
        {
            throw new InvalidOperationException("The edited video could not be written.");
        }

        var spans = keep is { Count: > 0 }
            ? keep.Select(span => ((long)TimeSpan.FromTicks(span.Start).TotalMilliseconds, (long)TimeSpan.FromTicks(span.End).TotalMilliseconds)).ToArray()
            : null;
        await SubtitleEdit.CarryAsync(sourcePath, destinationPath, spans, rate);
    }

    private static MediaEncodingProfile ProfileMatching(MediaClip clip)
    {
        var source = clip.GetVideoEncodingProperties();
        var width = source.Width < 2 ? 2 : source.Width - (source.Width % 2);
        var height = source.Height < 2 ? 2 : source.Height - (source.Height % 2);
        var quality = width >= 3840 || height >= 2160
            ? VideoEncodingQuality.Uhd2160p
            : width >= 1920 || height >= 1080
                ? VideoEncodingQuality.HD1080p
                : VideoEncodingQuality.HD720p;
        var profile = MediaEncodingProfile.CreateMp4(quality);
        profile.Video.Width = width;
        profile.Video.Height = height;
        if (source.Bitrate > 0)
        {
            profile.Video.Bitrate = source.Bitrate;
        }

        if (source.FrameRate is { Numerator: > 0, Denominator: > 0 })
        {
            profile.Video.FrameRate.Numerator = source.FrameRate.Numerator;
            profile.Video.FrameRate.Denominator = source.FrameRate.Denominator;
        }

        return profile;
    }

    private static async Task<OpenedClip> OpenClipAsync(string sourcePath)
    {
        try
        {
            return new OpenedClip(await CreateClipAsync(sourcePath), null);
        }
        catch (Exception ex) when (ex.Message.Contains("Error creating clip from file", StringComparison.OrdinalIgnoreCase))
        {
            var flat = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
            try
            {
                await Task.Run(() => VideoSpeedEncoder.Write(sourcePath, flat, 1, 1));
                return new OpenedClip(await CreateClipAsync(flat), flat);
            }
            catch
            {
                TryDelete(flat);
                throw;
            }
        }
    }

    private static async Task<MediaClip> CreateClipAsync(string path)
    {
        var source = await StorageFile.GetFileFromPathAsync(path);
        return await MediaClip.CreateFromFileAsync(source);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class OpenedClip : IDisposable
    {
        public OpenedClip(MediaClip clip, string? temporaryPath)
        {
            Clip = clip;
            _temporaryPath = temporaryPath;
        }

        public MediaClip Clip { get; }

        private readonly string? _temporaryPath;

        public void Dispose()
        {
            if (_temporaryPath is not null)
            {
                TryDelete(_temporaryPath);
            }
        }
    }
}