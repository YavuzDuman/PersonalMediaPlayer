using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalMediaPlayer.App.Playback;

internal static class Playlists
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "playlists.json");

    internal static string? StoreOverride { get; set; }

    private static string FilePath => StoreOverride ?? DefaultFilePath;

    public static IReadOnlyList<Playlist> All() => Read();

    public static Playlist? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return Read().FirstOrDefault(list => string.Equals(list.Id, id, StringComparison.Ordinal));
    }

    public static bool NameTaken(string name, string? exceptId = null)
    {
        var clean = CleanName(name);
        if (clean is null)
        {
            return false;
        }

        return Read().Any(list =>
            !string.Equals(list.Id, exceptId, StringComparison.Ordinal)
            && string.Equals(list.Name, clean, StringComparison.OrdinalIgnoreCase));
    }

    public static Playlist? Create(string name)
    {
        var clean = CleanName(name);
        if (clean is null || NameTaken(clean))
        {
            return null;
        }

        var lists = Read();
        var created = new Playlist
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = clean,
            CreatedAt = DateTimeOffset.Now.ToString("O")
        };
        lists.Add(created);
        Write(lists);
        return created;
    }

    public static bool Rename(string id, string name)
    {
        var clean = CleanName(name);
        if (clean is null || NameTaken(clean, id))
        {
            return false;
        }

        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null)
        {
            return false;
        }

        list.Name = clean;
        Write(lists);
        return true;
    }

    public static void Delete(string id)
    {
        var lists = Read();
        if (lists.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) == 0)
        {
            return;
        }

        Write(lists);
    }

    public static int Add(string id, IReadOnlyList<string> paths)
    {
        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null)
        {
            return 0;
        }

        var added = 0;
        var stamp = DateTimeOffset.UtcNow;
        foreach (var path in paths)
        {
            var clean = CleanPath(path);
            if (clean is null || list.Videos.Any(existing => !existing.Resolve && SamePath(existing.Location, clean)))
            {
                continue;
            }

            var entry = PlaylistEntry.ForFile(clean);
            entry.AddedUtc = stamp;
            stamp = stamp.AddTicks(1);
            list.Videos.Add(entry);
            added++;
        }

        if (added > 0)
        {
            Write(lists);
        }

        return added;
    }

    public static int AddPage(string id, string pageUrl, string? title)
    {
        if (!Uri.TryCreate(pageUrl.Trim(), UriKind.Absolute, out var page)
            || (page.Scheme != Uri.UriSchemeHttp && page.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(page.Host))
        {
            return -1;
        }

        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null)
        {
            return -1;
        }

        var location = page.AbsoluteUri;
        if (list.Videos.Any(existing => existing.Resolve && SamePath(existing.Location, location)))
        {
            return 0;
        }

        var entry = PlaylistEntry.Page(location, title);
        entry.AddedUtc = DateTimeOffset.UtcNow;
        list.Videos.Add(entry);
        Write(lists);
        return 1;
    }

    public static void RemoveAt(string id, int index)
    {
        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null || index < 0 || index >= list.Videos.Count)
        {
            return;
        }

        list.Videos.RemoveAt(index);
        Write(lists);
    }

    public static void MoveVideo(string id, int index, int delta)
    {
        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null)
        {
            return;
        }

        var target = index + delta;
        if (index < 0 || target < 0 || index >= list.Videos.Count || target >= list.Videos.Count)
        {
            return;
        }

        (list.Videos[index], list.Videos[target]) = (list.Videos[target], list.Videos[index]);
        Write(lists);
    }

    public static bool MoveTo(string id, int from, int to)
    {
        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null)
        {
            return false;
        }

        var count = list.Videos.Count;
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

        var item = list.Videos[from];
        list.Videos.RemoveAt(from);
        if (to > from)
        {
            to--;
        }

        list.Videos.Insert(to, item);
        Write(lists);
        return true;
    }

    public static void SortByName(string id, Func<string, string?>? libraryName)
    {
        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null || list.Videos.Count < 2)
        {
            return;
        }

        list.Videos = list.Videos
            .Select((video, index) => (video, index))
            .OrderBy(item => item.video.DisplayTitle(libraryName), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.index)
            .Select(item => item.video)
            .ToList();
        Write(lists);
    }

    public static void SortByAdded(string id)
    {
        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null || list.Videos.Count == 0)
        {
            return;
        }

        StampMissingAdded(list);
        if (list.Videos.Count > 1)
        {
            list.Videos = list.Videos
                .Select((video, index) => (video, index))
                .OrderByDescending(item => item.video.AddedUtc ?? DateTimeOffset.MinValue)
                .ThenByDescending(item => item.index)
                .Select(item => item.video)
                .ToList();
        }

        Write(lists);
    }

    private static void StampMissingAdded(Playlist list)
    {
        var missing = list.Videos.Count(video => video.AddedUtc is null);
        if (missing == 0)
        {
            return;
        }

        DateTimeOffset? earliest = null;
        foreach (var video in list.Videos)
        {
            if (video.AddedUtc is DateTimeOffset stamp && (earliest is null || stamp < earliest))
            {
                earliest = stamp;
            }
        }

        var start = (earliest ?? DateTimeOffset.UtcNow).AddSeconds(-missing);
        var placed = 0;
        foreach (var video in list.Videos)
        {
            if (video.AddedUtc is not null)
            {
                continue;
            }

            video.AddedUtc = start.AddSeconds(placed);
            placed++;
        }
    }

    public static void SetPlayNext(string id, bool playNext)
    {
        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null || list.PlayNext == playNext)
        {
            return;
        }

        list.PlayNext = playNext;
        Write(lists);
    }

    public static bool SetWatched(string id, int index, bool watched)
    {
        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null || index < 0 || index >= list.Videos.Count || list.Videos[index].Watched == watched)
        {
            return false;
        }

        list.Videos[index].Watched = watched;
        Write(lists);
        return true;
    }

    public static void MoveFile(string oldPath, string newPath)
    {
        var from = CleanPath(oldPath);
        var to = CleanPath(newPath);
        if (from is null || to is null || SamePath(from, to))
        {
            return;
        }

        var lists = Read();
        var changed = false;
        foreach (var list in lists)
        {
            for (var i = 0; i < list.Videos.Count; i++)
            {
                var entry = list.Videos[i];
                if (entry.Resolve || !SamePath(entry.Location, from))
                {
                    continue;
                }

                var replacement = PlaylistEntry.ForFile(to);
                replacement.AddedUtc = entry.AddedUtc;
                replacement.Watched = entry.Watched;
                list.Videos[i] = replacement;
                changed = true;
            }
        }

        if (changed)
        {
            Write(lists);
        }
    }

    public static bool RememberThumbnail(string id, string pageUrl, string? thumbnail)
    {
        var picture = PlaylistEntry.CleanThumbnail(thumbnail);
        var page = CleanPath(pageUrl);
        if (picture is null || page is null)
        {
            return false;
        }

        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        var entry = list?.Videos.FirstOrDefault(item => item.Resolve && SamePath(item.Location, page));
        if (entry is null || string.Equals(entry.Thumbnail, picture, StringComparison.Ordinal))
        {
            return false;
        }

        entry.Thumbnail = picture;
        Write(lists);
        return true;
    }

    public static bool ReplaceVideos(string id, IReadOnlyList<PlaylistEntry> videos)
    {
        var lists = Read();
        var list = lists.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (list is null)
        {
            return false;
        }

        list.Videos = videos.Select(item => item.Copy()).ToList();
        Write(lists);
        return true;
    }

    private static string? CleanName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 80)
        {
            return null;
        }

        return trimmed;
    }

    private static string? CleanPath(string? path)
    {
        var trimmed = path?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static bool SamePath(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static List<Playlist> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var lists = JsonSerializer.Deserialize<List<Playlist>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
            foreach (var list in lists)
            {
                list.Id ??= string.Empty;
                list.Name ??= string.Empty;
                list.Videos = (list.Videos ?? []).Where(item => !string.IsNullOrWhiteSpace(item.Location)).ToList();
            }

            return lists;
        }
        catch (IOException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void Write(List<Playlist> lists)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(lists, JsonOptions));
        PlaylistChanges.NoteSaved();
    }
}

