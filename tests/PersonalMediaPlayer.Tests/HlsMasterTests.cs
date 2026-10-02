using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class HlsMasterTests
{
    private static readonly Uri Playlist = new("https://cdn.example/file/index.m3u8");

    [Fact]
    public void SixAudioLanguagesKeepTheSelectedLanguage()
    {
        var master = Parse(Master(new[] { "ar", "de", "en", "es", "fr", "hi" }, "en"));
        Assert.True(master.ExceedsAudioLimit);
        Assert.Equal(6, master.Audios.Count);
        Assert.Equal("en", master.SelectAudio(null));

        var rewritten = master.Rewrite("hi", 0, Playlist);
        var trimmed = Parse(rewritten);
        Assert.False(trimmed.ExceedsAudioLimit);
        Assert.Equal("hi", Assert.Single(trimmed.Audios).Key);
        Assert.Equal(1, trimmed.MaxAudioRenditionsInAnyGroup);
        Assert.DoesNotContain("TYPE=SUBTITLES", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SUBTITLES=", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LANGUAGE=\"hi\"", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("LANGUAGE=\"ar\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("https://cdn.example/v/360.m3u8", rewritten, StringComparison.Ordinal);
        Assert.Contains("https://cdn.example/v/720.m3u8", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void FiveAudioLanguagesStayListed()
    {
        var master = Parse(Master(new[] { "ar", "de", "en", "es", "fr" }, "en"));
        Assert.False(master.ExceedsAudioLimit);
        Assert.Equal(5, master.Audios.Count);
        Assert.Equal(new[] { "en", "ar", "de", "es", "fr" }, master.Audios.Select(item => item.Key).ToArray());
    }

    [Fact]
    public void TheSameLanguageStopsAtFiveRenditionsInAGroup()
    {
        var lines = new List<string> { "#EXTM3U" };
        for (var i = 0; i < 8; i++)
        {
            lines.Add($"#EXT-X-MEDIA:URI=\"https://cdn.example/a/{i}.m3u8\",TYPE=AUDIO,GROUP-ID=\"233\",LANGUAGE=\"en\",NAME=\"English {i}\",DEFAULT={(i == 0 ? "YES" : "NO")},AUTOSELECT=YES");
        }

        lines.Add("#EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=640x360,AUDIO=\"233\"");
        lines.Add("https://cdn.example/v/360.m3u8");
        var master = Parse(string.Join('\n', lines));
        Assert.True(master.ExceedsAudioLimit);
        Assert.Single(master.Audios);

        var trimmed = Parse(master.Rewrite(null, 0, Playlist));
        Assert.Equal(HlsMaster.MaxAudioRenditionsPerGroup, trimmed.MaxAudioRenditionsInAnyGroup);
    }

    [Fact]
    public void AChosenHeightKeepsThatPictureAndPrefersAvc()
    {
        var text = """
            #EXTM3U
            #EXT-X-MEDIA:URI="https://cdn.example/a/en.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="en",NAME="English",DEFAULT=YES,AUTOSELECT=YES
            #EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=640x360,CODECS="avc1.4d401e,mp4a.40.2",AUDIO="233"
            https://cdn.example/v/360.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720,CODECS="av01.0.08M.08,mp4a.40.2",AUDIO="233"
            https://cdn.example/v/720-av1.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2400000,RESOLUTION=1280x720,CODECS="avc1.4d401f,mp4a.40.2",AUDIO="233"
            https://cdn.example/v/720-avc.m3u8
            """;
        var master = Parse(text);
        Assert.Equal(new[] { 720, 360 }, master.Qualities.Select(item => item.Height).ToArray());

        var chosen = master.Rewrite(null, 720, Playlist);
        Assert.Contains("https://cdn.example/v/720-avc.m3u8", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("720-av1", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("/360.m3u8", chosen, StringComparison.Ordinal);

        var auto = master.Rewrite(null, 0, Playlist);
        Assert.Contains("720-av1", auto, StringComparison.Ordinal);
        Assert.Contains("/360.m3u8", auto, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeAddressesBecomeAbsolute()
    {
        var text = """
            #EXTM3U
            #EXT-X-VERSION:3
            #EXT-X-MEDIA:URI="en.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="en",NAME="English, original",DEFAULT=YES,AUTOSELECT=YES
            #EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=640x360,AUDIO="233",SUBTITLES="vtt"
            360.m3u8
            """;
        var master = Parse(text);
        Assert.Equal("English · Original", master.Audios[0].Label);
        var rewritten = master.Rewrite(null, 0, Playlist);
        Assert.Contains("https://cdn.example/file/en.m3u8", rewritten, StringComparison.Ordinal);
        Assert.Contains("https://cdn.example/file/360.m3u8", rewritten, StringComparison.Ordinal);
        Assert.Contains("#EXT-X-VERSION:3", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultLanguageWinsAndOriginalIsNext()
    {
        var text = """
            #EXTM3U
            #EXT-X-MEDIA:URI="https://cdn.example/a/en.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="en",NAME="English - dubbed",DEFAULT=NO,AUTOSELECT=NO
            #EXT-X-MEDIA:URI="https://cdn.example/a/ja.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="ja",NAME="Japanese - original",DEFAULT=NO,AUTOSELECT=YES
            #EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=640x360,AUDIO="233"
            https://cdn.example/v/360.m3u8
            """;
        var original = Parse(text);
        Assert.Equal("ja", original.SelectAudio(null));

        var withDefault = text.Replace("LANGUAGE=\"en\",NAME=\"English - dubbed\",DEFAULT=NO", "LANGUAGE=\"en\",NAME=\"English - dubbed\",DEFAULT=YES", StringComparison.Ordinal);
        Assert.Equal("en", Parse(withDefault).SelectAudio(null));
        Assert.Equal("ja", Parse(withDefault).SelectAudio("ja"));
    }

    [Fact]
    public void AMissingAudioGroupPointsAtTheKeptGroup()
    {
        var text = """
            #EXTM3U
            #EXT-X-MEDIA:URI="https://cdn.example/a/en.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="en",NAME="English",DEFAULT=YES,AUTOSELECT=YES
            #EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=640x360,AUDIO="234"
            https://cdn.example/v/360.m3u8
            """;
        var rewritten = Parse(text).Rewrite("en", 0, Playlist);
        Assert.Contains("AUDIO=\"233\"", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("234", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void AMediaPlaylistIsNotAMaster()
    {
        Assert.False(HlsMaster.TryParse("#EXTM3U\n#EXTINF:4,\nsegment.ts\n", out _));
        Assert.False(HlsMaster.TryParse("not a playlist", out _));
    }

    [Fact]
    public void AudioNamesUseEnglish()
    {
        var text = """
            #EXTM3U
            #EXT-X-MEDIA:URI="https://cdn.example/a/en.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="en",NAME="English - original",DEFAULT=YES,AUTOSELECT=YES
            #EXT-X-MEDIA:URI="https://cdn.example/a/bn.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="bn",NAME="bn",DEFAULT=NO,AUTOSELECT=YES
            #EXT-X-MEDIA:URI="https://cdn.example/a/es.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="es-US",NAME="es-US",DEFAULT=NO,AUTOSELECT=YES
            #EXT-X-MEDIA:URI="https://cdn.example/a/ar.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="ar",NAME="العربية - dubbed",DEFAULT=NO,AUTOSELECT=YES
            #EXT-X-MEDIA:URI="https://cdn.example/a/hu.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="hu",NAME="magyar - dubbed",DEFAULT=NO,AUTOSELECT=YES
            #EXT-X-MEDIA:URI="https://cdn.example/a/de.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="de",NAME="Deutsch - dubbed",DEFAULT=NO,AUTOSELECT=YES
            #EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=640x360,AUDIO="233"
            https://cdn.example/v/360.m3u8
            """;
        var master = Parse(text);
        Assert.Equal(new[] { "en", "bn", "es-US", "ar", "hu", "de" }, master.Audios.Select(item => item.Key).ToArray());
        Assert.Equal(
            new[]
            {
                "English · Original",
                "Bangla",
                "Spanish (United States)",
                "Arabic · Dubbed",
                "Hungarian · Dubbed",
                "German · Dubbed"
            },
            master.Audios.Select(item => item.Label).ToArray());

        var rewritten = master.Rewrite("ar", 0, Playlist);
        Assert.Contains("NAME=\"العربية - dubbed\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("LANGUAGE=\"ar\"", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void APreferredLanguageUsesTheOriginalTrackWhenItIsMissing()
    {
        var text = """
            #EXTM3U
            #EXT-X-MEDIA:URI="https://cdn.example/a/en.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="en",NAME="English - dubbed",DEFAULT=YES,AUTOSELECT=YES
            #EXT-X-MEDIA:URI="https://cdn.example/a/ja.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="ja",NAME="Japanese - original",DEFAULT=NO,AUTOSELECT=YES
            #EXT-X-MEDIA:URI="https://cdn.example/a/es.m3u8",TYPE=AUDIO,GROUP-ID="233",LANGUAGE="es-US",NAME="Spanish",DEFAULT=NO,AUTOSELECT=YES
            #EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=640x360,AUDIO="233"
            https://cdn.example/v/360.m3u8
            """;
        var master = Parse(text);
        Assert.Equal("en", master.SelectAudio(null));
        Assert.Equal("ja", master.ChooseAudio(null, null));
        Assert.Equal("en", master.ChooseAudio(null, "en"));
        Assert.Equal("es-US", master.ChooseAudio(null, "es"));
        Assert.Equal("ja", master.ChooseAudio(null, "fr"));
        Assert.Equal("en", master.ChooseAudio("en", "ja"));
        Assert.Equal("ja", master.ChooseAudio("tr", "es"));
    }

    [Fact]
    public void AnUnknownHeightUsesTheClosestOne()
    {
        var master = Parse(Master(new[] { "en" }, "en"));
        Assert.Equal(720, master.ResolveHeight(800));
        Assert.Equal(0, master.ResolveHeight(0));
    }

    private static HlsMaster Parse(string text)
    {
        Assert.True(HlsMaster.TryParse(text, out var master));
        return master!;
    }

    private static string Master(string[] languages, string defaultLanguage)
    {
        var lines = new List<string> { "#EXTM3U" };
        foreach (var group in new[] { "233", "234" })
        {
            foreach (var language in languages)
            {
                var chosen = language == defaultLanguage ? "YES" : "NO";
                lines.Add($"#EXT-X-MEDIA:URI=\"https://cdn.example/{group}/{language}.m3u8\",TYPE=AUDIO,GROUP-ID=\"{group}\",LANGUAGE=\"{language}\",NAME=\"{language}\",DEFAULT={chosen},AUTOSELECT={chosen}");
            }
        }

        lines.Add("#EXT-X-MEDIA:URI=\"https://cdn.example/sub/en.m3u8\",TYPE=SUBTITLES,GROUP-ID=\"vtt\",LANGUAGE=\"en\",NAME=\"English\"");
        lines.Add("#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,CODECS=\"avc1.4d401e,mp4a.40.2\",AUDIO=\"233\",SUBTITLES=\"vtt\"");
        lines.Add("https://cdn.example/v/360.m3u8");
        lines.Add("#EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720,CODECS=\"avc1.4d401f,mp4a.40.2\",AUDIO=\"234\",SUBTITLES=\"vtt\"");
        lines.Add("https://cdn.example/v/720.m3u8");
        return string.Join('\n', lines);
    }
}
