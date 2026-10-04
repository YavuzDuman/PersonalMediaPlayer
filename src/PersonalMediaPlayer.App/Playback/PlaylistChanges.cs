namespace PersonalMediaPlayer.App.Playback;

internal static class PlaylistChanges
{
    private static string? _id;
    private static string _message = string.Empty;
    private static List<PlaylistEntry>? _before;
    private static List<PlaylistEntry>? _after;

    internal static event Action? Changed;

    public static bool Apply(string playlistId, string message, Action change)
    {
        var existing = Playlists.Find(playlistId);
        var before = existing?.Videos.Select(item => item.Copy()).ToList();
        change();
        if (before is null)
        {
            return false;
        }

        var updated = Playlists.Find(playlistId);
        if (updated is null || Same(before, updated.Videos))
        {
            return false;
        }

        _id = playlistId;
        _message = message;
        _before = before;
        _after = updated.Videos.Select(item => item.Copy()).ToList();
        return true;
    }

    public static bool IsPending(string? playlistId, out string message)
    {
        message = string.Empty;
        if (_id is null || _before is null || _after is null
            || !string.Equals(_id, playlistId, StringComparison.Ordinal))
        {
            return false;
        }

        var current = Playlists.Find(_id);
        if (current is null || !Same(_after, current.Videos))
        {
            Clear();
            return false;
        }

        message = _message;
        return true;
    }

    public static string? Undo()
    {
        if (_id is null || _before is null || _after is null)
        {
            return null;
        }

        var id = _id;
        var before = _before;
        var current = Playlists.Find(id);
        if (current is null || !Same(_after, current.Videos))
        {
            Clear();
            return null;
        }

        var restored = before.Select(item => item.Copy()).ToList();
        KeepThumbnails(restored, current.Videos);
        if (!Playlists.ReplaceVideos(id, restored))
        {
            Clear();
            return null;
        }

        Clear();
        return id;
    }

    public static int IndexOf(IReadOnlyList<PlaylistEntry> videos, PlaylistEntry entry)
    {
        for (var i = 0; i < videos.Count; i++)
        {
            if (SameVideo(videos[i], entry))
            {
                return i;
            }
        }

        return -1;
    }

    public static void Dismiss() => Clear();

    internal static void NoteSaved()
    {
        if (_id is null || _after is null)
        {
            return;
        }

        var current = Playlists.Find(_id);
        if (current is null || !Same(_after, current.Videos))
        {
            Clear();
        }
    }

    private static void Clear()
    {
        if (_id is null && _before is null)
        {
            return;
        }

        _id = null;
        _message = string.Empty;
        _before = null;
        _after = null;
        Changed?.Invoke();
    }

    private static void KeepThumbnails(List<PlaylistEntry> restored, IReadOnlyList<PlaylistEntry> current)
    {
        var used = new bool[current.Count];
        foreach (var entry in restored)
        {
            for (var i = 0; i < current.Count; i++)
            {
                if (used[i] || !SameVideo(entry, current[i]))
                {
                    continue;
                }

                used[i] = true;
                entry.Watched = current[i].Watched;
                if (!string.IsNullOrEmpty(current[i].Thumbnail))
                {
                    entry.Thumbnail = current[i].Thumbnail;
                }

                break;
            }
        }
    }

    private static bool Same(IReadOnlyList<PlaylistEntry> left, IReadOnlyList<PlaylistEntry> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (!SameVideo(a, b)
                || !string.Equals(a.Title, b.Title, StringComparison.Ordinal)
                || a.AddedUtc != b.AddedUtc)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameVideo(PlaylistEntry left, PlaylistEntry right)
        => left.Resolve == right.Resolve
           && string.Equals(left.Location, right.Location, StringComparison.OrdinalIgnoreCase);
}
