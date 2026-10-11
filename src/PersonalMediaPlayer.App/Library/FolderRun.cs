using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.Library;

// Videos that come after one file in an already sorted list, and the files that share its folder.
internal static class FolderRun
{
    internal static IReadOnlyList<MediaItem> After(IReadOnlyList<MediaItem> ordered, string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || ordered.Count == 0)
        {
            return [];
        }

        var index = -1;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i].FilePath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentPath };
        var rest = new List<MediaItem>();
        for (var i = index + 1; i < ordered.Count; i++)
        {
            var item = ordered[i];
            if (!item.IsVideo || item.IsMissing || string.IsNullOrWhiteSpace(item.FilePath) || !seen.Add(item.FilePath))
            {
                continue;
            }

            rest.Add(item);
        }

        return rest;
    }

    internal static IReadOnlyList<MediaItem> InDirectory(IEnumerable<MediaItem> library, string? currentPath)
    {
        var directory = DirectoryOf(currentPath);
        if (directory is null)
        {
            return [];
        }

        var found = new List<MediaItem>();
        foreach (var item in library)
        {
            if (string.Equals(DirectoryOf(item.FilePath), directory, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(item);
            }
        }

        return found;
    }

    private static string? DirectoryOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(path.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }
}
