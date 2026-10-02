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
