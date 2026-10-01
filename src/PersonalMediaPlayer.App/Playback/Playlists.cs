using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalMediaPlayer.App.Playback;

internal static class Playlists
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "playlists.json");

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
        foreach (var path in paths)
        {
            var clean = CleanPath(path);
            if (clean is null || list.Videos.Any(existing => SamePath(existing, clean)))
            {
                continue;
            }

            list.Videos.Add(clean);
            added++;
        }

        if (added > 0)
        {
            Write(lists);
        }

        return added;
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
                if (!SamePath(list.Videos[i], from))
                {
                    continue;
                }

                list.Videos[i] = to;
                changed = true;
            }
        }

        if (changed)
        {
            Write(lists);
        }
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
                list.Videos = (list.Videos ?? []).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
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
    }
}

internal sealed class Playlist
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public bool PlayNext { get; set; }

    public List<string> Videos { get; set; } = [];
}
