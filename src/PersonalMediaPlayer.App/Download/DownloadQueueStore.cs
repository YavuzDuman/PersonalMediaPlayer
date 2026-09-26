using System.Text.Json;

namespace PersonalMediaPlayer.App.Download;

internal static class DownloadQueueStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "download-queue.json");

    public static IReadOnlyList<DownloadQueueItem> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var entries = JsonSerializer.Deserialize<List<StoredItem>>(File.ReadAllText(FilePath)) ?? [];
            var items = new List<DownloadQueueItem>();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Url) || string.IsNullOrWhiteSpace(entry.Format) || entry.Status is "Cancelled" or "Saved")
                {
                    continue;
                }

                var ready = entry.Status == "Ready" && !string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath);
                var item = new DownloadQueueItem(entry.Title ?? "Video", entry.Url, new DownloadQuality(entry.QualityLabel ?? "Video", entry.Format, entry.AudioOnly));
                item.Restore(ready ? "Ready" : "Paused", entry.Progress, ready ? entry.FilePath : null, entry.OutputPath);
                items.Add(item);
            }

            return items;
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<DownloadQueueItem> items)
    {
        try
        {
            var stored = items
                .Where(item => item.Status is not ("Cancelled" or "Saved"))
                .Select(item => new StoredItem
                {
                    Title = item.Title,
                    Url = item.Url,
                    QualityLabel = item.Quality.Label,
                    Format = item.Quality.Format,
                    AudioOnly = item.Quality.AudioOnly,
                    Status = item.Status is "Ready" ? "Ready" : "Paused",
                    Progress = item.Progress,
                    FilePath = item.Status == "Ready" ? item.FilePath : null,
                    OutputPath = item.OutputPath
                })
                .ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(stored));
        }
        catch (Exception)
        {
            // Losing one snapshot should not stop the download that is already running.
        }
    }

    private sealed class StoredItem
    {
        public string? Title { get; set; }

        public string? Url { get; set; }

        public string? QualityLabel { get; set; }

        public string? Format { get; set; }

        public bool AudioOnly { get; set; }

        public string? Status { get; set; }

        public double Progress { get; set; }

        public string? FilePath { get; set; }

        public string? OutputPath { get; set; }
    }
}
