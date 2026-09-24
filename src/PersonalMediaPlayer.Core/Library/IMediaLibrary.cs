using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.Core.Library;

public interface IMediaLibrary
{
    string LibraryRoot { get; }

    IReadOnlyList<MediaItem> GetImages();

    IReadOnlyList<MediaItem> GetItems();

    Task<IReadOnlyList<MediaItem>> GetImagesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaItem>> GetItemsAsync(CancellationToken cancellationToken = default);

    MediaItem ImportImage(string sourcePath);

    MediaItem SaveScreenshot(Stream content, string fileName);

    MediaItem ImportImage(Stream content, string originalFileName);

    MediaItem ImportMedia(string sourcePath, string? folderName = null);

    MediaItem ImportMedia(Stream content, string originalFileName, string? folderName = null);

    IReadOnlyList<MediaItem> GetItems(string? folderName);

    Task<IReadOnlyList<MediaItem>> GetItemsAsync(string? folderName, CancellationToken cancellationToken = default);

    IReadOnlyList<LibraryFolder> GetFolders();

    string CreateFolder(string name);

    string RenameFolder(string currentName, string newName);

    void DeleteFolder(string name);

    void DeleteItems(IEnumerable<string> filePaths);

    IReadOnlyList<MediaItem> GetDeletedItems();

    void RestoreDeleted(IEnumerable<string> filePaths);

    void PurgeDeleted(IEnumerable<string> filePaths);

    void RemoveItemsFromFolder(IEnumerable<string> filePaths, string folderName);

    void MoveItems(IEnumerable<string> filePaths, string? folderName);

    void MoveItemsTo(IEnumerable<string> filePaths, string? sourceFolder, string destinationFolder);

    void AddToFolder(string filePath, string folderName);

    MediaItem RenameItem(string filePath, string newName);

    MediaItem? GetById(string id);

    void PreserveOriginal(string imagePath);

    bool HasOriginal(string filePath);

    string? TryGetOriginalPath(string filePath);

    void RestoreOriginal(string filePath);

    MediaItem SaveEditedAsNew(Stream content, string fileName);

    MediaItem OverwriteEdited(string existingPath, Stream content, string fileName);
}
