using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.Playback;

internal enum HomeHitKind
{
    LibraryVideo,
    LibraryPhoto,
    Online
}

internal sealed record HomeHit(
    string Title,
    string Source,
    HomeHitKind Kind,
    string? FilePath,
    string? PlaylistId,
    int PlaylistIndex,
    string? Thumbnail);

internal static class HomeSearch
{
    internal static IReadOnlyList<HomeHit> Find(string? query, IEnumerable<MediaItem> library, IEnumerable<Playlist> playlists)
    {
        var term = query?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return [];
        }

        var lists = playlists as IReadOnlyList<Playlist> ?? playlists.ToArray();
        var membership = PlaylistMembership(lists);
        var hits = new List<HomeHit>();
        foreach (var item in library)
        {
            if (string.IsNullOrWhiteSpace(item.DisplayName)
                || !item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var names = item.IsVideo && membership.TryGetValue(FileKey(item.FilePath), out var found)
                ? found
                : [];
            hits.Add(new HomeHit(
                item.DisplayName,
                Describe(library: true, photo: !item.IsVideo, names),
                item.IsVideo ? HomeHitKind.LibraryVideo : HomeHitKind.LibraryPhoto,
                item.FilePath,
                null,
                -1,
                null));
        }

        foreach (var group in OnlineGroups(lists, term).Values)
        {
            hits.Add(new HomeHit(
                string.IsNullOrWhiteSpace(group.Title) ? "Video" : group.Title,
                Describe(library: false, photo: false, group.Names),
                HomeHitKind.Online,
                null,
                group.PlaylistId,
                group.Index,
                group.Thumbnail));
        }

        return hits
            .OrderBy(hit => hit.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(hit => hit.Kind == HomeHitKind.Online)
            .ThenBy(hit => hit.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(hit => hit.PlaylistId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string Describe(bool library, bool photo, IReadOnlyList<string> names)
    {
        var kind = photo ? "Photo" : "Video";
        var label = library ? "Library · " + kind : kind;
        if (names.Count == 0)
        {
            return label;
        }

        var lists = names.Count == 1 ? "Playlist" : "Playlists";
        return label + " · " + lists + " · " + string.Join(", ", names);
    }

    private static Dictionary<string, List<string>> PlaylistMembership(IReadOnlyList<Playlist> playlists)
    {
        var membership = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in playlists)
        {
            var name = PlaylistName(list);
            foreach (var entry in list.Videos)
            {
                if (entry.Resolve || string.IsNullOrWhiteSpace(entry.Location))
                {
                    continue;
                }

                var key = FileKey(entry.Location);
                if (!membership.TryGetValue(key, out var names))
                {
                    names = [];
                    membership.Add(key, names);
                }

                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }
        }

        return membership;
    }

    private static Dictionary<string, OnlineGroup> OnlineGroups(IReadOnlyList<Playlist> playlists, string term)
    {
        var groups = new Dictionary<string, OnlineGroup>(StringComparer.Ordinal);
        foreach (var list in playlists)
        {
            var name = PlaylistName(list);
            for (var index = 0; index < list.Videos.Count; index++)
            {
                var entry = list.Videos[index];
                if (!entry.Resolve || !entry.Matches(term))
                {
                    continue;
                }

                var key = OnlineKey(entry.Location);
                var title = entry.DisplayTitle();
                var picture = PictureFor(entry);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new OnlineGroup(list.Id, index, title, picture);
                    groups.Add(key, group);
                }
                else
                {
                    if (IsAddress(group.Title) && !IsAddress(title))
                    {
                        group.Title = title;
                    }

                    if (group.Thumbnail is null)
                    {
                        group.Thumbnail = picture;
                    }
                }

                if (!group.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    group.Names.Add(name);
                }
            }
        }

        return groups;
    }

    private static string? PictureFor(PlaylistEntry entry)
    {
        if (StreamThumbnail.ForPage(entry.Location) is string youtube)
        {
            return youtube;
        }

        return PlaylistEntry.CleanThumbnail(entry.Thumbnail);
    }

    private static string PlaylistName(Playlist list)
        => string.IsNullOrWhiteSpace(list.Name) ? "Playlist" : list.Name.Trim();

    private static string FileKey(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }

    private static string OnlineKey(string location)
    {
        var id = StreamThumbnail.YoutubeId(location);
        if (id is not null)
        {
            return "yt:" + id;
        }

        if (StreamLink.TryNormalize(location, out var url))
        {
            return "url:" + url.AbsoluteUri.ToLowerInvariant();
        }

        return "url:" + location.Trim().ToLowerInvariant();
    }

    private static bool IsAddress(string text)
        => text.Contains("://", StringComparison.Ordinal);

    private sealed class OnlineGroup(string playlistId, int index, string title, string? thumbnail)
    {
        public string PlaylistId { get; } = playlistId;

        public int Index { get; } = index;

        public string Title { get; set; } = title;

        public string? Thumbnail { get; set; } = thumbnail;

        public List<string> Names { get; } = [];
    }
}
