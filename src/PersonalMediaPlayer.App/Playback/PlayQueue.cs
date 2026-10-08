using System.Text.Json;
using System.Text.Json.Serialization;

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

    public bool IsMissing
    {
        get
        {
            if (Kind != PlayQueueKind.File)
            {
                return false;
            }

            try
            {
                return !File.Exists(Location);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    public string SourceLabel => Kind == PlayQueueKind.Page
        ? "Online"
        : IsMissing ? "Missing" : "On this PC";
}

/// <summary>
/// Videos waiting after the current one. The order is the play order, so a Play next item stays at the front.
/// Each change is written to <c>play-queue.json</c>, separate from playlists and the download queue.
/// <see cref="Load"/> restores that order and does not start playback. A missing local file stays in the list.
/// Add appends. Play next inserts at the front. <see cref="Move"/> reorders this list only.
/// Saving these videos as a playlist is <see cref="Playlists.SaveFromQueue"/>, which writes playlists.json and leaves this file as it is.
/// <see cref="Remove"/> and <see cref="ClearWaiting"/> each keep one previous list for <see cref="Undo"/>.
/// That undo lasts until the app closes. <see cref="Clear"/> drops the in-memory list for tests and does not erase the file.
/// An A–B section is restarted by the player before <see cref="Advance"/> is asked, so a section loop is not skipped for the queue.
/// </summary>
internal static class PlayQueue
{
    internal const string FileName = "play-queue.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        FileName);

    private static readonly List<PlayQueueItem> Items = [];
    private static PlayQueueItem[]? _before;
    private static PlayQueueItem[]? _after;
    private static string _message = string.Empty;
    private static bool _remember;

    internal static string? StoreOverride { get; set; }

    /// <summary>
    /// Stops writing the queue. Tests call this so they do not touch the queue file on this PC.
    /// </summary>
    internal static void SuspendPersistence() => _remember = false;

    /// <summary>
    /// Reads the waiting videos into this list. Playback is left stopped.
    /// </summary>
    public static void Load()
    {
        Items.Clear();
        DropPending();
        Items.AddRange(ReadStored());
        _remember = true;
        Raise();
    }

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
        Save();
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
        Save();
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
        Save();
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
        Save();
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
        Save();
        Raise();
        return item;
    }

    public static void Notify() => Raise();

    /// <summary>
    /// Drops the in-memory list and any pending undo. The saved file is left as it is.
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
        Save();
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

    private static void Save()
    {
        if (!_remember)
        {
            return;
        }

        try
        {
            var stored = new List<StoredItem>(Items.Count);
            foreach (var item in Items)
            {
                stored.Add(new StoredItem
                {
                    Id = item.Id,
                    Title = item.Title,
                    Kind = item.Kind == PlayQueueKind.Page ? "Page" : "File",
                    Location = item.Location,
                    Thumbnail = item.Thumbnail
                });
            }

            var path = StoreOverride ?? DefaultFilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(stored, JsonOptions));
        }
        catch (Exception)
        {
            // A failed snapshot leaves the previous waiting list on disk.
        }
    }

    private static List<PlayQueueItem> ReadStored()
    {
        try
        {
            var path = StoreOverride ?? DefaultFilePath;
            if (!File.Exists(path))
            {
                return [];
            }

            var stored = JsonSerializer.Deserialize<List<StoredItem>>(File.ReadAllText(path), JsonOptions) ?? [];
            var items = new List<PlayQueueItem>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in stored)
            {
                if (TryRestore(entry, ids) is PlayQueueItem item)
                {
                    items.Add(item);
                }
            }

            return items;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static PlayQueueItem? TryRestore(StoredItem? entry, HashSet<string> ids)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Location))
        {
            return null;
        }

        if (string.Equals(entry.Kind, "Page", StringComparison.OrdinalIgnoreCase))
        {
            if (!StreamLink.TryNormalize(entry.Location, out var url))
            {
                return null;
            }

            var picture = StreamThumbnail.ForPage(url.AbsoluteUri) ?? PlaylistEntry.CleanThumbnail(entry.Thumbnail);
            return new PlayQueueItem(
                UniqueId(entry.Id, ids),
                CleanTitle(entry.Title, StreamLink.DisplayName(url)),
                PlayQueueKind.Page,
                url.AbsoluteUri,
                picture);
        }

        if (!string.Equals(entry.Kind, "File", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var location = entry.Location.Trim();
        if (location.Length == 0)
        {
            return null;
        }

        return new PlayQueueItem(
            UniqueId(entry.Id, ids),
            CleanTitle(entry.Title, Path.GetFileName(location)),
            PlayQueueKind.File,
            location,
            null);
    }

    private static string UniqueId(string? id, HashSet<string> used)
    {
        var chosen = string.IsNullOrWhiteSpace(id) ? NewId() : id.Trim();
        if (!used.Add(chosen))
        {
            chosen = NewId();
            used.Add(chosen);
        }

        return chosen;
    }

    private sealed class StoredItem
    {
        public string? Id { get; set; }

        public string? Title { get; set; }

        public string? Kind { get; set; }

        public string? Location { get; set; }

        public string? Thumbnail { get; set; }
    }
}
