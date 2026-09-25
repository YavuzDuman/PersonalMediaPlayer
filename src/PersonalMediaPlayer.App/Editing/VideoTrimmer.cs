using Windows.Media.Editing;
using Windows.Media.MediaProperties;
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

    public static async Task ChangeSpeedAsync(string sourcePath, string destinationPath, double rate, double volume)
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

        await Task.Run(() => VideoSpeedEncoder.Write(sourcePath, destinationPath, rate, volume));
        if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length < 1024)
        {
            throw new InvalidOperationException("The sped-up video could not be written.");
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