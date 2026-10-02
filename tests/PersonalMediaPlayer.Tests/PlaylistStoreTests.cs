using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaylistStoreTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;

    public PlaylistStoreTests()
    {
        Monitor.Enter(Gate);
        _store = Path.Combine(Path.GetTempPath(), "pmp-playlists-" + Guid.NewGuid().ToString("N") + ".json");
        Playlists.StoreOverride = _store;
    }

    public void Dispose()
    {
        Playlists.StoreOverride = null;
        if (File.Exists(_store))
        {
            File.Delete(_store);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void OlderPlaylistsStayListsOfFilePaths()
    {
        File.WriteAllText(_store, """
            [{"Id":"abc","Name":"Evening","Videos":["C:\\clips\\a.mp4"],"PlayNext":true}]
            """);

        var list = Assert.Single(Playlists.All());
        var video = Assert.Single(list.Videos);
        Assert.False(video.Resolve);
        Assert.Equal("C:\\clips\\a.mp4", video.Location);
        Assert.Null(video.AddedUtc);
        Assert.Null(video.Thumbnail);
        Assert.True(list.PlayNext);
    }

    [Fact]
    public void AStreamSavesThePageLinkAndNotTheTemporaryAddress()
    {
        var created = Playlists.Create("Evening");
        Assert.NotNull(created);
        var page = "https://www.youtube.com/watch?v=abcdefghijk";
        var temporary = "https://rr3---sn-4g5e.googlevideo.com/videoplayback?id=1";

        Assert.Equal(1, Playlists.AddPage(created.Id, page, "Me at the zoo"));
        Assert.Equal(0, Playlists.AddPage(created.Id, page, "Me at the zoo"));
        Playlists.Add(created.Id, [@"C:\videos\clip.mp4"]);

        var saved = File.ReadAllText(_store);
        Assert.Contains(page, saved);
        Assert.DoesNotContain("googlevideo.com", saved);
        Assert.DoesNotContain(temporary, saved);
        Assert.Contains(@"C:\\videos\\clip.mp4", saved);

        var list = Playlists.Find(created.Id);
        Assert.NotNull(list);
        Assert.Equal(2, list.Videos.Count);
        Assert.True(list.Videos[0].Resolve);
        Assert.Equal(page, list.Videos[0].Location);
        Assert.Equal("Me at the zoo", list.Videos[0].Title);
        Assert.False(list.Videos[1].Resolve);
        Assert.Equal(@"C:\videos\clip.mp4", list.Videos[1].Location);
    }

    [Fact]
    public void RenamingAFileLeavesThePageLinkAlone()
    {
        var created = Playlists.Create("Evening");
        Assert.NotNull(created);
        var page = "https://www.youtube.com/watch?v=abcdefghijk";
        Playlists.AddPage(created.Id, page, "Zoo");
        Playlists.Add(created.Id, [@"C:\videos\clip.mp4"]);

        Playlists.MoveFile(@"C:\videos\clip.mp4", @"C:\videos\renamed.mp4");

        var list = Playlists.Find(created.Id);
        Assert.NotNull(list);
        Assert.Equal(page, list.Videos[0].Location);
        Assert.True(list.Videos[0].Resolve);
        Assert.Equal(@"C:\videos\renamed.mp4", list.Videos[1].Location);
        Assert.False(list.Videos[1].Resolve);
    }

    [Fact]
    public void Add_inserts_each_new_file_once()
    {
        var created = Playlists.Create("Evening");
        Assert.NotNull(created);

        Assert.Equal(2, Playlists.Add(created.Id, [@"C:\videos\a.mp4", @"C:\videos\b.mkv", @"C:\videos\a.mp4"]));
        Assert.Equal(0, Playlists.Add(created.Id, [@"c:\videos\A.mp4"]));

        var list = Playlists.Find(created.Id);
        Assert.NotNull(list);
        Assert.Equal(2, list.Videos.Count);
        Assert.Equal(@"C:\videos\a.mp4", list.Videos[0].Location);
        Assert.Equal(@"C:\videos\b.mkv", list.Videos[1].Location);
    }

    [Fact]
    public void SortingAndDraggingSaveTheOrder()
    {
        var created = Playlists.Create("Evening");
        Assert.NotNull(created);
        Assert.Equal(2, Playlists.Add(created.Id, [@"C:\videos\alpha.mp4", @"C:\videos\zeta.mp4"]));

        Playlists.SortByName(created.Id, path => Path.GetFileName(path));
        var named = Playlists.Find(created.Id);
        Assert.NotNull(named);
        Assert.Equal(@"C:\videos\alpha.mp4", named.Videos[0].Location);
        Assert.Equal(@"C:\videos\zeta.mp4", named.Videos[1].Location);

        Playlists.SortByAdded(created.Id);
        var dated = Playlists.Find(created.Id);
        Assert.NotNull(dated);
        Assert.Equal(@"C:\videos\zeta.mp4", dated.Videos[0].Location);
        Assert.Equal(@"C:\videos\alpha.mp4", dated.Videos[1].Location);
        Assert.NotNull(dated.Videos[0].AddedUtc);
        Assert.NotNull(dated.Videos[1].AddedUtc);
        Assert.True(dated.Videos[0].AddedUtc > dated.Videos[1].AddedUtc);

        var again = Playlists.Find(created.Id);
        Assert.NotNull(again);
        Assert.Equal(dated.Videos[0].Location, again.Videos[0].Location);
        Assert.Equal(dated.Videos[1].Location, again.Videos[1].Location);
        Assert.Equal(dated.Videos[0].AddedUtc, again.Videos[0].AddedUtc);
        Assert.Equal(dated.Videos[1].AddedUtc, again.Videos[1].AddedUtc);

        Assert.False(Playlists.MoveTo(created.Id, 0, 0));
        Assert.False(Playlists.MoveTo(created.Id, 0, 1));
        Assert.True(Playlists.MoveTo(created.Id, 1, 0));
        var moved = Playlists.Find(created.Id);
        Assert.NotNull(moved);
        Assert.Equal(@"C:\videos\alpha.mp4", moved.Videos[0].Location);
        Assert.Equal(@"C:\videos\zeta.mp4", moved.Videos[1].Location);
        Assert.Equal(2, moved.Videos.Count);
        Assert.Equal(again.Videos[1].AddedUtc, moved.Videos[0].AddedUtc);
    }

    [Fact]
    public void SortingByDateStampsOlderEntriesFromTheirOrder()
    {
        File.WriteAllText(_store, """
            [{"Id":"abc","Name":"Evening","Videos":["C:\\clips\\older.mp4","C:\\clips\\newer.mp4"]}]
            """);

        Playlists.SortByAdded("abc");

        var list = Playlists.Find("abc");
        Assert.NotNull(list);
        Assert.Equal(@"C:\clips\newer.mp4", list.Videos[0].Location);
        Assert.Equal(@"C:\clips\older.mp4", list.Videos[1].Location);
        Assert.NotNull(list.Videos[0].AddedUtc);
        Assert.NotNull(list.Videos[1].AddedUtc);
        Assert.True(list.Videos[0].AddedUtc > list.Videos[1].AddedUtc);

        var again = Playlists.Find("abc");
        Assert.NotNull(again);
        Assert.Equal(list.Videos[0].AddedUtc, again.Videos[0].AddedUtc);
        Assert.Equal(list.Videos[1].AddedUtc, again.Videos[1].AddedUtc);
    }

    [Fact]
    public void SearchMatchesANameWithoutRemovingVideos()
    {
        var created = Playlists.Create("Evening");
        Assert.NotNull(created);
        var page = "https://www.youtube.com/watch?v=abcdefghijk";
        Assert.Equal(1, Playlists.AddPage(created.Id, page, "Zoo"));
        Assert.Equal(2, Playlists.Add(created.Id, [@"C:\videos\alpha.mp4", @"C:\videos\zeta.mp4"]));

        var list = Playlists.Find(created.Id);
        Assert.NotNull(list);
        Assert.True(list.Videos[0].Matches("  "));
        Assert.True(list.Videos[0].Matches("zoo"));
        Assert.False(list.Videos[0].Matches("youtube"));
        Assert.True(list.Videos[1].Matches("alpha"));
        Assert.True(list.Videos[1].Matches("holiday", _ => "Holiday trip"));
        Assert.False(list.Videos[1].Matches("zeta", _ => "Holiday trip"));
        Assert.True(list.Videos[2].Matches("ZETA"));

        var still = Playlists.Find(created.Id);
        Assert.NotNull(still);
        Assert.Equal(3, still.Videos.Count);
        Assert.Equal(page, still.Videos[0].Location);
        Assert.Equal(@"C:\videos\alpha.mp4", still.Videos[1].Location);
        Assert.Equal(@"C:\videos\zeta.mp4", still.Videos[2].Location);
    }

    [Fact]
    public void MovingAFileKeepsWhenItWasAdded()
    {
        var created = Playlists.Create("Evening");
        Assert.NotNull(created);
        Assert.Equal(1, Playlists.Add(created.Id, [@"C:\videos\clip.mp4"]));
        var before = Playlists.Find(created.Id);
        Assert.NotNull(before);
        Assert.NotNull(before.Videos[0].AddedUtc);

        Playlists.MoveFile(@"C:\videos\clip.mp4", @"C:\videos\renamed.mp4");

        var after = Playlists.Find(created.Id);
        Assert.NotNull(after);
        Assert.Equal(@"C:\videos\renamed.mp4", after.Videos[0].Location);
        Assert.False(after.Videos[0].Resolve);
        Assert.Equal(before.Videos[0].AddedUtc, after.Videos[0].AddedUtc);
    }

    [Fact]
    public void SortingByNameKeepsAPageLink()
    {
        var created = Playlists.Create("Evening");
        Assert.NotNull(created);
        var page = "https://www.youtube.com/watch?v=abcdefghijk";
        Assert.Equal(1, Playlists.AddPage(created.Id, page, "Zoo"));
        Assert.Equal(1, Playlists.Add(created.Id, [@"C:\videos\alpha.mp4"]));

        Playlists.SortByName(created.Id, path => Path.GetFileNameWithoutExtension(path));

        var list = Playlists.Find(created.Id);
        Assert.NotNull(list);
        Assert.Equal(2, list.Videos.Count);
        Assert.False(list.Videos[0].Resolve);
        Assert.Equal(@"C:\videos\alpha.mp4", list.Videos[0].Location);
        Assert.True(list.Videos[1].Resolve);
        Assert.Equal(page, list.Videos[1].Location);
        Assert.Equal("Zoo", list.Videos[1].Title);
    }

    [Fact]
    public void DraggingPastAHiddenNeighborKeepsThatNeighbor()
    {
        var created = Playlists.Create("Evening");
        Assert.NotNull(created);
        Assert.Equal(3, Playlists.Add(created.Id, [@"C:\videos\alpha.mp4", @"C:\videos\beta.mp4", @"C:\videos\zeta.mp4"]));

        Assert.True(Playlists.MoveTo(created.Id, 2, 0));

        var list = Playlists.Find(created.Id);
        Assert.NotNull(list);
        Assert.Equal(
            [@"C:\videos\zeta.mp4", @"C:\videos\alpha.mp4", @"C:\videos\beta.mp4"],
            list.Videos.Select(video => video.Location));
    }

    [Fact]
    public void AnOpenedPageKeepsItsPicture()
    {
        var created = Playlists.Create("Evening");
        Assert.NotNull(created);
        var page = "https://vimeo.com/123456";
        var picture = "https://cdn.example/poster.jpg";
        Assert.Equal(1, Playlists.AddPage(created.Id, page, "Clip"));
        Assert.Equal(1, Playlists.Add(created.Id, [@"C:\videos\clip.mp4"]));

        Assert.False(Playlists.RememberThumbnail(created.Id, page, "not a url"));
        Assert.False(Playlists.RememberThumbnail(created.Id, page, "file:///C:/poster.jpg"));
        Assert.True(Playlists.RememberThumbnail(created.Id, page, picture));
        Assert.False(Playlists.RememberThumbnail(created.Id, page, picture));

        var list = Playlists.Find(created.Id);
        Assert.NotNull(list);
        Assert.Equal(picture, list.Videos[0].Thumbnail);
        Assert.True(list.Videos[0].Resolve);
        Assert.Null(list.Videos[1].Thumbnail);
        Assert.Contains(picture, File.ReadAllText(_store));

        Playlists.MoveFile(@"C:\videos\clip.mp4", @"C:\videos\renamed.mp4");
        Playlists.SortByName(created.Id, path => Path.GetFileNameWithoutExtension(path));
        var sorted = Playlists.Find(created.Id);
        Assert.NotNull(sorted);
        var saved = Assert.Single(sorted.Videos, video => video.Resolve);
        Assert.Equal(page, saved.Location);
        Assert.Equal(picture, saved.Thumbnail);
        Assert.Contains(@"C:\videos\renamed.mp4", sorted.Videos.Select(video => video.Location));
    }
}
