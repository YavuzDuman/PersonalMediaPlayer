using System.Security.Cryptography;
using System.Text.Json;
using PersonalMediaPlayer.Core;
using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.Core.Storage;

public sealed class FileLibraryStore : ILibraryStore
{
    public FileLibraryStore(string libraryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        LibraryRoot = Path.GetFullPath(libraryRoot);
        ImagesDirectory = Path.Combine(LibraryRoot, "Images");
        VideosDirectory = Path.Combine(LibraryRoot, "Videos");
        OriginalsDirectory = Path.Combine(LibraryRoot, "Originals");
        FoldersDirectory = Path.Combine(LibraryRoot, "Folders");
        ScreenshotsDirectory = Path.Combine(LibraryRoot, "Screenshots");
        RecentlyDeletedDirectory = Path.Combine(LibraryRoot, "RecentlyDeleted");
        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(VideosDirectory);
        Directory.CreateDirectory(OriginalsDirectory);
        Directory.CreateDirectory(FoldersDirectory);
        Directory.CreateDirectory(ScreenshotsDirectory);
        Directory.CreateDirectory(RecentlyDeletedDirectory);
        MigratePhysicalFolders();
        EnsureRecordingsFolder();
        PurgeExpiredDeleted();
    }

    public string LibraryRoot { get; }

    public string ImagesDirectory { get; }

    public string VideosDirectory { get; }

    public string OriginalsDirectory { get; }

    public string FoldersDirectory { get; }

    public string ScreenshotsDirectory { get; }

    public string RecentlyDeletedDirectory { get; }

    public string ImportImage(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected file no longer exists.", sourcePath);
        }

        if (!MediaFileTypes.IsImage(sourcePath))
        {
            throw new NotSupportedException("The selected file is not a supported image type.");
        }

