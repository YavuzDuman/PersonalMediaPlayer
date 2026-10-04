using PersonalMediaPlayer.Core;
using PersonalMediaPlayer.Core.Models;
using PersonalMediaPlayer.Core.Storage;

namespace PersonalMediaPlayer.Core.Library;

public sealed class MediaLibrary : IMediaLibrary
{
    private readonly ILibraryStore _store;

    public MediaLibrary(ILibraryStore store)
    {
        _store = store;
    }

    public string LibraryRoot => _store.LibraryRoot;

    public void ReloadLinks() => _store.ReloadLinks();

    public IReadOnlyList<MediaItem> GetImages()
        => _store.EnumerateImageFiles().Select(ToItem).ToArray();

    public IReadOnlyList<MediaItem> GetItems()
        => GetItems(null);

    public IReadOnlyList<MediaItem> GetItems(string? folderName)
    {
        if (string.Equals(folderName, LibraryFolder.Unfiled, StringComparison.OrdinalIgnoreCase))
        {
            return GetUnfiled();
        }

        if (string.Equals(folderName, LibraryFolder.Photos, StringComparison.OrdinalIgnoreCase))
        {
            return AllItems().Where(item => !item.IsVideo).ToArray();
        }

        if (string.Equals(folderName, LibraryFolder.Videos, StringComparison.OrdinalIgnoreCase))
        {
            return AllItems().Where(item => item.IsVideo).ToArray();
        }

        if (string.Equals(folderName, LibraryFolder.ThisMonth, StringComparison.OrdinalIgnoreCase))
        {
            return AllItems().Where(item => LibraryFolder.IsInThisMonth(item.ImportedAt)).ToArray();
        }

        if (string.Equals(folderName, LibraryFolder.RecentlyDeleted, StringComparison.OrdinalIgnoreCase))
        {
            return GetDeletedItems();
        }

        return _store.EnumerateMediaFiles(folderName).Select(ToItem).ToArray();
    }

    private IReadOnlyList<MediaItem> AllItems()
        => _store.EnumerateMediaFiles(null).Select(ToItem).ToArray();

    private IReadOnlyList<MediaItem> GetUnfiled()
    {
        var filed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _store.EnumerateFolderNames().Append(LibraryFolder.Recordings))
        {
            foreach (var path in _store.EnumerateMediaFiles(name))
            {
                filed.Add(Path.GetFullPath(path));
            }
        }

        var screenshots = Path.GetFullPath(_store.ScreenshotsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return _store.EnumerateMediaFiles(null)
            .Select(Path.GetFullPath)
            .Where(path => !filed.Contains(path) && !path.StartsWith(screenshots, StringComparison.OrdinalIgnoreCase))
            .Select(ToItem)
            .ToArray();
    }

