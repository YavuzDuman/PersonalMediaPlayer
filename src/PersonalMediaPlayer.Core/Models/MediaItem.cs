namespace PersonalMediaPlayer.Core.Models;

public sealed class MediaItem
{
    public string Id { get; init; } = string.Empty;

    public MediaKind Kind { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public DateTimeOffset ImportedAt { get; init; }

    public long FileSizeBytes { get; init; }

    public string FolderName { get; init; } = string.Empty;

    public bool IsVideo => Kind is MediaKind.Video or MediaKind.Recording;

    public string TypeLabel => IsVideo ? "Video" : "Image";

    public string SizeLabel
    {
        get
        {
            const double kb = 1024;
            const double mb = kb * 1024;
            if (FileSizeBytes >= mb)
            {
                return $"{FileSizeBytes / mb:0.0} MB";
            }

            if (FileSizeBytes >= kb)
            {
                return $"{FileSizeBytes / kb:0} KB";
            }

            return $"{FileSizeBytes} B";
        }
    }
}
