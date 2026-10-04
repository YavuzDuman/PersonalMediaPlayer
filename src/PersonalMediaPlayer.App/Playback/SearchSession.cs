namespace PersonalMediaPlayer.App.Playback;

internal static class SearchSession
{
    internal const string Home = "home";

    internal const string Library = "library";

    internal const string LibraryAlbum = "library-album";

    internal const string SavedWords = "saved-words";

    internal const string OpenPlaylist = "playlists-open";

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    internal readonly record struct Entry(string Text, double Offset);

    internal static string Playlist(string id) => "playlist:" + id;

    internal static string Words(string key) => "words:" + key.ToLowerInvariant();

    internal static Entry Recall(string key)
        => Entries.TryGetValue(key, out var entry) ? entry : new Entry(string.Empty, 0);

    internal static void Remember(string key, string? text, double offset)
    {
        var value = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
        if (value.Length == 0 && offset <= 0)
        {
            Entries.Remove(key);
            return;
        }

        Entries[key] = new Entry(value, offset < 0 ? 0 : offset);
    }

    internal static void Remember(string key, string? text, double offset, bool placeReady)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Remember(key, string.Empty, 0);
            return;
        }

        if (!placeReady && offset <= 0)
        {
            var saved = Recall(key);
            if (string.Equals(saved.Text, text, StringComparison.Ordinal))
            {
                offset = saved.Offset;
            }
        }

        Remember(key, text, offset);
    }
}
