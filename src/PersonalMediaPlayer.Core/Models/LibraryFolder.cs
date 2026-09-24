namespace PersonalMediaPlayer.Core.Models;

public sealed class LibraryFolder
{
    public const string All = "";

    public const string Screenshots = "Screenshots";

    public const string Unfiled = "Unfiled";

    public const string Recordings = "Recordings";

    public const string Photos = "Photos";

    public const string Videos = "Videos";

    public const string ThisMonth = "This month";

    public const string RecentlyDeleted = "Recently deleted";

    public const string Favorites = "Favorites";

    public const string Duplicates = "Duplicates";

    public string Name { get; init; } = string.Empty;

    public int FileCount { get; init; }

    public string? CoverPath { get; init; }

    public bool CoverIsVideo { get; init; }

    public bool CountPending { get; init; }

    public string CountLabel => CountPending
        ? "Same content"
        : FileCount switch
    {
        0 => "Empty",
        1 => "1 item",
        _ => $"{FileCount} items"
    };

    public bool IsAll => string.IsNullOrEmpty(Name);

    public bool IsUnfiled => Name.Equals(Unfiled, StringComparison.OrdinalIgnoreCase);

    public bool IsRecordings => Name.Equals(Recordings, StringComparison.OrdinalIgnoreCase);

    public bool IsPhotos => Name.Equals(Photos, StringComparison.OrdinalIgnoreCase);

    public bool IsVideos => Name.Equals(Videos, StringComparison.OrdinalIgnoreCase);

    public bool IsThisMonth => Name.Equals(ThisMonth, StringComparison.OrdinalIgnoreCase);

    public bool IsSmart => IsPhotos || IsVideos || IsThisMonth;

    public bool IsRecentlyDeleted => Name.Equals(RecentlyDeleted, StringComparison.OrdinalIgnoreCase);

    public bool IsFavorites => Name.Equals(Favorites, StringComparison.OrdinalIgnoreCase);

    public bool IsDuplicates => Name.Equals(Duplicates, StringComparison.OrdinalIgnoreCase);

    public bool IsSystem =>
        Name.Equals(Screenshots, StringComparison.OrdinalIgnoreCase)
        || IsUnfiled
        || IsRecordings
        || IsSmart
        || IsRecentlyDeleted
        || IsFavorites
        || IsDuplicates;

    public static bool IsSmartName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && (name.Equals(Photos, StringComparison.OrdinalIgnoreCase)
               || name.Equals(Videos, StringComparison.OrdinalIgnoreCase)
               || name.Equals(ThisMonth, StringComparison.OrdinalIgnoreCase));

    public static bool IsInThisMonth(DateTimeOffset timestamp)
    {
        var local = timestamp.ToLocalTime();
        var today = DateTimeOffset.Now;
        return local.Year == today.Year && local.Month == today.Month;
    }

    public string DisplayName => IsAll ? "All media" : Name;
}
