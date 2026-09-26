using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;

namespace PersonalMediaPlayer.App.Download;

public sealed class DownloadHistoryEntry
{
    public string Name { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Quality { get; init; } = string.Empty;

    public DateTimeOffset SavedAt { get; init; }

    public string Location { get; init; } = string.Empty;

    public string When => SavedAt.ToLocalTime().ToString("g");

    public string Detail => string.IsNullOrWhiteSpace(Quality) ? When : $"{Quality} · {When}";

    public string Folder => Path.GetDirectoryName(Location) ?? Location;

    public string? ThumbnailUrl
    {
        get
        {
            var id = VideoId(Url);
            return id is null ? null : "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg";
        }
    }

    public Visibility ThumbnailVisibility => ThumbnailUrl is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PlaceholderVisibility => ThumbnailUrl is null ? Visibility.Visible : Visibility.Collapsed;

    private static string? VideoId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = Regex.Match(url, @"(?:v=|youtu\.be/|shorts/|embed/|live/)([A-Za-z0-9_-]{11})");
        return match.Success ? match.Groups[1].Value : null;
    }
}

internal static class DownloadHistory
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "download-history.json");

    public static IReadOnlyList<DownloadHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<DownloadHistoryEntry>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<DownloadHistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries.ToList()));
        }
        catch (Exception)
        {
            // A missed history line should not undo a file that was already saved.
        }
    }

    public static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
    }
}
