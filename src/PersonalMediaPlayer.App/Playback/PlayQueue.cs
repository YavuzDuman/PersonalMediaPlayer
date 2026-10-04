namespace PersonalMediaPlayer.App.Playback;

internal enum PlayQueueKind
{
    File,
    Page
}

internal enum PlayAdvanceKind
{
    Queue,
    Playlist,
    Stop
}

internal sealed class PlayQueueItem
{
    public PlayQueueItem(string id, string title, PlayQueueKind kind, string location, string? thumbnail)
    {
        Id = id;
        Title = title;
        Kind = kind;
        Location = location;
        Thumbnail = thumbnail;
    }

    public string Id { get; }

    public string Title { get; }

    public PlayQueueKind Kind { get; }

    public string Location { get; }

    public string? Thumbnail { get; }
}

/// <summary>
/// Session list of videos waiting after the current one. Nothing here is written to disk or into a playlist.
/// Add appends. Play next inserts at the front. <see cref="Move"/> reorders this list only.
/// <see cref="Remove"/> and <see cref="ClearWaiting"/> each keep one previous list for <see cref="Undo"/>.
/// The window calls <see cref="Clear"/> when it closes, and that drops the list and the undo.
/// An A–B section is restarted by the player before <see cref="Advance"/> is asked, so a section loop is not skipped for the queue.
/// </summary>
internal static class PlayQueue
{
    private static readonly List<PlayQueueItem> Items = [];
    private static PlayQueueItem[]? _before;
    private static PlayQueueItem[]? _after;
    private static string _message = string.Empty;

    public static event EventHandler? Changed;

    public static int Count => Items.Count;

    public static IReadOnlyList<PlayQueueItem> Snapshot() => Items.ToArray();

    public static PlayAdvanceKind Advance(int queueCount, bool hasPlaylist)
        => queueCount > 0
            ? PlayAdvanceKind.Queue
            : hasPlaylist ? PlayAdvanceKind.Playlist : PlayAdvanceKind.Stop;

    public static PlayQueueItem? AddFile(string? title, string? path, bool notify = true)
        => Place(CreateFile(title, path), Items.Count, notify);

    public static PlayQueueItem? PlayNextFile(string? title, string? path, bool notify = true)
        => Place(CreateFile(title, path), 0, notify);

    public static int AddFiles(IReadOnlyList<(string Title, string Path)> files, bool next, bool notify = true)
    {
        var index = next ? 0 : Items.Count;
        var added = 0;
        foreach (var file in files)
        {
            if (Place(CreateFile(file.Title, file.Path), index, notify: false) is null)
            {
                continue;
            }

            added++;
            index++;
        }

        if (notify && added > 0)
        {
            Raise();
        }

        return added;
    }

    public static PlayQueueItem? AddPage(string? title, string? page, string? thumbnail, bool notify = true)
        => Place(CreatePage(title, page, thumbnail), Items.Count, notify);

    public static PlayQueueItem? PlayNextPage(string? title, string? page, string? thumbnail, bool notify = true)
        => Place(CreatePage(title, page, thumbnail), 0, notify);

    public static PlayQueueItem? Add(PlaylistEntry entry, Func<string, string?>? libraryName = null, bool notify = true)
        => Place(FromEntry(entry, libraryName), Items.Count, notify);

    public static PlayQueueItem? PlayNext(PlaylistEntry entry, Func<string, string?>? libraryName = null, bool notify = true)
        => Place(FromEntry(entry, libraryName), 0, notify);

    public static bool Move(int from, int to)
    {
        var count = Items.Count;
        if (from < 0 || from >= count)
        {
            return false;
        }

        if (to < 0)
        {
            to = 0;
        }

        if (to > count)
        {
            to = count;
        }

        if (to == from || to == from + 1)
        {
            return false;
        }

        var item = Items[from];
        Items.RemoveAt(from);
        if (to > from)
        {
            to--;
        }

        Items.Insert(to, item);
        DropPending();
        Raise();
        return true;
    }

