using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
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
    }

    public static Task RenderAsync(string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> removed, TimeSpan? keepStart = null, TimeSpan? keepEnd = null, (int X, int Y, int Width, int Height)? crop = null)
        => RenderCompositionAsync(sourcePath, destinationPath, removed, keepStart, keepEnd, crop);

    private static async Task RenderCompositionAsync(string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> removed, TimeSpan? keepStart, TimeSpan? keepEnd, (int X, int Y, int Width, int Height)? crop)
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

            if (crop is { Width: >= 2, Height: >= 2 })
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

                return;
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        await WriteCropAsync(sourcePath, destinationPath, kept, crop!.Value, frameWidth, frameHeight);
    }

    private static async Task WriteCropAsync(string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, (int X, int Y, int Width, int Height) crop, int frameWidth, int frameHeight)
    {
        if (await TryFfmpegCropAsync(sourcePath, destinationPath, kept, crop))
        {
            return;
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        var width = Math.Max(1, frameWidth);
        var height = Math.Max(1, frameHeight);
        var fractions = new VideoSpeedEncoder.VideoCrop(
            crop.X / (double)width,
            crop.Y / (double)height,
            (crop.X + crop.Width) / (double)width,
            (crop.Y + crop.Height) / (double)height);
        var keep = kept.Select(span => (span.Start.Ticks, span.End.Ticks)).ToArray();
        await Task.Run(() => VideoSpeedEncoder.Write(sourcePath, destinationPath, 1, 1, fractions, keep));
        if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length < 1024)
        {
            throw new InvalidOperationException("The edited video could not be written.");
        }
    }

    private static async Task<bool> TryFfmpegCropAsync(string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, (int X, int Y, int Width, int Height) crop)
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
            if (await RunFfmpegAsync(ffmpeg, sourcePath, destinationPath, kept, crop, encoder, audio: true)
                || await RunFfmpegAsync(ffmpeg, sourcePath, destinationPath, kept, crop, encoder, audio: false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> RunFfmpegAsync(string ffmpeg, string sourcePath, string destinationPath, IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, (int X, int Y, int Width, int Height) crop, string encoder, bool audio)
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
        startInfo.ArgumentList.Add(CropGraph(kept, crop, audio));
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

    private static string CropGraph(IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept, (int X, int Y, int Width, int Height) crop, bool audio)
    {
        var culture = CultureInfo.InvariantCulture;
        var cropFilter = string.Create(culture, $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}");
        var parts = new List<string>();
        var inputs = new List<string>();
        for (var index = 0; index < kept.Count; index++)
        {
            var start = kept[index].Start.TotalSeconds.ToString("0.000", culture);
            var end = kept[index].End.TotalSeconds.ToString("0.000", culture);
            parts.Add($"[0:v]trim=start={start}:end={end},setpts=PTS-STARTPTS,{cropFilter}[v{index}]");
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