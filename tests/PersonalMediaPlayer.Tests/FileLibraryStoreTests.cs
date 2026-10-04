using PersonalMediaPlayer.Core.Library;
using PersonalMediaPlayer.Core.Models;
using PersonalMediaPlayer.Core.Storage;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class FileLibraryStoreTests
{
    [Fact]
    public void LinkMedia_lists_the_file_and_delete_leaves_the_original()
    {
        using var root = new TempFolder();
        var source = root.WriteOutside("clip.mp4", [1, 2, 3, 4]);
        var store = new FileLibraryStore(root.Library);

        var linked = store.LinkMedia(source, "Trip");

        Assert.Equal(Path.GetFullPath(source), Path.GetFullPath(linked));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(store.VideosDirectory));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, source));
        Assert.Contains(store.EnumerateMediaFiles("Trip"), path => Same(path, source));

        store.PreserveOriginal(linked);
        Assert.True(store.HasOriginal(linked));
        store.DeleteMedia(linked);

        Assert.True(File.Exists(source));
        Assert.DoesNotContain(store.EnumerateMediaFiles(), path => Same(path, source));
        Assert.Empty(store.GetDeletedFiles());
        Assert.False(store.HasOriginal(source));
        var reopened = new FileLibraryStore(root.Library);
        Assert.DoesNotContain(reopened.EnumerateMediaFiles(), path => Same(path, source));
    }

    [Fact]
    public void ImportMedia_still_copies_and_delete_moves_only_the_copy()
    {
        using var root = new TempFolder();
        var source = root.WriteOutside("clip.mp4", [1, 2, 3, 4]);
        var store = new FileLibraryStore(root.Library);

        var imported = store.ImportMedia(source);
        Assert.NotEqual(Path.GetFullPath(source), Path.GetFullPath(imported));
        Assert.StartsWith(store.VideosDirectory, Path.GetFullPath(imported), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(source));
        Assert.Single(store.EnumerateMediaFiles());

        var again = store.LinkMedia(imported);
        Assert.Equal(Path.GetFullPath(imported), Path.GetFullPath(again));
        Assert.Single(store.EnumerateMediaFiles());

        store.DeleteMedia(imported);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(imported));
        Assert.Single(store.GetDeletedFiles());
        Assert.DoesNotContain(store.EnumerateMediaFiles(), path => Same(path, imported));
    }

    [Fact]
    public void RelocateLink_points_at_the_moved_file_without_copying_it()
    {
        using var root = new TempFolder();
        var source = root.WriteOutside("clip.mp4", [9, 8, 7]);
        var store = new FileLibraryStore(root.Library);
        var library = new MediaLibrary(store);
        var linked = library.LinkMedia(source, "Trip");
        Assert.True(LibraryFolder.IsInThisMonth(linked.ImportedAt));
        Assert.True(linked.IsLinked);
        Assert.False(linked.IsMissing);
        store.PreserveOriginal(linked.FilePath);

        var moved = Path.Combine(root.Outside, "moved.mp4");
        File.Move(source, moved);

        var missing = Assert.Single(library.GetItems());
        Assert.True(missing.IsMissing);
        Assert.Equal("Missing", missing.SizeLabel);
        Assert.Equal(Path.GetFullPath(source), missing.FilePath);
        Assert.NotNull(library.GetById(missing.Id));
        Assert.Null(library.GetById(Path.GetRelativePath(store.LibraryRoot, source)));
        Assert.Throws<InvalidOperationException>(() => store.RenameMedia(source, "renamed.mp4"));

        var located = library.RelocateLink(source, moved);

        Assert.Equal(Path.GetFullPath(moved), located.FilePath);
        Assert.False(located.IsMissing);
        Assert.Equal(3, located.FileSizeBytes);
        Assert.Contains("Trip", located.FolderName, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(store.VideosDirectory));
        Assert.Empty(Directory.EnumerateFiles(store.ImagesDirectory));
        Assert.Contains(store.EnumerateMediaFiles("Trip"), path => Same(path, moved));
        Assert.DoesNotContain(store.EnumerateMediaFiles(), path => Same(path, source));
        Assert.True(store.HasOriginal(moved));
        Assert.False(store.HasOriginal(source));
        Assert.Throws<InvalidOperationException>(() => store.RelocateLink(moved, importedAlready(store)));

        string importedAlready(FileLibraryStore current)
        {
            var other = root.WriteOutside("other.mp4", [1]);
            return current.ImportMedia(other);
        }
    }

    [Fact]
    public void RelocateLink_rejects_a_different_media_type_and_keeps_the_entry()
    {
        using var root = new TempFolder();
        var video = root.WriteOutside("clip.mp4", [1, 2, 3]);
        var photo = root.WriteOutside("shot.jpg", [4, 5]);
        var store = new FileLibraryStore(root.Library);
        store.LinkMedia(video, "Trip");
        store.PreserveOriginal(video);

        var error = Assert.Throws<InvalidOperationException>(() => store.RelocateLink(video, photo));
        Assert.Equal("Choose a video. A photo cannot replace this video.", error.Message);
        Assert.True(store.IsLinked(video));
        Assert.False(store.IsLinked(photo));
        Assert.Contains(store.EnumerateMediaFiles("Trip"), path => Same(path, video));
        Assert.DoesNotContain(store.EnumerateMediaFiles(), path => Same(path, photo));
        Assert.True(File.Exists(video));
        Assert.True(File.Exists(photo));
        Assert.True(store.HasOriginal(video));

        var otherVideo = root.WriteOutside("other.mkv", [9]);
        var located = store.RelocateLink(video, otherVideo);
        Assert.Equal(Path.GetFullPath(otherVideo), Path.GetFullPath(located));
        Assert.Contains(store.EnumerateMediaFiles("Trip"), path => Same(path, otherVideo));

        var image = root.WriteOutside("pic.png", [7]);
        store.LinkMedia(image);
        var photoError = Assert.Throws<InvalidOperationException>(() => store.RelocateLink(image, video));
        Assert.Equal("Choose a photo. A video cannot replace this photo.", photoError.Message);
        Assert.True(store.IsLinked(image));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, image));
    }

    [Fact]
    public void Replacing_a_linked_image_with_a_new_name_keeps_the_original_file()
    {
        using var root = new TempFolder();
        var source = root.WriteOutside("shot.jpg", [1, 2, 3]);
        var store = new FileLibraryStore(root.Library);
        store.LinkMedia(source);

        using var content = new MemoryStream([9, 9, 9]);
        var replaced = store.ReplaceImage(source, content, "shot.png");

        Assert.True(File.Exists(source));
        Assert.True(File.Exists(replaced));
        Assert.NotEqual(Path.GetFullPath(source), Path.GetFullPath(replaced));
        Assert.Equal(Path.GetDirectoryName(source), Path.GetDirectoryName(replaced), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, replaced));
        Assert.DoesNotContain(store.EnumerateMediaFiles(), path => Same(path, source));
        Assert.Empty(Directory.EnumerateFiles(store.ImagesDirectory));
        Assert.True(store.HasOriginal(replaced));
    }

    [Fact]
    public void ReloadLinks_reads_the_file_again()
    {
        using var root = new TempFolder();
        var first = root.WriteOutside("one.mp4", [1]);
        var second = root.WriteOutside("two.mp4", [2]);
        var store = new FileLibraryStore(root.Library);
        store.LinkMedia(first);

        var jsonPath = Path.GetFullPath(second).Replace("\\", "\\\\");
        File.WriteAllText(
            Path.Combine(root.Library, "links.json"),
            $"[{{\"Path\":\"{jsonPath}\",\"AddedUtc\":\"2026-01-01T00:00:00Z\"}}]");

        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, first));
        Assert.DoesNotContain(store.EnumerateMediaFiles(), path => Same(path, second));

        store.ReloadLinks();

        Assert.DoesNotContain(store.EnumerateMediaFiles(), path => Same(path, first));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, second));
    }

    [Fact]
    public void ConnectingAFolderLinksNewFilesAndRefreshDoesNotDuplicateThem()
    {
        using var root = new TempFolder();
        var media = Path.Combine(root.Outside, "media");
        var day = Path.Combine(media, "day");
        Directory.CreateDirectory(day);
        var clip = Path.Combine(media, "clip.mp4");
        var photo = Path.Combine(media, "shot.jpg");
        var nested = Path.Combine(day, "extra.mkv");
        var hidden = Path.Combine(media, "secret.mp4");
        File.WriteAllBytes(clip, [1, 2, 3]);
        File.WriteAllBytes(photo, [4, 5]);
        File.WriteAllBytes(nested, [6]);
        File.WriteAllBytes(hidden, [7]);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(media, "notes.txt"), "skip");
        var store = new FileLibraryStore(root.Library);
        store.LinkMedia(clip);

        Assert.Equal(ConnectFolderResult.Connected, store.ConnectFolder(media));
        Assert.Equal(ConnectFolderResult.AlreadyConnected, store.ConnectFolder(media + Path.DirectorySeparatorChar));
        Assert.Equal(ConnectFolderResult.NotAFolder, store.ConnectFolder(clip));
        var first = store.RefreshConnectedFolders();

        Assert.Equal(2, first.Added);
        Assert.Equal(1, first.AlreadyThere);
        Assert.Equal(0, first.MissingFolders);
        Assert.Single(store.ConnectedFolders());
        Assert.Empty(Directory.EnumerateFiles(store.VideosDirectory));
        Assert.Empty(Directory.EnumerateFiles(store.ImagesDirectory));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, clip));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, photo));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, nested));
        Assert.DoesNotContain(store.EnumerateMediaFiles(), path => Same(path, hidden));
        Assert.Equal(3, store.EnumerateMediaFiles().Count);
        Assert.Equal(1, File.ReadAllText(Path.Combine(root.Library, "links.json")).Split("clip.mp4").Length - 1);

        var again = store.RefreshConnectedFolders();
        Assert.Equal(0, again.Added);
        Assert.Equal(3, again.AlreadyThere);
        Assert.Equal(3, store.EnumerateMediaFiles().Count);

        var late = Path.Combine(media, "late.png");
        File.WriteAllBytes(late, [8]);
        var found = store.RefreshConnectedFolders();
        Assert.Equal(1, found.Added);
        Assert.Equal(3, found.AlreadyThere);
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, late));
        Assert.Empty(Directory.EnumerateFiles(store.ImagesDirectory));
        Assert.True(File.Exists(clip));
        Assert.True(File.Exists(photo));
    }

    [Fact]
    public void RefreshKeepsAMissingFileAndItsAlbum()
    {
        using var root = new TempFolder();
        var media = Path.Combine(root.Outside, "media");
        Directory.CreateDirectory(media);
        var clip = Path.Combine(media, "clip.mp4");
        var photo = Path.Combine(media, "shot.jpg");
        File.WriteAllBytes(clip, [1, 2, 3]);
        File.WriteAllBytes(photo, [4]);
        var store = new FileLibraryStore(root.Library);
        var library = new MediaLibrary(store);
        Assert.Equal(ConnectFolderResult.Connected, store.ConnectFolder(media));
        Assert.Equal(2, store.RefreshConnectedFolders().Added);
        store.AddToFolder(clip, "Trip");

        File.Delete(clip);
        var scan = store.RefreshConnectedFolders();

        Assert.Equal(0, scan.Added);
        Assert.False(File.Exists(clip));
        Assert.True(File.Exists(photo));
        Assert.Contains(store.EnumerateMediaFiles("Trip"), path => Same(path, clip));
        var missing = library.GetItems().Single(item => Same(item.FilePath, clip));
        Assert.True(missing.IsMissing);
        Assert.True(missing.IsLinked);
        Assert.Contains("Trip", missing.FolderName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.GetFullPath(clip), missing.FilePath);

        Directory.Delete(media, recursive: true);
        var unavailable = store.RefreshConnectedFolders();
        Assert.Equal(1, unavailable.MissingFolders);
        Assert.Equal(0, unavailable.Added);
        Assert.Contains(store.ConnectedFolders(), path => Same(path, media));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, clip));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, photo));
    }

    [Fact]
    public void DisconnectingAFolderLeavesTheFilesInPlace()
    {
        using var root = new TempFolder();
        var media = Path.Combine(root.Outside, "media");
        Directory.CreateDirectory(media);
        var clip = Path.Combine(media, "clip.mp4");
        File.WriteAllBytes(clip, [1, 2, 3, 4]);
        var elsewhere = root.WriteOutside("other.mp4", [9]);
        var store = new FileLibraryStore(root.Library);
        store.LinkMedia(elsewhere);
        Assert.Equal(ConnectFolderResult.Connected, store.ConnectFolder(media));
        Assert.Equal(1, store.RefreshConnectedFolders().Added);

        Assert.True(store.DisconnectFolder(media));
        Assert.False(store.DisconnectFolder(media));
        Assert.Empty(store.ConnectedFolders());
        Assert.True(File.Exists(clip));
        Assert.True(File.Exists(elsewhere));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, clip));
        Assert.Contains(store.EnumerateMediaFiles(), path => Same(path, elsewhere));
        Assert.Empty(Directory.EnumerateFiles(store.VideosDirectory));

        var added = Path.Combine(media, "new.mp4");
        File.WriteAllBytes(added, [5]);
        var scan = store.RefreshConnectedFolders();
        Assert.Equal(0, scan.Added);
        Assert.DoesNotContain(store.EnumerateMediaFiles(), path => Same(path, added));
        Assert.True(File.Exists(added));
        Assert.False(File.Exists(Path.Combine(root.Library, "connected-folders.json")));
    }

    [Fact]
    public void ConnectingRefusesTheLibraryAndAnOverlappingFolder()
    {
        using var root = new TempFolder();
        var media = Path.Combine(root.Outside, "media");
        var day = Path.Combine(media, "day");
        Directory.CreateDirectory(day);
        var store = new FileLibraryStore(root.Library);

        Assert.Equal(ConnectFolderResult.InsideLibrary, store.ConnectFolder(store.LibraryRoot));
        Assert.Equal(ConnectFolderResult.InsideLibrary, store.ConnectFolder(store.VideosDirectory));
        Assert.Equal(ConnectFolderResult.CoversLibrary, store.ConnectFolder(root.Root));
        Assert.Equal(ConnectFolderResult.Connected, store.ConnectFolder(media));
        Assert.Equal(ConnectFolderResult.Overlaps, store.ConnectFolder(day));
        Assert.Equal(ConnectFolderResult.Overlaps, store.ConnectFolder(root.Outside));
        Assert.Equal(new[] { Path.GetFullPath(media) }, store.ConnectedFolders().Select(Path.GetFullPath));
        Assert.Empty(store.EnumerateMediaFiles());
        Assert.False(File.Exists(Path.Combine(store.VideosDirectory, "day")));
    }

    private static bool Same(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "pmp-library-" + Guid.NewGuid().ToString("N"));
            Library = Path.Combine(Root, "library");
            Outside = Path.Combine(Root, "outside");
            Directory.CreateDirectory(Outside);
        }

        public string Root { get; }

        public string Library { get; }

        public string Outside { get; }

        public string WriteOutside(string name, byte[] bytes)
        {
            var path = Path.Combine(Outside, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
