namespace PersonalMediaPlayer.App.Download;

internal static class DownloadQueueHub
{
    public static event EventHandler<DownloadQueueItem>? Enqueued;

    public static bool Enqueue(string title, string pageUrl, DownloadQuality quality, DownloadSubtitle? subtitle)
    {
        var items = DownloadQueueStore.Load().ToList();
        if (items.Any(item => string.Equals(item.Url, pageUrl, StringComparison.OrdinalIgnoreCase)
            && item.Status is "Queued" or "Downloading" or "Paused" or "Ready"))
        {
            return false;
        }

        var item = new DownloadQueueItem(title, pageUrl, quality, subtitle);
        items.Add(item);
        DownloadQueueStore.Save(items);
        Enqueued?.Invoke(null, item);
        return true;
    }
}
