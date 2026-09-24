using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace PersonalMediaPlayer.App.Editing;

internal static class VideoTrimmer
{
    public static async Task TrimAsync(string sourcePath, string destinationPath, TimeSpan start, TimeSpan end)
    {
        var source = await StorageFile.GetFileFromPathAsync(sourcePath);
        var clip = await MediaClip.CreateFromFileAsync(source);
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
}