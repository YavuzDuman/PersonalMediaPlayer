using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PersonalMediaPlayer.App.Playback;

namespace PersonalMediaPlayer.App.Storage;

internal static class AppDataBackup
{
    private const int FormatVersion = 1;
    private const int MaxFileBytes = 4 * 1024 * 1024;
    private const string AppName = "PersonalMediaPlayer";
    private const string UnreadableMessage = "This backup file is not one this app can read.";
    private const string NewerMessage = "This backup is from a newer version of the app.";
    private const string RestoreFailedMessage = "Could not restore that backup.";
    private const string RecoveryFailedMessage = "Could not put the previous data back. A recovery copy is still saved in this app's folder.";
    private const string RecoveryName = "recovery.pmpbackup";

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly BackupFile[] Files =
    [
        new("playlists.json", "playlists.json", BackupKind.Playlists),
        new("saved-words.json", "saved-words.json", BackupKind.SavedWords),
        new("playback-bookmarks.json", "playback-bookmarks.json", BackupKind.Bookmarks),
        new("playback-positions.json", "playback-positions.json", BackupKind.Positions),
        new("favorites.json", "favorites.json", BackupKind.Favorites),
        new("links.json", Path.Combine("Library", "links.json"), BackupKind.Links),
        new("theme.txt", "theme.txt", BackupKind.Theme),
        new("stream-languages.json", "stream-languages.json", BackupKind.Languages),
        new("include-cursor.txt", "include-cursor.txt", BackupKind.Cursor),
        new("capture-delay.txt", "capture-delay.txt", BackupKind.Delay),
        new("include-system-audio.txt", "include-system-audio.txt", BackupKind.SystemAudio),
        new("playback-volume.json", "playback-volume.json", BackupKind.Volume)
    ];

    internal static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer");

    internal static void Export(string dataRoot, string destinationPath)
    {
        string? temporary = null;
        var removeEmptyDestination = false;
        try
        {
            removeEmptyDestination = File.Exists(destinationPath) && new FileInfo(destinationPath).Length == 0;
            var manifest = new ManifestDocument
            {
                Format = FormatVersion,
                App = AppName,
                ExportedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in Files)
            {
                var path = InsideRoot(dataRoot, file.RelativePath);
                var included = File.Exists(path);
                manifest.Items.Add(new ManifestEntry { Name = file.Name, Included = included });
                if (!included)
                {
                    continue;
                }

                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > MaxFileBytes || !Valid(file.Kind, bytes))
                {
                    throw new IOException("Could not save the backup.");
                }

                payload[file.Name] = bytes;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            temporary = TemporaryBeside(destinationPath);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
                WriteEntry(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestOptions));
                foreach (var pair in payload)
                {
                    WriteEntry(zip, pair.Key, pair.Value);
                }
            }

            File.Move(temporary, destinationPath, overwrite: true);
            temporary = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (temporary is not null)
            {
                TryDelete(temporary);
            }

            if (removeEmptyDestination)
            {
                TryDeleteIfEmpty(destinationPath);
            }