        var destination = GetAvailablePath(Path.GetFileName(sourcePath), ImagesDirectory);
        File.Copy(sourcePath, destination);
        return destination;
    }

    public string ImportImage(Stream content, string originalFileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        if (!MediaFileTypes.IsImage(originalFileName))
        {
            throw new NotSupportedException("The selected file is not a supported image type.");
        }

        var destination = GetAvailablePath(originalFileName, ImagesDirectory);
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        content.CopyTo(file);
        return destination;
    }

    public string ImportMedia(string sourcePath, string? folderName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected file no longer exists.", sourcePath);
        }

        var destination = GetAvailablePath(Path.GetFileName(sourcePath), DirectoryFor(sourcePath));
        File.Copy(sourcePath, destination);
        if (CanAddImportedFileTo(folderName))
        {
            AddToFolder(destination, folderName!);
        }

        return destination;
    }

    public string ImportMedia(Stream content, string originalFileName, string? folderName = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);

        var destination = GetAvailablePath(originalFileName, DirectoryFor(originalFileName));
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        content.CopyTo(file);
        if (CanAddImportedFileTo(folderName))
        {
            AddToFolder(destination, folderName!);
        }

        return destination;
    }

    public IReadOnlyList<string> EnumerateImageFiles()
        => EnumerateFiles(ImagesDirectory, MediaFileTypes.ImageExtensions)
            .OrderByDescending(File.GetCreationTimeUtc)
            .ToArray();

    public IReadOnlyList<string> EnumerateMediaFiles(string? folderName = null)
    {
        IEnumerable<string> files;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            files = EnumerateFiles(ImagesDirectory, AllMediaExtensions)
                .Concat(EnumerateFiles(VideosDirectory, AllMediaExtensions))
                .Concat(EnumerateFiles(ScreenshotsDirectory, MediaFileTypes.ImageExtensions));
        }
        else if (IsScreenshotsFolder(folderName))
        {
            files = EnumerateFiles(ScreenshotsDirectory, MediaFileTypes.ImageExtensions);
        }
        else
        {
            files = ReadMembers(folderName)
                .Select(ResolveLibraryPath)
                .Where(File.Exists);
        }

        return files.OrderByDescending(File.GetCreationTimeUtc).ToArray();
    }

    public IReadOnlyList<string> EnumerateFolderNames()
    {
        Directory.CreateDirectory(FoldersDirectory);
        return Directory.EnumerateFiles(FoldersDirectory, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name) && !IsReservedFolder(name!))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public string CreateFolder(string name)
    {
        var folderName = NormalizeFolderName(name);
        if (IsReservedFolder(folderName))
        {
            throw new InvalidOperationException($"'{folderName}' is reserved and cannot be used as a folder name.");
        }

        var path = GetFolderIndexPath(folderName);
        if (File.Exists(path))
        {
            throw new InvalidOperationException($"A folder named '{folderName}' already exists.");
        }

        WriteMembers(folderName, []);
        return folderName;
    }

    public string RenameFolder(string currentName, string newName)
    {
        var sourceName = NormalizeFolderName(currentName);
        var destinationName = NormalizeFolderName(newName);
        if (IsReservedFolder(sourceName) || IsReservedFolder(destinationName))
        {
            throw new InvalidOperationException("That folder name is reserved.");
        }

        var source = GetFolderIndexPath(sourceName);
        var destination = GetFolderIndexPath(destinationName);
        if (!File.Exists(source))
        {
            throw new DirectoryNotFoundException($"The folder '{currentName}' does not exist.");
        }

        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return destinationName;
        }

        if (File.Exists(destination))
        {
            throw new InvalidOperationException($"A folder named '{destinationName}' already exists.");
        }

        File.Move(source, destination);
        return destinationName;
    }

    public void DeleteFolder(string name)
    {
        if (IsReservedFolder(name))
        {
            throw new InvalidOperationException("That folder cannot be deleted.");
        }

        var path = GetFolderIndexPath(NormalizeFolderName(name));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteMedia(string filePath)
    {
        var full = EnsureLibraryFile(filePath);
        if (IsInRecentlyDeleted(full))
        {
            PurgeDeleted(full);
            return;
        }

        var record = new DeletedRecord
        {
            RelativePath = RelativeId(full),
            FileName = Path.GetFileName(full),
            DeletedUtc = DateTimeOffset.UtcNow,
            Folders = FoldersContaining(full)
        };
        var bin = Path.Combine(RecentlyDeletedDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bin);
        var stored = Path.Combine(bin, record.FileName);
        var original = OriginalBackupPath(full);
        if (File.Exists(original))
        {
            record.OriginalFile = BackupName(record.FileName, Path.GetExtension(original));
            File.Move(original, Path.Combine(bin, record.OriginalFile));
        }

        File.Move(full, stored);
        File.WriteAllText(Path.Combine(bin, "item.json"), JsonSerializer.Serialize(record));
        RemoveFromAllFolders(full);
        RemoveRecordingMembership(full);
    }

    public IReadOnlyList<string> GetDeletedFiles()
    {
        PurgeExpiredDeleted();
        if (!Directory.Exists(RecentlyDeletedDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(RecentlyDeletedDirectory)
            .Select(dir => StoredMedia(dir))
            .Where(path => path is not null)
            .Cast<string>()
            .ToArray();
    }

    public void RestoreDeleted(string filePath)
    {
        var bin = BinFor(filePath);
        var record = ReadDeleted(bin) ?? throw new FileNotFoundException("This deleted item is no longer available.", filePath);
        var stored = StoredMedia(bin) ?? throw new FileNotFoundException("This deleted item is no longer available.", filePath);
        var destination = ResolveLibraryPath(record.RelativePath);
        destination = File.Exists(destination)
            ? GetAvailablePath(Path.GetFileName(destination), Path.GetDirectoryName(destination)!)
            : destination;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(stored, destination);
        if (!string.IsNullOrWhiteSpace(record.OriginalFile))
        {
            var original = Path.Combine(bin, record.OriginalFile);
            if (File.Exists(original))
            {
                var backup = NestedOriginalPath(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                if (!File.Exists(backup))
                {
                    File.Move(original, backup);
                }
            }
        }

        foreach (var folder in record.Folders)
        {
            try
            {
                AddToFolder(destination, folder);
            }
            catch (InvalidOperationException)
            {
                // The album was removed while the file was deleted.
            }
        }

        Directory.Delete(bin, recursive: true);
    }

    public void PurgeDeleted(string filePath)
    {
        var bin = BinFor(filePath);
        if (Directory.Exists(bin))
        {
            Directory.Delete(bin, recursive: true);
        }
    }

    public void AddToFolder(string filePath, string folderName)
    {
        if (IsScreenshotsFolder(folderName))
        {
            throw new InvalidOperationException("Items cannot be added to Screenshots. Use Capture.");
        }

        if (IsUnfiledFolder(folderName))
        {
            throw new InvalidOperationException("Unfiled is a view of items that are not in a folder.");
        }

        if (IsSmartFolder(folderName))
        {
            throw new InvalidOperationException("Photos, Videos, and This month fill themselves. Add the item to one of your folders instead.");
        }

        var full = EnsureLibraryFile(filePath);
        var id = RelativeId(full);
        var members = ReadMembers(folderName);
        if (members.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        members.Add(id);
        WriteMembers(folderName, members);
    }

    public void RemoveFromFolder(string filePath, string folderName)
    {
        var full = EnsureLibraryFile(filePath);
        if (IsScreenshotsFolder(folderName) || IsRecordingsFolder(folderName))
        {
            throw new InvalidOperationException("Items stay in that folder. Delete the file to remove it.");
        }

        if (IsUnfiledFolder(folderName))
        {
            throw new InvalidOperationException("Unfiled is a view of items that are not in a folder.");
        }

        if (IsSmartFolder(folderName))
        {
            throw new InvalidOperationException("Photos, Videos, and This month fill themselves. Delete the file to remove it from the library.");
        }

        var id = RelativeId(full);
        var members = ReadMembers(folderName);
        var next = members.Where(member => !member.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        WriteMembers(folderName, next);
    }

    public string MoveMedia(string filePath, string? folderName)
    {
        var full = EnsureLibraryFile(filePath);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            RemoveFromAllFolders(full);
            return full;
        }

        if (IsScreenshotsFolder(folderName))
        {
            throw new InvalidOperationException("Items cannot be added to Screenshots. Use Capture.");
        }

        AddToFolder(full, folderName);
        return full;
    }

    public void MoveMediaTo(string filePath, string? sourceFolder, string destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            throw new ArgumentException("Choose a folder.", nameof(destinationFolder));
        }

        if (IsScreenshotsFolder(destinationFolder))
        {
            throw new InvalidOperationException("Items cannot be added to Screenshots. Use Capture.");
        }

        if (IsUnfiledFolder(destinationFolder))
        {
            throw new InvalidOperationException("Unfiled is a view of items that are not in a folder.");
        }

        if (IsRecordingsFolder(destinationFolder))
        {
            throw new InvalidOperationException("New recordings are saved into Recordings. Add this item to another folder instead.");
        }

        if (IsSmartFolder(destinationFolder))
        {
            throw new InvalidOperationException("Photos, Videos, and This month fill themselves. Add the item to one of your folders instead.");
        }

        var full = EnsureLibraryFile(filePath);
        var source = string.IsNullOrWhiteSpace(sourceFolder) ? null : sourceFolder.Trim();
        var leavingOne = source is not null && !IsReservedFolder(source);
        if (leavingOne && source!.Equals(destinationFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (leavingOne)
        {
            RemoveFromFolder(full, source!);
        }
        else
        {
            RemoveFromAllFolders(full);
        }

        AddToFolder(full, destinationFolder);
    }

    public string RenameMedia(string filePath, string newName)
    {
        var full = EnsureLibraryFile(filePath);
        var directory = Path.GetDirectoryName(full) ?? ImagesDirectory;
        var extension = Path.GetExtension(full);
        var stem = Path.GetFileNameWithoutExtension(newName.Trim());
        if (string.IsNullOrWhiteSpace(stem) || stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The file name is not valid.", nameof(newName));
        }

        var requested = NormalizeFileName(stem + extension);
        var destination = Path.Combine(directory, requested);
        var oldId = RelativeId(full);
        if (string.Equals(destination, full, StringComparison.Ordinal))
        {
            return full;
        }

        var originalSource = OriginalBackupPath(full);
        if (string.Equals(destination, full, StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(directory, $".{Guid.NewGuid():N}{extension}");
            File.Move(full, temp);
            File.Move(temp, destination);
        }
        else
        {
            destination = GetAvailablePath(requested, directory);
            File.Move(full, destination);
        }

        if (File.Exists(originalSource))
        {
            var originalDestination = NestedOriginalPath(destination);
            if (!string.Equals(originalSource, originalDestination, StringComparison.OrdinalIgnoreCase)
                && !File.Exists(originalDestination))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(originalDestination)!);
                File.Move(originalSource, originalDestination);
            }
        }

        ReplaceMemberId(oldId, RelativeId(destination));
        return destination;
    }

    public string SaveScreenshot(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        Directory.CreateDirectory(ScreenshotsDirectory);
        var destination = GetAvailablePath(fileName, ScreenshotsDirectory);
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        content.CopyTo(file);
        return destination;
    }

    public void PreserveOriginal(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("The image to preserve no longer exists.", imagePath);
        }

        var backupPath = OriginalBackupPath(Path.GetFullPath(imagePath));
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        if (!File.Exists(backupPath))
        {
            File.Copy(imagePath, backupPath);
        }
    }

    public bool HasOriginal(string filePath) => TryGetOriginalPath(filePath) is not null;

    public string? TryGetOriginalPath(string filePath)
    {
        try
        {
            var full = EnsureLibraryFile(filePath);
            var backup = OriginalBackupPath(full);
            return File.Exists(backup) ? backup : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void RestoreOriginal(string filePath)
    {
        var full = EnsureLibraryFile(filePath);
        var backup = OriginalBackupPath(full);
        if (!File.Exists(backup))
        {
            throw new FileNotFoundException("This item has no saved original.", full);
        }

        File.Copy(backup, full, overwrite: true);
    }

    public string SaveNewImage(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        var destination = GetAvailablePath(fileName, ImagesDirectory);
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        content.CopyTo(file);
        return destination;
    }

    public string ReplaceImage(string existingPath, Stream content, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingPath);
        ArgumentNullException.ThrowIfNull(content);
        if (!File.Exists(existingPath))
        {
            throw new FileNotFoundException("The image to replace no longer exists.", existingPath);
        }

        PreserveOriginal(existingPath);

        var requestedName = NormalizeFileName(fileName);
        var existingFull = Path.GetFullPath(existingPath);
        string destination;
        if (string.Equals(requestedName, Path.GetFileName(existingFull), StringComparison.OrdinalIgnoreCase))
        {
            destination = existingFull;
        }
        else
        {
            destination = GetAvailablePath(requestedName, ImagesDirectory);
        }

        var tempPath = Path.Combine(ImagesDirectory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                content.CopyTo(file);
            }

            File.Copy(tempPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        if (!string.Equals(destination, existingFull, StringComparison.OrdinalIgnoreCase)
            && File.Exists(existingFull))
        {
            File.Delete(existingFull);
        }

        return destination;
    }

    private string GetAvailablePath(string originalFileName, string directory)
    {
        var fileName = NormalizeFileName(originalFileName);
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, fileName);
        if (!File.Exists(destination))
        {
            return destination;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 1; ; i++)
        {
            destination = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!File.Exists(destination))
            {
                return destination;
            }
        }
    }

    private static readonly HashSet<string> AllMediaExtensions = new(MediaFileTypes.ImageExtensions.Concat(MediaFileTypes.VideoExtensions), StringComparer.OrdinalIgnoreCase);

    private string DirectoryFor(string fileName)
        => MediaFileTypes.IsVideo(fileName) ? VideosDirectory : ImagesDirectory;

    private static bool IsScreenshotsFolder(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name.Equals(LibraryFolder.Screenshots, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnfiledFolder(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name.Equals(LibraryFolder.Unfiled, StringComparison.OrdinalIgnoreCase);

    private static bool IsRecordingsFolder(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name.Equals(LibraryFolder.Recordings, StringComparison.OrdinalIgnoreCase);

    private static bool CanAddImportedFileTo(string? folderName)
        => !string.IsNullOrWhiteSpace(folderName)
           && !IsScreenshotsFolder(folderName)
           && !IsUnfiledFolder(folderName)
           && !IsSmartFolder(folderName);

    private void EnsureRecordingsFolder()
    {
        var path = GetFolderIndexPath(LibraryFolder.Recordings);
        if (!File.Exists(path))
        {
            WriteMembers(LibraryFolder.Recordings, []);
        }
    }

    private static bool IsSmartFolder(string? name)
        => LibraryFolder.IsSmartName(name);

    private static bool IsReservedFolder(string? name)
        => IsScreenshotsFolder(name) || IsUnfiledFolder(name) || IsRecordingsFolder(name) || IsSmartFolder(name)
           || string.Equals(name, LibraryFolder.RecentlyDeleted, StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, LibraryFolder.Favorites, StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, LibraryFolder.Duplicates, StringComparison.OrdinalIgnoreCase);

    private bool IsInRecentlyDeleted(string fullPath)
        => fullPath.StartsWith(Path.GetFullPath(RecentlyDeletedDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private List<string> FoldersContaining(string fullPath)
    {
        var id = RelativeId(fullPath);
        var names = new List<string>();
        foreach (var name in EnumerateFolderNames().Append(LibraryFolder.Recordings))
        {
            if (ReadMembers(name).Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private void PurgeExpiredDeleted()
    {
        if (!Directory.Exists(RecentlyDeletedDirectory))
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        foreach (var dir in Directory.EnumerateDirectories(RecentlyDeletedDirectory))
        {
            var record = ReadDeleted(dir);
            if (record is null || record.DeletedUtc < cutoff)
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static string? StoredMedia(string bin)
    {
        var record = ReadDeleted(bin);
        if (!string.IsNullOrWhiteSpace(record?.FileName))
        {
            var named = Path.Combine(bin, record.FileName);
            return File.Exists(named) ? named : null;
        }

        return Directory.EnumerateFiles(bin)
            .FirstOrDefault(path => !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), record?.OriginalFile, StringComparison.OrdinalIgnoreCase));
    }

    private static string BackupName(string mediaFileName, string extension)
    {
        var name = ".backup" + extension;
        if (!string.Equals(name, mediaFileName, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return ".backup-copy" + extension;
    }

    private string BinFor(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        if (!IsInRecentlyDeleted(full))
        {
            throw new FileNotFoundException("This file is not in Recently deleted.", filePath);
        }

        return Path.GetDirectoryName(full) ?? throw new FileNotFoundException("This file is not in Recently deleted.", filePath);
    }

    private static DeletedRecord? ReadDeleted(string bin)
    {
        var path = Path.Combine(bin, "item.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeletedRecord>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private sealed class DeletedRecord
    {
        public string RelativePath { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public DateTimeOffset DeletedUtc { get; set; }

        public List<string> Folders { get; set; } = [];

        public string? OriginalFile { get; set; }
    }

    private string GetFolderIndexPath(string folderName)
        => Path.Combine(FoldersDirectory, NormalizeFolderName(folderName) + ".json");

    private string RelativeId(string fullPath)
        => Path.GetRelativePath(LibraryRoot, fullPath).Replace('\\', '/');

    private string NestedOriginalPath(string fullPath)
    {
        var relative = RelativeId(fullPath).Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(OriginalsDirectory, relative));
        var root = Path.GetFullPath(OriginalsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The original path is outside the library.");
        }

        return combined;
    }

    private string OriginalBackupPath(string fullPath)
    {
        var nested = NestedOriginalPath(fullPath);
        if (File.Exists(nested))
        {
            if (ConflictsWithAnotherFile(fullPath, nested))
            {
                Quarantine(nested);
            }

            return nested;
        }

        var legacy = Path.Combine(OriginalsDirectory, Path.GetFileName(fullPath));
        if (!File.Exists(legacy) || !LegacyOriginalBelongsTo(fullPath) || ConflictsWithAnotherFile(fullPath, legacy))
        {
            return nested;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.Move(legacy, nested);
        return nested;
    }

    private void Quarantine(string backupPath)
    {
        var unclaimed = Path.Combine(OriginalsDirectory, ".unclaimed");
        Directory.CreateDirectory(unclaimed);
        File.Move(backupPath, GetAvailablePath(Path.GetFileName(backupPath), unclaimed));
    }

    private bool ConflictsWithAnotherFile(string fullPath, string backupPath)
    {
        var backup = new FileInfo(backupPath);
        if (!backup.Exists)
        {
            return false;
        }

        byte[]? backupHash = null;
        foreach (var path in EnumerateMediaFiles(null))
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var other = new FileInfo(path);
            if (!other.Exists || other.Length != backup.Length)
            {
                continue;
            }

            backupHash ??= SHA256.HashData(File.ReadAllBytes(backupPath));
            if (SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(backupHash))
            {
                return true;
            }
        }

        return false;
    }

    private bool LegacyOriginalBelongsTo(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        string? match = null;
        foreach (var path in EnumerateMediaFiles(null))
        {
            if (!string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null)
            {
                return false;
            }

            match = path;
        }

        return match is not null
            && string.Equals(Path.GetFullPath(match), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveLibraryPath(string relativeId)
        => Path.GetFullPath(Path.Combine(LibraryRoot, relativeId.Replace('/', Path.DirectorySeparatorChar)));

    private List<string> ReadMembers(string folderName)
    {
        var path = GetFolderIndexPath(folderName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteMembers(string folderName, IEnumerable<string> members)
    {
        Directory.CreateDirectory(FoldersDirectory);
        var unique = members
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        File.WriteAllText(GetFolderIndexPath(folderName), JsonSerializer.Serialize(unique));
    }

    private void ReplaceMemberId(string oldId, string newId)
    {
        if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var name in EnumerateFolderNames().Append(LibraryFolder.Recordings))
        {
            var members = ReadMembers(name);
            var changed = false;
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i].Equals(oldId, StringComparison.OrdinalIgnoreCase))
                {
                    members[i] = newId;
                    changed = true;
                }
            }

            if (changed)
            {
                WriteMembers(name, members);
            }
        }
    }

    private void RemoveRecordingMembership(string fullPath)
    {
        var id = RelativeId(fullPath);
        var members = ReadMembers(LibraryFolder.Recordings);
        var next = members.Where(member => !member.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (next.Length != members.Count)
        {
            WriteMembers(LibraryFolder.Recordings, next);
        }
    }

    private void RemoveFromAllFolders(string filePath)
    {
        var id = RelativeId(filePath);
        foreach (var name in EnumerateFolderNames().Append(LibraryFolder.Recordings))
        {
            if (IsRecordingsFolder(name))
            {
                continue;
            }

            var members = ReadMembers(name);
            var next = members.Where(member => !member.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (next.Length != members.Count)
            {
                WriteMembers(name, next);
            }
        }
    }

    private void MigratePhysicalFolders()
    {
        Directory.CreateDirectory(FoldersDirectory);
        foreach (var directory in Directory.GetDirectories(FoldersDirectory))
        {
            var name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var members = ReadMembers(name);
            foreach (var file in EnumerateFiles(directory, AllMediaExtensions).ToArray())
            {
                var destination = GetAvailablePath(Path.GetFileName(file), DirectoryFor(file));
                File.Move(file, destination);
                members.Add(RelativeId(destination));
            }

            WriteMembers(name, members);
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private string EnsureLibraryFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var full = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(LibraryRoot);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new FileNotFoundException("The media file is not in the library.", filePath);
        }

        return full;
    }

    private static string NormalizeFolderName(string name)
    {
        var folderName = name.Trim();
        if (string.IsNullOrWhiteSpace(folderName)
            || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || folderName is "." or "..")
        {
            throw new ArgumentException("The folder name is not valid.", nameof(name));
        }

        return folderName;
    }

    private static IEnumerable<string> EnumerateFiles(string directory, HashSet<string> extensions)
    {
        Directory.CreateDirectory(directory);
        return Directory.EnumerateFiles(directory)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !name.StartsWith('.')
                    && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    && extensions.Contains(Path.GetExtension(path));
            });
    }

    private static string NormalizeFileName(string originalFileName)
    {
        var fileName = Path.GetFileName(originalFileName.Trim());
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The file name is not valid.", nameof(originalFileName));
        }

        var extension = Path.GetExtension(fileName);
        if (!MediaFileTypes.IsImage(fileName) && !MediaFileTypes.IsVideo(fileName))
        {
            throw new NotSupportedException($"'{extension}' is not a supported media type.");
        }

        return fileName;
    }
}