internal sealed class Playlist
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public bool PlayNext { get; set; }

    public List<PlaylistEntry> Videos { get; set; } = [];

    public string WatchedSummary()
    {
        var total = Videos.Count;
        if (total == 0)
        {
            return "No videos";
        }

        var watched = Videos.Count(video => video.Watched);
        return $"{watched} of {total} watched";
    }
}

[JsonConverter(typeof(PlaylistEntryConverter))]
internal sealed class PlaylistEntry
{
    public string Location { get; set; } = string.Empty;

    public string? Title { get; set; }

    public bool Resolve { get; set; }

    public DateTimeOffset? AddedUtc { get; set; }

    public string? Thumbnail { get; set; }

    public bool Watched { get; set; }

    public bool IsPlayable => Resolve || System.IO.File.Exists(Location);

    public string? Note()
    {
        var place = Resolve ? "Online" : IsPlayable ? null : "Not on this PC";
        if (!Watched)
        {
            return place;
        }

        return place is null ? "Watched" : place + " · Watched";
    }

    internal PlaylistEntry Copy() => new()
    {
        Location = Location,
        Title = Title,
        Resolve = Resolve,
        AddedUtc = AddedUtc,
        Thumbnail = Thumbnail,
        Watched = Watched
    };

    public static string? CleanThumbnail(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 2048)
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    public bool Matches(string? query, Func<string, string?>? libraryName = null)
    {
        var term = query?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return true;
        }

