using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalMediaPlayer.App.Playback;

internal enum WatchingKind
{
    File,
    Page,
    Stream
}

internal sealed record WatchingVideo(WatchingKind Kind, string Title, string Location, long PositionMs, string? Thumbnail)
{
    public bool IsMissing
    {
        get
        {
            if (Kind != WatchingKind.File)
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

    public string SourceLabel => Kind == WatchingKind.File
        ? IsMissing ? "Missing" : "On this PC"
        : "Online";
}

/// <summary>
/// The video that was open. This file is separate from the waiting queue, playlists, and the download queue.
/// <see cref="Load"/> restores the record and does not start playback.
/// </summary>
internal static class WatchingSession
{
    internal const string FileName = "watching.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        FileName);

    private static bool _remember;

    internal static string? StoreOverride { get; set; }

    public static WatchingVideo? Current { get; private set; }

    internal static void SuspendPersistence() => _remember = false;

    /// <summary>
    /// Reads the open video. Playback stays stopped.
    /// </summary>
    public static void Load()
    {
        Current = Read();
        _remember = true;
    }

    public static void RememberFile(string? title, string? path, long positionMs)
        => Remember(WatchingKind.File, title, path, null, positionMs);

    public static void RememberPage(string? title, string? page, string? thumbnail, long positionMs)
        => Remember(WatchingKind.Page, title, page, thumbnail, positionMs);

    public static void RememberStream(string? title, string? url, string? thumbnail, long positionMs)
        => Remember(WatchingKind.Stream, title, url, thumbnail, positionMs);

    public static void NotePosition(long positionMs)
    {
        if (Current is null)
        {
            return;
        }

        var next = Math.Max(0, positionMs);
        if (next == Current.PositionMs)
        {
            return;
        }

        Current = Current with { PositionMs = next };
        Save();
    }

    public static void Forget()
    {
        Current = null;
        if (!_remember)
        {
            return;
        }

        try
        {
            var path = StoreOverride ?? DefaultFilePath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Closing the player still leaves the previous open video until the next save.
        }
    }

    private static void Remember(WatchingKind kind, string? title, string? location, string? thumbnail, long positionMs)
    {
        var video = Create(kind, title, location, thumbnail, positionMs);
        if (video is null || video == Current)
        {
            return;
        }

        Current = video;
        Save();
    }

    private static WatchingVideo? Create(WatchingKind kind, string? title, string? location, string? thumbnail, long positionMs)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var place = location.Trim();
        string? picture = null;
        if (kind == WatchingKind.File)
        {
            if (place.Length == 0)
            {
                return null;
            }
        }
        else
        {
            if (!StreamLink.TryNormalize(place, out var url))
            {
                return null;
            }

            place = url.AbsoluteUri;
            picture = StreamThumbnail.ForPage(place) ?? PlaylistEntry.CleanThumbnail(thumbnail);
        }

        var name = string.IsNullOrWhiteSpace(title) ? FallbackTitle(kind, place) : title.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Video";
        }

        if (name.Length > 120)
        {
            name = name[..120].Trim();
        }

        return new WatchingVideo(kind, name, place, Math.Max(0, positionMs), picture);
    }

    private static string FallbackTitle(WatchingKind kind, string location)
    {
        if (kind == WatchingKind.File)
        {
            var name = Path.GetFileName(location);
            return string.IsNullOrWhiteSpace(name) ? "Video" : name;
        }

        return StreamLink.TryNormalize(location, out var url) ? StreamLink.DisplayName(url) : "Video";
    }

    private static void Save()
    {
        if (!_remember || Current is null)
        {
            return;
        }

        try
        {
            var stored = new StoredVideo
            {
                Kind = Current.Kind.ToString(),
                Title = Current.Title,
                Location = Current.Location,
                PositionMs = Current.PositionMs,
                Thumbnail = Current.Thumbnail
            };
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
            // A failed snapshot leaves the previous open video on disk.
        }
    }

    private static WatchingVideo? Read()
    {
        try
        {
            var path = StoreOverride ?? DefaultFilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<StoredVideo>(File.ReadAllText(path), JsonOptions);
            if (stored is null || !Enum.TryParse<WatchingKind>(stored.Kind, ignoreCase: true, out var kind))
            {
                return null;
            }

            return Create(kind, stored.Title, stored.Location, stored.Thumbnail, stored.PositionMs);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class StoredVideo
    {
        public string? Kind { get; set; }

        public string? Title { get; set; }

        public string? Location { get; set; }

        public long PositionMs { get; set; }

        public string? Thumbnail { get; set; }
    }
}