            throw new IOException("Could not save the backup.", ex);
        }
    }

    internal static BackupPreview Read(string backupPath)
    {
        try
        {
            using var stream = File.OpenRead(backupPath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            return ReadArchive(zip);
        }
        catch (InvalidDataException ex) when (ex.Message is UnreadableMessage or NewerMessage)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(UnreadableMessage);
        }
        catch (IOException)
        {
            throw new IOException("Could not read that backup.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new IOException("Could not read that backup.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException(UnreadableMessage);
        }
    }

    private static BackupPreview ReadArchive(ZipArchive zip)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)
                || entry.FullName != entry.Name
                || !entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException(UnreadableMessage);
            }
        }

        if (!entries.Remove("manifest.json", out var manifestEntry))
        {
            throw new InvalidDataException(UnreadableMessage);
        }

        var manifest = JsonSerializer.Deserialize<ManifestDocument>(ReadEntry(manifestEntry), ManifestOptions);
        if (manifest is null || !string.Equals(manifest.App, AppName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(UnreadableMessage);
        }

        if (manifest.Format > FormatVersion)
        {
            throw new InvalidDataException(NewerMessage);
        }

        if (manifest.Format != FormatVersion || manifest.Items is not { Count: var itemCount } || itemCount != Files.Length)
        {
            throw new InvalidDataException(UnreadableMessage);
        }

        var included = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var item in manifest.Items)
        {
            if (string.IsNullOrEmpty(item.Name) || !included.TryAdd(item.Name, item.Included))
            {
                throw new InvalidDataException(UnreadableMessage);
            }
        }

        var files = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var file in Files)
        {
            if (!included.Remove(file.Name, out var present))
            {
                throw new InvalidDataException(UnreadableMessage);
            }

            var hasEntry = entries.Remove(file.Name, out var entry);
            if (present != hasEntry || (entry is not null && entry.Length == 0))
            {
                throw new InvalidDataException(UnreadableMessage);
            }

            var bytes = entry is null ? null : ReadEntry(entry);
            if (bytes is not null && !Valid(file.Kind, bytes))
            {
                throw new InvalidDataException(UnreadableMessage);
            }

            files[file.Name] = bytes;
        }

        if (included.Count > 0 || entries.Count > 0)
        {
            throw new InvalidDataException(UnreadableMessage);
        }

        return new BackupPreview(files, Describe(files));
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(bytes);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxFileBytes)
        {
            throw new InvalidDataException(UnreadableMessage);
        }

        using var entryStream = entry.Open();
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxFileBytes)
            {
                throw new InvalidDataException(UnreadableMessage);
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static string Describe(IReadOnlyDictionary<string, byte[]?> files)
    {
        var playlists = PlaylistCounts(files["playlists.json"]);
        var lines = new List<string>
        {
            "This replaces playlists, saved words, bookmarks, watched marks, playback positions, favorites, library links, and settings on this PC.",
            string.Empty,
            PlaylistLine(playlists),
            CountLine("Saved words", CountArray(files["saved-words.json"]), "saved word", "saved words"),
            CountLine("Bookmarks", CountBookmarkMarks(files["playback-bookmarks.json"]), "bookmark", "bookmarks"),
            CountLine("Playback positions", CountObject(files["playback-positions.json"]), "playback position", "playback positions"),
            CountLine("Favorites", CountArray(files["favorites.json"]), "favorite", "favorites"),
            CountLine("Library links", CountObjects(files["links.json"]), "library link", "library links"),
            "Settings: " + SettingsLine(files),
            string.Empty,
            "Videos, photos, albums, recordings, and downloads stay where they are.",
            "Saved data that is not in this backup is removed."
        };
        return string.Join(Environment.NewLine, lines);
    }

    private static string PlaylistLine((int Lists, int Videos, int Watched) counts)
    {
        if (counts.Lists == 0)
        {
            return "Playlists: none";
        }

        var lists = counts.Lists == 1 ? "1 playlist" : $"{counts.Lists} playlists";
        var videos = counts.Videos == 1 ? "1 video" : $"{counts.Videos} videos";
        var watched = counts.Watched == 0
            ? "none marked watched"
            : counts.Watched == 1 ? "1 marked watched" : $"{counts.Watched} marked watched";
        return $"Playlists: {lists}, {videos}, {watched}";
    }

    private static string CountLine(string label, int count, string singular, string plural)
    {
        var value = count == 0 ? "none" : count == 1 ? "1 " + singular : $"{count} {plural}";
        return $"{label}: {value}";
    }

    private static string SettingsLine(IReadOnlyDictionary<string, byte[]?> files)
    {
        var theme = files["theme.txt"] is byte[] themeBytes ? Text(themeBytes) : "System";
        var themeLabel = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? "Light theme"
            : theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Dark theme" : "Windows theme";
        var (audio, captions) = LanguageChoices(files["stream-languages.json"]);
        var cursor = Flag(files["include-cursor.txt"]) ? "cursor in screenshots" : "cursor hidden";
        var delay = DelaySeconds(files["capture-delay.txt"]);
        var delayLabel = delay == 0 ? "no capture delay" : $"capture delay {delay} seconds";
        var speakers = Flag(files["include-system-audio.txt"]) ? "system audio on" : "system audio off";
        return string.Join(", ",
        [
            themeLabel,
            audio,
            captions,
            cursor,
            delayLabel,
            speakers,
            "volume " + VolumeLevel(files["playback-volume.json"]).ToString(CultureInfo.InvariantCulture)
        ]);
    }

    private static bool Valid(BackupKind kind, byte[] bytes)
    {
        try
        {
            switch (kind)
            {
                case BackupKind.Playlists:
                    return ValidPlaylists(bytes);
                case BackupKind.SavedWords:
                    return ValidSavedWords(bytes);
                case BackupKind.Favorites:
                    return ValidFavorites(bytes);
                case BackupKind.Links:
                    return ValidLinks(bytes);
                case BackupKind.Bookmarks:
                    return ValidBookmarks(bytes);
                case BackupKind.Positions:
                    return ValidPositions(bytes);
                case BackupKind.Languages:
                    return ValidLanguages(bytes);
                case BackupKind.Volume:
                    return ValidVolume(bytes);
                case BackupKind.Theme:
                    return Text(bytes).ToLowerInvariant() is "system" or "light" or "dark";
                case BackupKind.Cursor:
                case BackupKind.SystemAudio:
                    return bool.TryParse(Text(bytes), out _);
                case BackupKind.Delay:
                    return int.TryParse(Text(bytes), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                        && seconds is 0 or 3 or 5 or 10;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ValidPlaylists(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var list in document.RootElement.EnumerateArray())
        {
            if (list.ValueKind != JsonValueKind.Object
                || !OptionalText(list, "Id")
                || !OptionalText(list, "Name")
                || !OptionalText(list, "CreatedAt")
                || !OptionalBool(list, "PlayNext"))
            {
                return false;
            }

            if (Property(list, "Videos") is not JsonElement videos)
            {
                continue;
            }

            if (videos.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var video in videos.EnumerateArray())
            {
                if (!ValidPlaylistVideo(video))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidPlaylistVideo(JsonElement video)
    {
        if (video.ValueKind == JsonValueKind.String)
        {
            return !string.IsNullOrWhiteSpace(video.GetString());
        }

        if (video.ValueKind != JsonValueKind.Object
            || !OptionalText(video, "Title")
            || !OptionalText(video, "Thumb")
            || !OptionalText(video, "Thumbnail")
            || !OptionalText(video, "Added")
            || !OptionalBool(video, "Resolve")
            || !OptionalBool(video, "Watched")
            || !OptionalText(video, "Path")
            || !OptionalText(video, "Url"))
        {
            return false;
        }

        var path = Property(video, "Path");
        var url = Property(video, "Url");
        return NonEmptyText(path) || NonEmptyText(url);
    }

    private static bool ValidSavedWords(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var word in document.RootElement.EnumerateArray())
        {
            if (word.ValueKind != JsonValueKind.Object
                || !OptionalText(word, "English")
                || !OptionalText(word, "Turkish")
                || !OptionalText(word, "Sentence")
                || !OptionalText(word, "VideoPath")
                || !OptionalText(word, "PageUrl")
                || !OptionalText(word, "SourceName")
                || !OptionalText(word, "AudioLanguage")
                || !OptionalText(word, "CaptionLanguage")
                || !OptionalText(word, "SavedAt")
                || !OptionalBool(word, "ResolvePage")
                || !OptionalInteger(word, "TimeMs"))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidFavorites(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var favorite in document.RootElement.EnumerateArray())
        {
            if (favorite.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(favorite.GetString()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidLinks(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var link in document.RootElement.EnumerateArray())
        {
            if (link.ValueKind != JsonValueKind.Object || !NonEmptyText(Property(link, "Path")))
            {
                return false;
            }

            if (Property(link, "AddedUtc") is JsonElement added
                && (added.ValueKind != JsonValueKind.String
                    || !DateTimeOffset.TryParse(added.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidBookmarks(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var file in document.RootElement.EnumerateObject())
        {
            if (file.Value.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var mark in file.Value.EnumerateArray())
            {
                if (mark.ValueKind == JsonValueKind.Number)
                {
                    if (!mark.TryGetInt64(out _))
                    {
                        return false;
                    }

                    continue;
                }

                if (mark.ValueKind != JsonValueKind.Object
                    || Property(mark, "TimeMs") is not JsonElement time
                    || !time.TryGetInt64(out _)
                    || !OptionalText(mark, "Note"))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidPositions(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var file in document.RootElement.EnumerateObject())
        {
            var value = file.Value;
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (!value.TryGetInt64(out var time) || time <= 0)
                {
                    return false;
                }

                continue;
            }

            if (value.ValueKind != JsonValueKind.Object
                || Property(value, "time") is not JsonElement stored
                || !stored.TryGetInt64(out var position)
                || position <= 0
                || !OptionalInteger(value, "duration")
                || !OptionalText(value, "title")
                || !OptionalText(value, "thumbnail")
                || !OptionalText(value, "seen"))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidLanguages(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return StoredChoice(document.RootElement, "Audio") && StoredChoice(document.RootElement, "Captions");
    }

    private static bool ValidVolume(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || Property(document.RootElement, "Level") is not JsonElement level
            || !Finite(level))
        {
            return false;
        }

        return Property(document.RootElement, "Audible") is not JsonElement audible || Finite(audible);
    }

    private static bool StoredChoice(JsonElement element, string name)
        => Property(element, name) is JsonElement value
           && value.ValueKind == JsonValueKind.String
           && StreamLanguageSettings.IsStoredChoice(value.GetString());

    private static bool OptionalText(JsonElement element, string name)
        => Property(element, name) is not JsonElement value || value.ValueKind is JsonValueKind.String or JsonValueKind.Null;

    private static bool OptionalBool(JsonElement element, string name)
        => Property(element, name) is not JsonElement value || value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool OptionalInteger(JsonElement element, string name)
        => Property(element, name) is not JsonElement value
           || value.ValueKind == JsonValueKind.Null
           || (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _));

    private static bool NonEmptyText(JsonElement? element)
        => element is JsonElement value
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString());

    private static bool Finite(JsonElement element)
        => element.ValueKind == JsonValueKind.Number
           && element.TryGetDouble(out var value)
           && !double.IsNaN(value)
           && !double.IsInfinity(value);

    private static (int Lists, int Videos, int Watched) PlaylistCounts(byte[]? bytes)
    {
        if (bytes is null)
        {
            return (0, 0, 0);
        }

        using var document = JsonDocument.Parse(bytes);
        var lists = 0;
        var videos = 0;
        var watched = 0;
        foreach (var list in document.RootElement.EnumerateArray())
        {
            if (list.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            lists++;
            if (Property(list, "Videos") is not JsonElement items || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var video in items.EnumerateArray())
            {
                videos++;
                if (video.ValueKind == JsonValueKind.Object
                    && Property(video, "Watched") is JsonElement mark
                    && mark.ValueKind == JsonValueKind.True)
                {
                    watched++;
                }
            }
        }

        return (lists, videos, watched);
    }

    private static int CountArray(byte[]? bytes)
    {
        if (bytes is null)
        {
            return 0;
        }

        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.GetArrayLength();
    }

    private static int CountObjects(byte[]? bytes)
    {
        if (bytes is null)
        {
            return 0;
        }

        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.EnumerateArray().Count(item => item.ValueKind == JsonValueKind.Object);
    }

    private static int CountObject(byte[]? bytes)
    {
        if (bytes is null)
        {
            return 0;
        }

        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.EnumerateObject().Count();
    }

    private static int CountBookmarkMarks(byte[]? bytes)
    {
        if (bytes is null)
        {
            return 0;
        }

        using var document = JsonDocument.Parse(bytes);
        var count = 0;
        foreach (var file in document.RootElement.EnumerateObject())
        {
            if (file.Value.ValueKind == JsonValueKind.Array)
            {
                count += file.Value.GetArrayLength();
            }
        }

        return count;
    }

    private static (string Audio, string Captions) LanguageChoices(byte[]? bytes)
    {
        var audio = "original audio";
        var captions = "subtitles off";
        if (bytes is null)
        {
            return (audio, captions);
        }

        using var document = JsonDocument.Parse(bytes);
        if (Property(document.RootElement, "Audio") is JsonElement audioValue && audioValue.ValueKind == JsonValueKind.String)
        {
            audio = LanguageLabel(audioValue.GetString(), audio: true);
        }

        if (Property(document.RootElement, "Captions") is JsonElement captionValue && captionValue.ValueKind == JsonValueKind.String)
        {
            captions = LanguageLabel(captionValue.GetString(), audio: false);
        }

        return (audio, captions);
    }

    private static string LanguageLabel(string? code, bool audio)
    {
        if (string.IsNullOrWhiteSpace(code)
            || code.Equals(audio ? "original" : "off", StringComparison.OrdinalIgnoreCase))
        {
            return audio ? "original audio" : "subtitles off";
        }

        if (code.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return "audio off";
        }

        if (code.Equals("original", StringComparison.OrdinalIgnoreCase))
        {
            return "original subtitles";
        }

        try
        {
            var name = CultureInfo.GetCultureInfo(code).EnglishName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return audio ? name + " audio" : name + " subtitles";
            }
        }
        catch (CultureNotFoundException)
        {
        }

        return audio ? code + " audio" : code + " subtitles";
    }

    private static bool Flag(byte[]? bytes)
        => bytes is not null && bool.TryParse(Text(bytes), out var value) && value;

    private static int DelaySeconds(byte[]? bytes)
        => bytes is not null
           && int.TryParse(Text(bytes), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
           && seconds is 0 or 3 or 5 or 10
            ? seconds
            : 0;

    private static int VolumeLevel(byte[]? bytes)
    {
        if (bytes is null)
        {
            return 80;
        }

        using var document = JsonDocument.Parse(bytes);
        if (Property(document.RootElement, "Level") is JsonElement level
            && level.TryGetDouble(out var value)
            && !double.IsNaN(value)
            && !double.IsInfinity(value))
        {
            return (int)Math.Clamp(Math.Round(value), 0, 100);
        }

        return 80;
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string Text(byte[] bytes)
        => Encoding.UTF8.GetString(bytes).Trim().TrimStart('\uFEFF');

    private static string InsideRoot(string dataRoot, string relativePath)
    {
        var root = Path.GetFullPath(dataRoot);
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Could not save the backup.");
        }

        return path;
    }

    private enum BackupKind
    {
        Playlists,
        SavedWords,
        Bookmarks,
        Positions,
        Favorites,
        Links,
        Theme,
        Languages,
        Cursor,
        Delay,
        SystemAudio,
        Volume
    }

    private readonly record struct BackupFile(string Name, string RelativePath, BackupKind Kind);

    private sealed class ManifestDocument
    {
        public int Format { get; set; }

        public string? App { get; set; }

        public string? ExportedUtc { get; set; }

        public List<ManifestEntry> Items { get; set; } = [];
    }

    private sealed class ManifestEntry
    {
        public string? Name { get; set; }

        public bool Included { get; set; }
    }

    internal sealed class BackupPreview
    {
        private readonly Dictionary<string, byte[]?> _files;

        internal BackupPreview(Dictionary<string, byte[]?> files, string confirmation)
        {
            _files = files;
            Confirmation = confirmation;
        }

        internal string Confirmation { get; }

        internal void Apply(string dataRoot)
        {
            foreach (var file in Files)
            {
                if (_files[file.Name] is byte[] bytes && !Valid(file.Kind, bytes))
                {
                    throw new InvalidDataException(UnreadableMessage);
                }
            }

            var recoveryPath = Path.Combine(Path.GetFullPath(dataRoot), RecoveryName);
            try
            {
                WriteRecovery(dataRoot, recoveryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(RestoreFailedMessage, ex);
            }

            var temps = new List<string>();
            try
            {
                WriteFiles(dataRoot, _files, temps);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DeleteTemps(temps);
                try
                {
                    RestoreRecovery(dataRoot, recoveryPath);
                }
                catch (Exception recoveryEx) when (recoveryEx is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    throw new IOException(RecoveryFailedMessage, recoveryEx);
                }

                throw new IOException(RestoreFailedMessage, ex);
            }
        }
    }

    private static void WriteRecovery(string dataRoot, string recoveryPath)
        {
            var manifest = new ManifestDocument
            {
                Format = FormatVersion,
                App = AppName,
                ExportedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in Files)
            {
                var path = InsideRoot(dataRoot, file.RelativePath);
                var included = File.Exists(path);
                manifest.Items.Add(new ManifestEntry { Name = file.Name, Included = included });
                if (included)
                {
                    payload[file.Name] = File.ReadAllBytes(path);
                }
            }

            var directory = Path.GetDirectoryName(recoveryPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = TemporaryBeside(recoveryPath);
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
                    WriteEntry(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestOptions));
                    foreach (var pair in payload)
                    {
                        WriteEntry(zip, pair.Key, pair.Value);
                    }
                }

                File.Move(temporary, recoveryPath, overwrite: true);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }

        private static void RestoreRecovery(string dataRoot, string recoveryPath)
        {
            using var stream = File.OpenRead(recoveryPath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName != entry.Name || !entries.TryAdd(entry.FullName, entry))
                {
                    throw new InvalidDataException(UnreadableMessage);
                }
            }

            if (!entries.Remove("manifest.json", out var manifestEntry))
            {
                throw new InvalidDataException(UnreadableMessage);
            }

            var manifest = JsonSerializer.Deserialize<ManifestDocument>(ReadRaw(manifestEntry), ManifestOptions);
            if (manifest?.Items is null)
            {
                throw new InvalidDataException(UnreadableMessage);
            }

            var included = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var item in manifest.Items)
            {
                if (string.IsNullOrEmpty(item.Name) || !included.TryAdd(item.Name, item.Included))
                {
                    throw new InvalidDataException(UnreadableMessage);
                }
            }

            var files = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            foreach (var file in Files)
            {
                if (!included.Remove(file.Name, out var present))
                {
                    throw new InvalidDataException(UnreadableMessage);
                }

                if (!present)
                {
                    files[file.Name] = null;
                    continue;
                }

                if (!entries.Remove(file.Name, out var entry))
                {
                    throw new InvalidDataException(UnreadableMessage);
                }

                files[file.Name] = ReadRaw(entry);
            }

            var temps = new List<string>();
            try
            {
                WriteFiles(dataRoot, files, temps);
            }
            catch
            {
                DeleteTemps(temps);
                throw;
            }
        }

        private static void WriteFiles(string dataRoot, IReadOnlyDictionary<string, byte[]?> files, List<string> temps)
        {
            foreach (var file in Files)
            {
                var path = InsideRoot(dataRoot, file.RelativePath);
                var bytes = files[file.Name];
                if (bytes is null)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    continue;
                }

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temp = path + "." + Guid.NewGuid().ToString("N") + ".pmpbackup-tmp";
                File.WriteAllBytes(temp, bytes);
                temps.Add(temp);
                File.Move(temp, path, overwrite: true);
                temps.Remove(temp);
            }
        }

        private static byte[] ReadRaw(ZipArchiveEntry entry)
        {
            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            return memory.ToArray();
        }

        private static string TemporaryBeside(string destinationPath)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            var folder = string.IsNullOrEmpty(directory) ? Path.GetTempPath() : directory;
            return Path.Combine(folder, Path.GetFileName(destinationPath) + "." + Guid.NewGuid().ToString("N") + ".pmpbackup-tmp");
        }

        private static void DeleteTemps(List<string> temps)
        {
            foreach (var temp in temps)
            {
                TryDelete(temp);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void TryDeleteIfEmpty(string path)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length == 0)
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
}
