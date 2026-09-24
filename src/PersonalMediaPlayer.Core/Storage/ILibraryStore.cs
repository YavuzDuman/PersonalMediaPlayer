namespace PersonalMediaPlayer.Core.Storage;

public interface ILibraryStore
{
    string LibraryRoot { get; }

    string ImagesDirectory { get; }

    string VideosDirectory { get; }

    string OriginalsDirectory { get; }

    string ScreenshotsDirectory { get; }

    string ImportImage(string sourcePath);

    string ImportImage(Stream content, string originalFileName);

    string ImportMedia(string sourcePath, string? folderName = null);

    string ImportMedia(Stream content, string originalFileName, string? folderName = null);

    IReadOnlyList<string> EnumerateImageFiles();

    IReadOnlyList<string> EnumerateMediaFiles(string? folderName = null);

    IReadOnlyList<string> EnumerateFolderNames();

    string CreateFolder(string name);

    string RenameFolder(string currentName, string newName);

    void DeleteFolder(string name);

    void DeleteMedia(string filePath);

    IReadOnlyList<string> GetDeletedFiles();

    void RestoreDeleted(string filePath);

    void PurgeDeleted(string filePath);

    void AddToFolder(string filePath, string folderName);

    void RemoveFromFolder(string filePath, string folderName);

    string MoveMedia(string filePath, string? folderName);

    void MoveMediaTo(string filePath, string? sourceFolder, string destinationFolder);

    string RenameMedia(string filePath, string newName);

    void PreserveOriginal(string imagePath);

    bool HasOriginal(string filePath);

    string? TryGetOriginalPath(string filePath);

    void RestoreOriginal(string filePath);

    string SaveScreenshot(Stream content, string fileName);

    string SaveNewImage(Stream content, string fileName);

    string ReplaceImage(string existingPath, Stream content, string fileName);
}
