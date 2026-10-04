using System.IO.Compression;
using System.Text;
using PersonalMediaPlayer.App.Storage;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class AppDataBackupTests
{
    [Fact]
    public void ExportLeavesMediaOutAndRestorePutsTheSavedDataBack()
    {
        using var root = new TempFolder();
        WriteSample(root.Path);
        var media = Path.Combine(root.Path, "Library", "Videos", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        File.WriteAllBytes(media, Encoding.UTF8.GetBytes("NOT-A-BACKUP-MEDIA"));
        File.WriteAllText(Path.Combine(root.Path, "download-queue.json"), "NOT-A-BACKUP-QUEUE");

        var backup = Path.Combine(root.Path, "evening.pmpbackup");
        AppDataBackup.Export(root.Path, backup);
        using (var zip = ZipFile.OpenRead(backup))
        {
            var names = zip.Entries.Select(entry => entry.FullName).ToArray();
            Assert.Equal(13, names.Length);
            Assert.Contains("manifest.json", names);
            Assert.Contains("playlists.json", names);
            Assert.Contains("links.json", names);
            Assert.Contains("saved-words.json", names);
            Assert.Contains("playback-bookmarks.json", names);
            Assert.Contains("playback-positions.json", names);
            Assert.Contains("favorites.json", names);
            Assert.DoesNotContain(names, name => name.Contains("mp4", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(names, name => name.Contains("download", StringComparison.OrdinalIgnoreCase));
        }

        var preview = AppDataBackup.Read(backup);
        Assert.Contains("This replaces playlists, saved words, bookmarks, watched marks, playback positions, favorites, library links, and settings on this PC.", preview.Confirmation);
        Assert.Contains("Playlists: 1 playlist, 3 videos, 2 marked watched", preview.Confirmation);
        Assert.Contains("Saved words: 2", preview.Confirmation);
        Assert.Contains("Bookmarks: 2", preview.Confirmation);
        Assert.Contains("Playback positions: 1", preview.Confirmation);
        Assert.Contains("Favorites: 2", preview.Confirmation);
        Assert.Contains("Library links: 1", preview.Confirmation);
        Assert.Contains("Dark theme", preview.Confirmation);
        Assert.Contains("Turkish audio", preview.Confirmation);
        Assert.Contains("subtitles off", preview.Confirmation);
        Assert.Contains("cursor hidden", preview.Confirmation);
        Assert.Contains("capture delay 3 seconds", preview.Confirmation);
        Assert.Contains("system audio on", preview.Confirmation);
        Assert.Contains("volume 40", preview.Confirmation);
        Assert.Contains("Videos, photos, albums, recordings, and downloads stay where they are.", preview.Confirmation);
        Assert.Contains("Saved data that is not in this backup is removed.", preview.Confirmation);

        File.WriteAllText(Path.Combine(root.Path, "playlists.json"), "[]");
        File.WriteAllText(Path.Combine(root.Path, "favorites.json"), "[\"C:\\\\only-here.mp4\"]");
        File.Delete(Path.Combine(root.Path, "theme.txt"));

        preview.Apply(root.Path);

        Assert.True(File.Exists(Path.Combine(root.Path, "recovery.pmpbackup")));
        var playlists = File.ReadAllText(Path.Combine(root.Path, "playlists.json"));
        Assert.Contains("\"Watched\":true", playlists);
        Assert.Contains("https://vimeo.com/1", playlists);
        Assert.Equal("[\"C:\\\\a.mp4\",\"C:\\\\b.mp4\"]", File.ReadAllText(Path.Combine(root.Path, "favorites.json")));
        Assert.Equal("Dark", File.ReadAllText(Path.Combine(root.Path, "theme.txt")));
        Assert.Equal("NOT-A-BACKUP-MEDIA", File.ReadAllText(media));
        Assert.Equal("NOT-A-BACKUP-QUEUE", File.ReadAllText(Path.Combine(root.Path, "download-queue.json")));
        Assert.Contains("C:\\\\outside\\\\a.mp4", File.ReadAllText(Path.Combine(root.Path, "Library", "links.json")));
    }

    [Fact]
    public void RestoreClearsDataTheBackupDoesNotHave()
    {
        using var source = new TempFolder();
        using var destination = new TempFolder();
        File.WriteAllText(Path.Combine(source.Path, "theme.txt"), "Light");
        File.WriteAllText(Path.Combine(destination.Path, "playlists.json"), "[{\"Id\":\"keep\",\"Name\":\"Keep\",\"Videos\":[]}]");
        File.WriteAllText(Path.Combine(destination.Path, "favorites.json"), "[\"C:\\\\keep.mp4\"]");

        var backup = Path.Combine(source.Path, "light.pmpbackup");
        AppDataBackup.Export(source.Path, backup);
        var preview = AppDataBackup.Read(backup);
        Assert.Contains("Playlists: none", preview.Confirmation);
        Assert.Contains("Favorites: none", preview.Confirmation);
        Assert.Contains("Light theme", preview.Confirmation);

        preview.Apply(destination.Path);

        Assert.False(File.Exists(Path.Combine(destination.Path, "playlists.json")));
        Assert.False(File.Exists(Path.Combine(destination.Path, "favorites.json")));
        Assert.Equal("Light", File.ReadAllText(Path.Combine(destination.Path, "theme.txt")));
    }

    [Fact]
    public void AFailedRestorePutsThePreviousDataBack()
    {
        using var source = new TempFolder();
        using var destination = new TempFolder();
        WriteSample(source.Path);
        File.WriteAllText(Path.Combine(destination.Path, "playlists.json"), "[{\"Id\":\"keep\",\"Name\":\"Keep\",\"Videos\":[]}]");
        File.WriteAllText(Path.Combine(destination.Path, "favorites.json"), "[\"C:\\\\keep.mp4\"]");
        File.WriteAllText(Path.Combine(destination.Path, "Library"), "not a directory");

        var backup = Path.Combine(source.Path, "full.pmpbackup");
        AppDataBackup.Export(source.Path, backup);
        var error = Assert.Throws<IOException>(() => AppDataBackup.Read(backup).Apply(destination.Path));
        Assert.Equal("Could not restore that backup.", error.Message);
        Assert.Contains("\"Name\":\"Keep\"", File.ReadAllText(Path.Combine(destination.Path, "playlists.json")));
        Assert.Equal("[\"C:\\\\keep.mp4\"]", File.ReadAllText(Path.Combine(destination.Path, "favorites.json")));
        Assert.Equal("not a directory", File.ReadAllText(Path.Combine(destination.Path, "Library")));
        Assert.Empty(Directory.EnumerateFiles(destination.Path, "*.pmpbackup-tmp", SearchOption.AllDirectories));
        using var recovery = ZipFile.OpenRead(Path.Combine(destination.Path, "recovery.pmpbackup"));
        var saved = recovery.GetEntry("playlists.json");
        Assert.NotNull(saved);
        using var reader = new StreamReader(saved.Open());
        Assert.Contains("Keep", reader.ReadToEnd());
    }

    [Fact]
    public void ExportReplacesTheDestinationOnlyAfterTheFileIsComplete()
    {
        using var root = new TempFolder();
        WriteSample(root.Path);
        var backup = Path.Combine(root.Path, "evening.pmpbackup");
        File.WriteAllText(backup, "OLD-BACKUP");

        AppDataBackup.Export(root.Path, backup);

        Assert.NotEqual("OLD-BACKUP", File.ReadAllText(backup));
        using var zip = ZipFile.OpenRead(backup);
        Assert.Contains(zip.Entries, entry => entry.FullName == "manifest.json");
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*.pmpbackup-tmp"));
    }

    [Fact]
    public void AFailedExportLeavesTheExistingBackupInPlace()
    {
        using var root = new TempFolder();
        WriteSample(root.Path);
        File.WriteAllText(Path.Combine(root.Path, "playlists.json"), "[1]");
        var backup = Path.Combine(root.Path, "evening.pmpbackup");
        File.WriteAllText(backup, "OLD-BACKUP");

        var error = Assert.Throws<IOException>(() => AppDataBackup.Export(root.Path, backup));

        Assert.Equal("Could not save the backup.", error.Message);
        Assert.Equal("OLD-BACKUP", File.ReadAllText(backup));
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*.pmpbackup-tmp"));
    }

    [Fact]
    public void AFailedExportRemovesAnEmptyDestination()
    {
        using var root = new TempFolder();
        WriteSample(root.Path);
        File.WriteAllText(Path.Combine(root.Path, "playlists.json"), "[1]");
        var backup = Path.Combine(root.Path, "evening.pmpbackup");
        File.WriteAllBytes(backup, []);

        Assert.Throws<IOException>(() => AppDataBackup.Export(root.Path, backup));

        Assert.False(File.Exists(backup));
        Assert.Empty(Directory.EnumerateFiles(root.Path, "*.pmpbackup-tmp"));
    }

    [Fact]
    public void ABackupWithABadRecordIsRefused()
    {
        using var root = new TempFolder();
        WriteSample(root.Path);
        var cases = new (string Name, string Contents)[]
        {
            ("playlists.json", "[1]"),
            ("saved-words.json", "[\"hello\"]"),
            ("favorites.json", "[\" \"]"),
            ("links.json", "[\"C:\\\\a.mp4\"]"),
            ("playback-bookmarks.json", "{\"C:\\\\a.mp4\":1}"),
            ("playback-positions.json", "{\"C:\\\\a.mp4\":\"later\"}"),
            ("stream-languages.json", "{\"Audio\":\"not-a-language\",\"Captions\":\"off\"}"),
            ("playback-volume.json", "{}")
        };

        foreach (var item in cases)
        {
            var backup = Path.Combine(root.Path, item.Name + ".pmpbackup");
            AppDataBackup.Export(root.Path, backup);
            ReplaceEntry(backup, item.Name, item.Contents);
            var error = Assert.Throws<InvalidDataException>(() => AppDataBackup.Read(backup));
            Assert.Equal("This backup file is not one this app can read.", error.Message);
        }
    }

    [Fact]
    public void AFailedRecoveryIsReported()
    {
        using var source = new TempFolder();
        using var destination = new TempFolder();
        WriteSample(source.Path);
        var playlists = Path.Combine(destination.Path, "playlists.json");
        var theme = Path.Combine(destination.Path, "theme.txt");
        File.WriteAllText(playlists, """[{"Id":"keep","Name":"Keep","Videos":[]}]""");
        File.WriteAllText(Path.Combine(destination.Path, "favorites.json"), """["C:\\keep.mp4"]""");
        File.WriteAllText(theme, "Dark");
        File.SetAttributes(theme, FileAttributes.ReadOnly);
        try
        {
            var backup = Path.Combine(source.Path, "full.pmpbackup");
            AppDataBackup.Export(source.Path, backup);
            var error = Assert.Throws<IOException>(() => AppDataBackup.Read(backup).Apply(destination.Path));
            Assert.Equal("Could not put the previous data back. A recovery copy is still saved in this app's folder.", error.Message);
            Assert.Contains("\"Name\":\"Keep\"", File.ReadAllText(playlists));
            Assert.Equal("Dark", File.ReadAllText(theme));
            Assert.True(File.Exists(Path.Combine(destination.Path, "recovery.pmpbackup")));
        }
        finally
        {
            File.SetAttributes(theme, FileAttributes.Normal);
        }
    }

    [Fact]
    public void AnUnreadableFileIsRefused()
    {
        using var root = new TempFolder();
        var notes = Path.Combine(root.Path, "notes.pmpbackup");
        File.WriteAllText(notes, "this is not a backup");
        var error = Assert.Throws<InvalidDataException>(() => AppDataBackup.Read(notes));
        Assert.Equal("This backup file is not one this app can read.", error.Message);

        var newer = Path.Combine(root.Path, "newer.pmpbackup");
        WriteZip(newer, """{"format":2,"app":"PersonalMediaPlayer","items":[]}""");
        var newerError = Assert.Throws<InvalidDataException>(() => AppDataBackup.Read(newer));
        Assert.Equal("This backup is from a newer version of the app.", newerError.Message);

        var extra = Path.Combine(root.Path, "extra.pmpbackup");
        WriteSample(root.Path);
        AppDataBackup.Export(root.Path, extra);
        using (var zip = ZipFile.Open(extra, ZipArchiveMode.Update))
        {
            var entry = zip.CreateEntry("clip.mp4");
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes("media"));
        }

        var extraError = Assert.Throws<InvalidDataException>(() => AppDataBackup.Read(extra));
        Assert.Equal("This backup file is not one this app can read.", extraError.Message);
    }

    private static void WriteSample(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Library"));
        File.WriteAllText(Path.Combine(root, "playlists.json"), """
            [{"Id":"abc","Name":"Evening","Videos":["C:\\clips\\a.mp4",{"Path":"C:\\clips\\b.mp4","Watched":true},{"Url":"https://vimeo.com/1","Title":"Zoo","Resolve":true,"Watched":true}]}]
            """);
        File.WriteAllText(Path.Combine(root, "saved-words.json"), """[{},{}]""");
        File.WriteAllText(Path.Combine(root, "playback-bookmarks.json"), """{"C:\\a.mp4":[{"TimeMs":1},{"TimeMs":2}]}""");
        File.WriteAllText(Path.Combine(root, "playback-positions.json"), """{"C:\\a.mp4":{"time":1000,"duration":5000}}""");
        File.WriteAllText(Path.Combine(root, "favorites.json"), """["C:\\a.mp4","C:\\b.mp4"]""");
        File.WriteAllText(Path.Combine(root, "Library", "links.json"), """[{"Path":"C:\\outside\\a.mp4","AddedUtc":"2026-01-01T00:00:00Z"}]""");
        File.WriteAllText(Path.Combine(root, "theme.txt"), "Dark");
        File.WriteAllText(Path.Combine(root, "stream-languages.json"), """{"Audio":"tr","Captions":"off"}""");
        File.WriteAllText(Path.Combine(root, "include-cursor.txt"), "false");
        File.WriteAllText(Path.Combine(root, "capture-delay.txt"), "3");
        File.WriteAllText(Path.Combine(root, "include-system-audio.txt"), "true");
        File.WriteAllText(Path.Combine(root, "playback-volume.json"), """{"Level":40,"Audible":80}""");
    }

    private static void ReplaceEntry(string backup, string name, string contents)
    {
        using var zip = ZipFile.Open(backup, ZipArchiveMode.Update);
        zip.GetEntry(name)?.Delete();
        var created = zip.CreateEntry(name);
        using var writer = new StreamWriter(created.Open());
        writer.Write(contents);
    }

    private static void WriteZip(string path, string manifest)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("manifest.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(manifest);
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pmp-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