        if (DisplayTitle(libraryName).Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !Resolve && Path.GetFileName(Location).Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public static PlaylistEntry ForFile(string path) => new() { Location = path };

    public static PlaylistEntry Page(string url, string? title)
    {
        var clean = title?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Equals("Opening…", StringComparison.OrdinalIgnoreCase) || clean.Equals("Video", StringComparison.OrdinalIgnoreCase))
        {
            clean = null;
        }
        else if (clean.Length > 120)
        {
            clean = clean[..120].Trim();
        }

        return new PlaylistEntry { Location = url, Title = clean, Resolve = true };
    }

    public string DisplayTitle(Func<string, string?>? libraryName = null)
    {
        if (Resolve)
        {
            return string.IsNullOrWhiteSpace(Title) ? Location : Title!;
        }

        var known = libraryName?.Invoke(Location);
        return string.IsNullOrWhiteSpace(known) ? Path.GetFileName(Location) : known!;
    }
}

internal sealed class PlaylistEntryConverter : JsonConverter<PlaylistEntry>
{
    public override PlaylistEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return PlaylistEntry.ForFile(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return PlaylistEntry.ForFile(string.Empty);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        string? path = null;
        string? url = null;
        string? title = null;
        bool? resolve = null;
        DateTimeOffset? added = null;
        string? thumbnail = null;
        var watched = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            if (!reader.Read())
            {
                break;
            }

            switch (name?.ToLowerInvariant())
            {
                case "path":
                    path = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case "url":
                    url = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case "title":
                    title = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case "resolve":
                    resolve = reader.TokenType != JsonTokenType.False && reader.TokenType != JsonTokenType.Null;
                    break;
                case "added":
                    added = ReadAdded(ref reader);
                    break;
                case "thumb":
                case "thumbnail":
                    thumbnail = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case "watched":
                    watched = reader.TokenType == JsonTokenType.True;
                    if (reader.TokenType is not JsonTokenType.True and not JsonTokenType.False)
                    {
                        reader.Skip();
                    }

                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        var page = resolve == true
            || (resolve is null && !string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(path));
        if (page && !string.IsNullOrWhiteSpace(url))
        {
            var entry = PlaylistEntry.Page(url, title);
            entry.AddedUtc = added;
            entry.Thumbnail = PlaylistEntry.CleanThumbnail(thumbnail);
            entry.Watched = watched;
            return entry;
        }

        var file = PlaylistEntry.ForFile(path ?? url ?? string.Empty);
        file.AddedUtc = added;
        file.Watched = watched;
        return file;
    }

    private static DateTimeOffset? ReadAdded(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }

        var text = reader.GetString();
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)
            ? stamp
            : null;
    }

    public override void Write(Utf8JsonWriter writer, PlaylistEntry value, JsonSerializerOptions options)
    {
        if (!value.Resolve && value.AddedUtc is null && !value.Watched)
        {
            writer.WriteStringValue(value.Location);
            return;
        }

        writer.WriteStartObject();
        if (value.Resolve)
        {
            writer.WriteString("Url", value.Location);
            if (!string.IsNullOrWhiteSpace(value.Title))
            {
                writer.WriteString("Title", value.Title);
            }

            writer.WriteBoolean("Resolve", true);
            var thumbnail = PlaylistEntry.CleanThumbnail(value.Thumbnail);
            if (thumbnail is not null)
            {
                writer.WriteString("Thumb", thumbnail);
            }
        }
        else
        {
            writer.WriteString("Path", value.Location);
        }

        if (value.AddedUtc is DateTimeOffset added)
        {
            writer.WriteString("Added", added.ToString("O", CultureInfo.InvariantCulture));
        }

        if (value.Watched)
        {
            writer.WriteBoolean("Watched", true);
        }

        writer.WriteEndObject();
    }
}
