using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class SearchSessionTests
{
    [Fact]
    public void ASearchKeepsItsTextAndPlace()
    {
        var key = "home-" + Guid.NewGuid().ToString("N");

        SearchSession.Remember(key, "harbor", 240);

        var place = SearchSession.Recall(key);
        Assert.Equal("harbor", place.Text);
        Assert.Equal(240, place.Offset);
    }

    [Fact]
    public void EachSearchIsSeparate()
    {
        var home = "home-" + Guid.NewGuid().ToString("N");
        var library = "library-" + Guid.NewGuid().ToString("N");

        SearchSession.Remember(home, "harbor", 10);
        SearchSession.Remember(library, "photo", 80);

        Assert.Equal("harbor", SearchSession.Recall(home).Text);
        Assert.Equal("photo", SearchSession.Recall(library).Text);
        Assert.Equal(80, SearchSession.Recall(library).Offset);
    }

    [Fact]
    public void ClearingASearchForgetsIt()
    {
        var key = "words-" + Guid.NewGuid().ToString("N");
        SearchSession.Remember(key, "trunk", 40);

        SearchSession.Remember(key, "  ", 0);

        Assert.Equal(string.Empty, SearchSession.Recall(key).Text);
        Assert.Equal(0, SearchSession.Recall(key).Offset);
    }

    [Fact]
    public void PlaylistAndWordKeysStayDistinct()
    {
        var playlist = SearchSession.Playlist("evening");
        var words = SearchSession.Words("evening");

        SearchSession.Remember(playlist, "harbor", 12);
        SearchSession.Remember(words, "trunk", 30);

        Assert.Equal("harbor", SearchSession.Recall(playlist).Text);
        Assert.Equal("trunk", SearchSession.Recall(words).Text);
    }

    [Fact]
    public void ANegativePlaceIsStoredAtTheTop()
    {
        var key = "top-" + Guid.NewGuid().ToString("N");

        SearchSession.Remember(key, "harbor", -5);

        Assert.Equal(0, SearchSession.Recall(key).Offset);
    }

    [Fact]
    public void AnUnreadyListKeepsTheOldPlace()
    {
        var key = "ready-" + Guid.NewGuid().ToString("N");
        SearchSession.Remember(key, "harbor", 240);

        SearchSession.Remember(key, "harbor", 0, placeReady: false);

        Assert.Equal(240, SearchSession.Recall(key).Offset);
    }

    [Fact]
    public void TheTopOfAReadyListIsKept()
    {
        var key = "ready-top-" + Guid.NewGuid().ToString("N");
        SearchSession.Remember(key, "harbor", 240);

        SearchSession.Remember(key, "harbor", 0, placeReady: true);

        Assert.Equal(0, SearchSession.Recall(key).Offset);
    }

    [Fact]
    public void ClearingASearchForgetsItsPlace()
    {
        var key = "clear-" + Guid.NewGuid().ToString("N");
        SearchSession.Remember(key, "harbor", 240);

        SearchSession.Remember(key, string.Empty, 0, placeReady: false);

        Assert.Equal(string.Empty, SearchSession.Recall(key).Text);
        Assert.Equal(0, SearchSession.Recall(key).Offset);
    }
}
