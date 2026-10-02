using System.Text.Json;

namespace PersonalMediaPlayer.App.Playback;

internal static class MediaFavorites
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "favorites.json");

    internal static string? StoreOverride { get; set; }

    private static string FilePath => StoreOverride ?? DefaultFilePath;

    public static event EventHandler? Changed;

    public static bool Contains(string filePath)
        => Read().Contains(filePath);

    public static bool Toggle(string filePath)
    {
        var items = Read();
        var added = !items.Remove(filePath);
        if (added)
        {
            items.Add(filePath);
        }

        Write(items);
        Changed?.Invoke(null, EventArgs.Empty);
        return added;
    }

    public static int AddMany(IEnumerable<string> paths)
    {
        var items = Read();
        var added = 0;
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && items.Add(path))
            {
                added++;
            }
        }

        if (added == 0)
        {
            return 0;
        }

        Write(items);
        Changed?.Invoke(null, EventArgs.Empty);
        return added;
    }

    public static int RemoveMany(IEnumerable<string> paths)
    {
        var items = Read();
        var removed = 0;
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && items.Remove(path))
            {
                removed++;
            }
        }

        if (removed == 0)
        {
            return 0;
        }

        Write(items);
        Changed?.Invoke(null, EventArgs.Empty);
        return removed;
    }

    public static void Move(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath)
            || string.IsNullOrWhiteSpace(newPath)
            || string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var items = Read();
        if (!items.Remove(oldPath))
        {
            return;
        }

        items.Add(newPath);
        Write(items);
    }

    public static void Remove(string filePath)
    {
        var items = Read();
        if (!items.Remove(filePath))
        {
            return;
        }

        Write(items);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static HashSet<string> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var parsed = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [];
            return new HashSet<string>(
                parsed.Where(path => !string.IsNullOrWhiteSpace(path)),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Write(HashSet<string> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items));
        }
        catch
        {
            // A missed favorite should not stop the library.
        }
    }
}