    public Task<IReadOnlyList<MediaItem>> GetImagesAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetImages, cancellationToken);

    public Task<IReadOnlyList<MediaItem>> GetItemsAsync(CancellationToken cancellationToken = default)
        => GetItemsAsync(null, cancellationToken);

    public Task<IReadOnlyList<MediaItem>> GetItemsAsync(string? folderName, CancellationToken cancellationToken = default)
        => Task.Run(() => GetItems(folderName), cancellationToken);

    public MediaItem SaveScreenshot(Stream content, string fileName)
        => ToItem(_store.SaveScreenshot(content, fileName));

    public MediaItem ImportImage(string sourcePath)
        => ToItem(_store.ImportImage(sourcePath));

    public MediaItem ImportImage(Stream content, string originalFileName)
        => ToItem(_store.ImportImage(content, originalFileName));

    public MediaItem ImportMedia(string sourcePath, string? folderName = null)
        => ToItem(_store.ImportMedia(sourcePath, folderName));

    public MediaItem ImportMedia(Stream content, string originalFileName, string? folderName = null)
        => ToItem(_store.ImportMedia(content, originalFileName, folderName));

    public MediaItem LinkMedia(string sourcePath, string? folderName = null)
        => ToItem(_store.LinkMedia(sourcePath, folderName));

    public IReadOnlyList<string> ConnectedFolders() => _store.ConnectedFolders();

    public ConnectFolderResult ConnectFolder(string path) => _store.ConnectFolder(path);

    public bool DisconnectFolder(string path) => _store.DisconnectFolder(path);

    public ConnectedFolderScan RefreshConnectedFolders() => _store.RefreshConnectedFolders();

    public MediaItem RelocateLink(string currentPath, string newPath)
        => ToItem(_store.RelocateLink(currentPath, newPath));

    public IReadOnlyList<LibraryFolder> GetFolders()
    {
        var screenshots = Describe(LibraryFolder.Screenshots, _store.EnumerateMediaFiles(LibraryFolder.Screenshots));
        var recordings = Describe(LibraryFolder.Recordings, _store.EnumerateMediaFiles(LibraryFolder.Recordings));
        var userFolders = _store.EnumerateFolderNames()
            .Select(name => Describe(name, _store.EnumerateMediaFiles(name)));
        return new[] { screenshots, recordings }.Concat(userFolders).ToArray();
    }

    private static LibraryFolder Describe(string name, IReadOnlyList<string> filesNewestFirst)
    {
        var cover = filesNewestFirst.Count == 0 ? null : filesNewestFirst[0];
        return new LibraryFolder
        {
            Name = name,
            FileCount = filesNewestFirst.Count,
            CoverPath = cover,
            CoverIsVideo = cover is not null && MediaFileTypes.IsVideo(cover)
        };
    }

    public string CreateFolder(string name) => _store.CreateFolder(name);

    public string RenameFolder(string currentName, string newName) => _store.RenameFolder(currentName, newName);

    public void DeleteFolder(string name) => _store.DeleteFolder(name);

    public void DeleteItems(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _store.DeleteMedia(path);
        }
    }

    public IReadOnlyList<MediaItem> GetDeletedItems()
        => _store.GetDeletedFiles().Select(ToItem).ToArray();

    public void RestoreDeleted(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _store.RestoreDeleted(path);
        }
    }

    public void PurgeDeleted(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _store.PurgeDeleted(path);
        }
    }

    public void RemoveItemsFromFolder(IEnumerable<string> filePaths, string folderName)
    {
        foreach (var path in filePaths)
        {
            _store.RemoveFromFolder(path, folderName);
        }
    }

    public void MoveItems(IEnumerable<string> filePaths, string? folderName)
    {
        foreach (var path in filePaths)
        {
            _store.MoveMedia(path, folderName);
        }
    }

    public void MoveItemsTo(IEnumerable<string> filePaths, string? sourceFolder, string destinationFolder)
    {
        foreach (var path in filePaths)
        {
            _store.MoveMediaTo(path, sourceFolder, destinationFolder);
        }
    }

    public void AddToFolder(string filePath, string folderName) => _store.AddToFolder(filePath, folderName);

    public MediaItem RenameItem(string filePath, string newName)
        => ToItem(_store.RenameMedia(filePath, newName));

    public MediaItem? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (Path.IsPathRooted(id))
        {
            var rooted = Path.GetFullPath(id);
            if (_store.IsLinked(rooted))
            {
                return ToItem(rooted);
            }

            var libraryRoot = Path.GetFullPath(_store.LibraryRoot);
            if (rooted.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(rooted))
            {
                return ToItem(rooted);
            }

            return null;
        }

        var root = Path.GetFullPath(_store.LibraryRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, id.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return null;
        }

        return ToItem(fullPath);
    }

    public void PreserveOriginal(string imagePath) => _store.PreserveOriginal(imagePath);

    public bool HasOriginal(string filePath) => _store.HasOriginal(filePath);

    public string? TryGetOriginalPath(string filePath) => _store.TryGetOriginalPath(filePath);

    public void RestoreOriginal(string filePath) => _store.RestoreOriginal(filePath);

    public MediaItem SaveEditedAsNew(Stream content, string fileName)
        => ToItem(_store.SaveNewImage(content, fileName));

    public MediaItem OverwriteEdited(string existingPath, Stream content, string fileName)
        => ToItem(_store.ReplaceImage(existingPath, content, fileName));

    private MediaItem ToItem(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var linked = _store.IsLinked(full);
        var exists = File.Exists(full);
        if (!exists && !linked)
        {
            throw new FileNotFoundException("The media file is not in the library.", filePath);
        }

        long size = 0;
        DateTimeOffset imported;
        if (exists)
        {
            var info = new FileInfo(full);
            full = info.FullName;
            size = info.Exists ? info.Length : 0;
            var created = new DateTimeOffset(DateTime.SpecifyKind(info.CreationTimeUtc, DateTimeKind.Utc));
            imported = linked ? _store.LinkAddedAt(full) ?? created : created;
        }
        else
        {
            imported = _store.LinkAddedAt(full) ?? DateTimeOffset.UtcNow;
        }

        return new MediaItem
        {
            Id = linked ? full : Path.GetRelativePath(_store.LibraryRoot, full).Replace('\\', '/'),
            Kind = MediaFileTypes.GetKind(full),
            DisplayName = Path.GetFileName(full),
            FilePath = full,
            ImportedAt = imported,
            FileSizeBytes = size,
            FolderName = FolderNameOf(full),
            IsLinked = linked
        };
    }

    private string FolderNameOf(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var names = new List<string>();
        if (full.StartsWith(Path.GetFullPath(_store.ScreenshotsDirectory), StringComparison.OrdinalIgnoreCase))
        {
            names.Add(LibraryFolder.Screenshots);
        }

        names.AddRange(_store.EnumerateFolderNames()
            .Where(name => _store.EnumerateMediaFiles(name).Any(path =>
                string.Equals(Path.GetFullPath(path), full, StringComparison.OrdinalIgnoreCase))));
        return string.Join(", ", names);
    }
}
