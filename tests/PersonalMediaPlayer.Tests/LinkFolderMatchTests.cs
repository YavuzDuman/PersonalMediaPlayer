using PersonalMediaPlayer.Core.Storage;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class LinkFolderMatchTests
{
    [Fact]
    public void AMovedFolderMatchesEachFileByItsRelativePath()
    {
        using var root = new Temp();
        var first = root.PathOf("old", "Trip", "day1", "clip.mp4");
        var second = root.PathOf("old", "Trip", "day2", "clip.mp4");
        var moved = root.Dir("new", "Trip");
        var newFirst = root.Write("new", "Trip", "day1", "clip.mp4");
        var newSecond = root.Write("new", "Trip", "day2", "clip.mp4");
        root.Write("new", "Trip", "extra", "clip.mp4");

        var matches = LinkFolderMatch.Match([first, second], LinkFolderMatch.FindMedia(moved));

        Assert.Equal(LinkMatchState.Ready, matches[0].State);
        Assert.Equal(Path.GetFullPath(newFirst), matches[0].ChosenPath);
        Assert.Equal(LinkMatchState.Ready, matches[1].State);
        Assert.Equal(Path.GetFullPath(newSecond), matches[1].ChosenPath);
    }

    [Fact]
    public void OneFileWithThatNameIsReadyEvenInASubfolder()
    {
        using var root = new Temp();
        var missing = root.PathOf("old", "clip.mp4");
        var found = root.Write("new", "nested", "clip.mp4");

        var match = Assert.Single(LinkFolderMatch.Match([missing], LinkFolderMatch.FindMedia(root.Dir("new"))));

        Assert.Equal(LinkMatchState.Ready, match.State);
        Assert.Equal(Path.GetFullPath(found), match.ChosenPath);
    }

    [Fact]
    public void TheSameNameInTwoPlacesAsks()
    {
        using var root = new Temp();
        var missing = root.PathOf("old", "clip.mp4");
        var one = root.Write("new", "one", "clip.mp4");
        var two = root.Write("new", "two", "clip.mp4");

        var match = Assert.Single(LinkFolderMatch.Match([missing], [one, two]));

        Assert.Equal(LinkMatchState.Choose, match.State);
        Assert.Null(match.ChosenPath);
        Assert.Equal([Path.GetFullPath(one), Path.GetFullPath(two)], match.Candidates);
    }

    [Fact]
    public void TwoPreservedPathsAskInsteadOfChoosingOne()
    {
        using var root = new Temp();
        var clip = root.PathOf("old", "Trip", "day", "clip.mp4");
        var cover = root.PathOf("old", "Trip", "cover.jpg");
        var first = root.Write("one", "Trip", "day", "clip.mp4");
        var second = root.Write("two", "Trip", "day", "clip.mp4");
        var photo = root.Write("one", "Trip", "cover.jpg");

        var matches = LinkFolderMatch.Match([clip, cover], [first, second, photo]);

        Assert.Equal(LinkMatchState.Choose, matches[0].State);
        Assert.Null(matches[0].ChosenPath);
        Assert.Equal([Path.GetFullPath(first), Path.GetFullPath(second)], matches[0].Candidates);
        Assert.Equal(LinkMatchState.Ready, matches[1].State);
        Assert.Equal(Path.GetFullPath(photo), matches[1].ChosenPath);
    }

    [Fact]
    public void TwoMissingFilesDoNotShareOneTarget()
    {
        using var root = new Temp();
        var first = root.PathOf("one", "clip.mp4");
        var second = root.PathOf("two", "clip.mp4");
        var found = root.Write("new", "clip.mp4");

        var matches = LinkFolderMatch.Match([first, second], [found]);

        Assert.All(matches, match => Assert.Equal(LinkMatchState.Choose, match.State));
        Assert.All(matches, match => Assert.Null(match.ChosenPath));
        Assert.All(matches, match => Assert.Equal([Path.GetFullPath(found)], match.Candidates));
    }

    [Fact]
    public void ADifferentNameOrTypeIsLeftUnmatched()
    {
        using var root = new Temp();
        var video = root.PathOf("old", "clip.mp4");
        var photo = root.PathOf("old", "shot.jpg");
        var other = root.Write("new", "clip.mkv");
        var wrongType = root.Write("new", "shot.mp4");
        root.Write("new", "notes.txt");

        var matches = LinkFolderMatch.Match([video, photo], LinkFolderMatch.FindMedia(root.Dir("new")));

        Assert.Equal(LinkMatchState.None, matches[0].State);
        Assert.Equal(LinkMatchState.None, matches[1].State);
        Assert.DoesNotContain(Path.GetFullPath(other), matches.SelectMany(match => match.Candidates));
        Assert.DoesNotContain(Path.GetFullPath(wrongType), matches.SelectMany(match => match.Candidates));
    }

    [Fact]
    public void TheSameNameMatchesWhenOnlyTheLetterCaseDiffers()
    {
        using var root = new Temp();
        var missing = root.PathOf("old", "Clip.MP4");
        var found = root.Write("new", "clip.mp4");

        var match = Assert.Single(LinkFolderMatch.Match([missing], [found]));

        Assert.Equal(LinkMatchState.Ready, match.State);
        Assert.Equal(Path.GetFullPath(found), match.ChosenPath);
    }

    [Fact]
    public void FindMediaReadsSubfoldersAndSkipsOtherFiles()
    {
        using var root = new Temp();
        var clip = root.Write("media", "day", "clip.mp4");
        var photo = root.Write("media", "shot.jpg");
        root.Write("media", "notes.txt");
        var hidden = root.Write("media", "secret.mp4");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var found = LinkFolderMatch.FindMedia(root.Dir("media"));

        Assert.Contains(Path.GetFullPath(clip), found);
        Assert.Contains(Path.GetFullPath(photo), found);
        Assert.Contains(Path.GetFullPath(hidden), found);
        Assert.Equal(3, found.Count);
        Assert.Empty(LinkFolderMatch.FindMedia(root.PathOf("missing-folder")));
    }

    private sealed class Temp : IDisposable
    {
        public Temp()
        {
            Root = Path.Combine(Path.GetTempPath(), "pmp-locate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Dir(params string[] parts)
        {
            var path = Path.Combine([Root, .. parts]);
            Directory.CreateDirectory(path);
            return path;
        }

        public string PathOf(params string[] parts)
            => Path.Combine([Root, .. parts]);

        public string Write(params string[] parts)
        {
            var path = PathOf(parts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
