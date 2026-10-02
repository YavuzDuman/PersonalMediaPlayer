using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.Core;

public static class MediaFileTypes
{
    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"
    };

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v", ".m4a", ".mp3", ".wav"
    };

    public static bool IsImage(string path)
        => ImageExtensions.Contains(Path.GetExtension(path));

    public static bool IsVideo(string path)
        => VideoExtensions.Contains(Path.GetExtension(path));

    public static MediaKind GetKind(string path)
        => IsVideo(path) ? MediaKind.Video : MediaKind.Image;

    public static bool SameKind(string left, string right)
        => IsVideo(left) == IsVideo(right);

    public static string LocateMismatchWarning(string currentPath)
        => IsVideo(currentPath)
            ? "Choose a video. A photo cannot replace this video."
            : "Choose a photo. A video cannot replace this photo.";
}
