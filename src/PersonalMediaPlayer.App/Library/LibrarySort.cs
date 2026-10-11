using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.Library;

// Orders the items it is given. Search and the type filter choose that list first.
internal static class LibrarySort
{
    internal const int Newest = 0;
    internal const int Oldest = 1;
    internal const int NameAscending = 2;
    internal const int NameDescending = 3;
    internal const int Largest = 4;
    internal const int Smallest = 5;
    internal const int Longest = 6;
    internal const int Shortest = 7;
    internal const int Type = 8;

    internal static IReadOnlyList<string> Options { get; } =
    [
        "Date added, newest",
        "Date added, oldest",
        "Name A–Z",
        "Name Z–A",
        "File size, largest",
        "File size, smallest",
        "Video length, longest",
        "Video length, shortest",
        "Type"
    ];

    internal static bool NeedsLength(int sortIndex) => sortIndex is Longest or Shortest;

    internal static List<MediaItem> Order(IEnumerable<MediaItem> items, int sortIndex, Func<MediaItem, long>? lengthMs = null)
    {
        var list = items.ToList();
        list.Sort((left, right) => Compare(left, right, sortIndex, lengthMs));
        return list;
    }

    private static int Compare(MediaItem left, MediaItem right, int sortIndex, Func<MediaItem, long>? lengthMs)
    {
        var primary = sortIndex switch
        {
            Oldest => left.ImportedAt.CompareTo(right.ImportedAt),
            NameAscending => CompareName(left, right),
            NameDescending => CompareName(right, left),
            Largest => right.FileSizeBytes.CompareTo(left.FileSizeBytes),
            Smallest => left.FileSizeBytes.CompareTo(right.FileSizeBytes),
            Longest => CompareLength(left, right, lengthMs, longestFirst: true),
            Shortest => CompareLength(left, right, lengthMs, longestFirst: false),
            Type => string.Compare(left.TypeLabel, right.TypeLabel, StringComparison.CurrentCultureIgnoreCase),
            _ => right.ImportedAt.CompareTo(left.ImportedAt)
        };
        if (primary != 0)
        {
            return primary;
        }

        var name = CompareName(left, right);
        if (name != 0)
        {
            return name;
        }

        return string.Compare(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareLength(MediaItem left, MediaItem right, Func<MediaItem, long>? lengthMs, bool longestFirst)
    {
        var leftMs = LengthOf(left, lengthMs);
        var rightMs = LengthOf(right, lengthMs);
        var leftKnown = leftMs > 0;
        var rightKnown = rightMs > 0;
        if (leftKnown != rightKnown)
        {
            return leftKnown ? -1 : 1;
        }

        if (!leftKnown)
        {
            return 0;
        }

        return longestFirst ? rightMs.CompareTo(leftMs) : leftMs.CompareTo(rightMs);
    }

    private static long LengthOf(MediaItem item, Func<MediaItem, long>? lengthMs)
    {
        if (lengthMs is null || !item.IsVideo)
        {
            return 0;
        }

        var length = lengthMs(item);
        return length < 0 ? 0 : length;
    }

    private static int CompareName(MediaItem left, MediaItem right)
        => string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
}