    public static bool Remove(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        var index = Items.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            return false;
        }

        Remember($"Removed \"{Items[index].Title}\".");
        Items.RemoveAt(index);
        FinishRemember();
        Raise();
        return true;
    }

    /// <summary>
    /// Drops every waiting video and remembers that order for <see cref="Undo"/>.
    /// An empty queue leaves a pending undo alone. This does not stop the video that is playing.
    /// </summary>
    public static bool ClearWaiting()
    {
        if (Items.Count == 0)
        {
            return false;
        }

        Remember("Cleared the queue.");
        Items.Clear();
        FinishRemember();
        Raise();
        return true;
    }

    public static bool IsPending(out string message)
    {
        message = string.Empty;
        if (_before is null || _after is null)
        {
            return false;
        }

        if (!Same(_after, Items))
        {
            DropPending();
            return false;
        }

        message = _message;
        return true;
    }

    public static bool Undo()
    {
        if (_before is null || _after is null || !Same(_after, Items))
        {
            if (_before is not null || _after is not null)
            {
                DropPending();
            }

            return false;
        }

        var restored = _before;
        DropPending();
        Items.Clear();
        Items.AddRange(restored);
        Raise();
        return true;
    }

    public static void Dismiss()
    {
        if (_before is null && _after is null)
        {
            return;
        }

        DropPending();
        Raise();
    }

    public static PlayQueueItem? TakeNext()
    {
        if (Items.Count == 0)
        {
            return null;
        }

        var item = Items[0];
        Items.RemoveAt(0);
        DropPending();
        Raise();
        return item;
    }

    public static void Notify() => Raise();

    /// <summary>
    /// Drops every waiting video and any pending undo. The window calls this when it closes.
    /// </summary>
    public static void Clear()
    {
        if (Items.Count == 0 && _before is null && _after is null)
        {
            return;
        }

        Items.Clear();
        DropPending();
        Raise();
    }

    private static PlayQueueItem? FromEntry(PlaylistEntry entry, Func<string, string?>? libraryName)
        => entry.Resolve
            ? CreatePage(entry.DisplayTitle(libraryName), entry.Location, entry.Thumbnail)
            : CreateFile(entry.DisplayTitle(libraryName), entry.Location);

    private static PlayQueueItem? CreateFile(string? title, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var location = path.Trim();
        return new PlayQueueItem(NewId(), CleanTitle(title, Path.GetFileName(location)), PlayQueueKind.File, location, null);
    }

    private static PlayQueueItem? CreatePage(string? title, string? page, string? thumbnail)
    {
        if (!StreamLink.TryNormalize(page, out var url))
        {
            return null;
        }

        var picture = StreamThumbnail.ForPage(url.AbsoluteUri) ?? PlaylistEntry.CleanThumbnail(thumbnail);
        return new PlayQueueItem(NewId(), CleanTitle(title, StreamLink.DisplayName(url)), PlayQueueKind.Page, url.AbsoluteUri, picture);
    }

    private static PlayQueueItem? Place(PlayQueueItem? item, int index, bool notify)
    {
        if (item is null)
        {
            return null;
        }

        if (index < 0)
        {
            index = 0;
        }

        if (index > Items.Count)
        {
            index = Items.Count;
        }

        DropPending();
        Items.Insert(index, item);
        if (notify)
        {
            Raise();
        }

        return item;
    }

    private static void Remember(string message)
    {
        _before = Items.ToArray();
        _after = null;
        _message = message;
    }

    private static void FinishRemember() => _after = Items.ToArray();

    private static void DropPending()
    {
        _before = null;
        _after = null;
        _message = string.Empty;
    }

    private static bool Same(IReadOnlyList<PlayQueueItem> left, IReadOnlyList<PlayQueueItem> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Id, right[i].Id, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void Raise() => Changed?.Invoke(null, EventArgs.Empty);

    private static string CleanTitle(string? title, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Video";
        }

        return name.Length > 120 ? name[..120].Trim() : name;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
