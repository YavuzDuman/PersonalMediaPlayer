using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class PlaybackSelectionTests
{
    [Fact]
    public void ATallerSilentPictureUsesASeparateSoundTrack()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Split",
              "formats": [
                {
                  "format_id": "18",
                  "url": "https://cdn.example/720.mp4",
                  "vcodec": "avc1.4d401e",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 720,
                  "tbr": 500
                },
                {
                  "format_id": "137",
                  "url": "https://cdn.example/1080-video.mp4",
                  "vcodec": "avc1.640028",
                  "acodec": "none",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 1080,
                  "tbr": 2000
                },
                {
                  "format_id": "140",
                  "url": "https://cdn.example/audio.m4a",
                  "vcodec": "none",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "m4a",
                  "tbr": 128
                },
                {
                  "format_id": "sb0",
                  "url": "https://cdn.example/storyboard.jpg",
                  "vcodec": "images",
                  "acodec": "none",
                  "protocol": "https",
                  "ext": "mhtml",
                  "format_note": "storyboard",
                  "height": 90
                }
              ]
            }
            """);

        Assert.Equal("Split", choice.Title);
        Assert.Equal("https://cdn.example/1080-video.mp4", choice.Media.AbsoluteUri);
        Assert.Equal("https://cdn.example/audio.m4a", choice.Audio!.AbsoluteUri);
        Assert.False(choice.Quality.AudioOnly);
        Assert.Contains("137", choice.Quality.Format);
    }

    [Fact]
    public void TheSameHeightPrefersOneStreamWithPictureAndSound()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Combined",
              "formats": [
                {
                  "format_id": "22",
                  "url": "https://cdn.example/720-both.mp4",
                  "vcodec": "avc1.4d401f",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 720,
                  "tbr": 800
                },
                {
                  "format_id": "136",
                  "url": "https://cdn.example/720-video.mp4",
                  "vcodec": "avc1.4d401f",
                  "acodec": "none",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 720,
                  "tbr": 1500
                },
                {
                  "format_id": "140",
                  "url": "https://cdn.example/audio.m4a",
                  "vcodec": "none",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "m4a",
                  "tbr": 128
                }
              ]
            }
            """);

        Assert.Equal("https://cdn.example/720-both.mp4", choice.Media.AbsoluteUri);
        Assert.Null(choice.Audio);
    }

    [Fact]
    public void AnM3u8PlaylistIsTheMediaAddress()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Playlist",
              "formats": [
                {
                  "format_id": "96",
                  "manifest_url": "https://cdn.example/master.m3u8",
                  "url": "https://cdn.example/segment.m4s",
                  "protocol": "m3u8_native",
                  "ext": "mp4",
                  "vcodec": "avc1.4d401f",
                  "acodec": "mp4a.40.2",
                  "height": 1080,
                  "tbr": 2500
                },
                {
                  "format_id": "18",
                  "url": "https://cdn.example/360.mp4",
                  "vcodec": "avc1.4d401e",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 360,
                  "tbr": 400
                }
              ]
            }
            """);

        Assert.Equal("https://cdn.example/master.m3u8", choice.Media.AbsoluteUri);
        Assert.Null(choice.Audio);
    }

    [Fact]
    public void TheSameHeightPrefersAFileOverAPlaylist()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Rank",
              "formats": [
                {
                  "format_id": "hls",
                  "manifest_url": "https://cdn.example/master.m3u8",
                  "url": "https://cdn.example/master.m3u8",
                  "protocol": "m3u8_native",
                  "ext": "mp4",
                  "vcodec": "avc1.4d401f",
                  "acodec": "mp4a.40.2",
                  "height": 720,
                  "tbr": 3000
                },
                {
                  "format_id": "22",
                  "url": "https://cdn.example/720.mp4",
                  "vcodec": "avc1.4d401f",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 720,
                  "tbr": 700
                }
              ]
            }
            """);

        Assert.Equal("https://cdn.example/720.mp4", choice.Media.AbsoluteUri);
        Assert.Null(choice.Audio);
    }

    [Fact]
    public void ADashFileYieldsToAPlaylist()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Dash",
              "formats": [
                {
                  "format_id": "401",
                  "url": "https://cdn.example/2160.mp4",
                  "vcodec": "av01.0.13M.08",
                  "acodec": "none",
                  "protocol": "https",
                  "ext": "mp4",
                  "container": "mp4_dash",
                  "height": 2160,
                  "tbr": 9000
                },
                {
                  "format_id": "140",
                  "url": "https://cdn.example/audio.m4a",
                  "vcodec": "none",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "m4a",
                  "container": "m4a_dash",
                  "tbr": 129
                },
                {
                  "format_id": "312",
                  "manifest_url": "https://cdn.example/master.m3u8",
                  "url": "https://cdn.example/1080.m3u8",
                  "vcodec": "avc1.640028",
                  "acodec": "none",
                  "protocol": "m3u8_native",
                  "ext": "mp4",
                  "height": 1080,
                  "tbr": 4000
                }
              ]
            }
            """);

        Assert.Equal("https://cdn.example/master.m3u8", choice.Media.AbsoluteUri);
        Assert.Null(choice.Audio);
    }

    [Fact]
    public void ADashFileRemainsWhenItIsTheOnlyPicture()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Dash only",
              "formats": [
                {
                  "format_id": "137",
                  "url": "https://cdn.example/1080.mp4",
                  "vcodec": "avc1.640028",
                  "acodec": "none",
                  "protocol": "https",
                  "ext": "mp4",
                  "container": "mp4_dash",
                  "height": 1080,
                  "tbr": 2000
                },
                {
                  "format_id": "140",
                  "url": "https://cdn.example/audio.m4a",
                  "vcodec": "none",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "m4a",
                  "container": "m4a_dash",
                  "tbr": 128
                }
              ]
            }
            """);

        Assert.Equal("https://cdn.example/1080.mp4", choice.Media.AbsoluteUri);
        Assert.Equal("https://cdn.example/audio.m4a", choice.Audio!.AbsoluteUri);
    }

    [Fact]
    public void ALicenseProtectedPageIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => YoutubeDownloader.ReadPlayback("""
            {
              "title": "Protected",
              "formats": [
                {
                  "format_id": "1",
                  "url": "https://cdn.example/protected.mp4",
                  "vcodec": "avc1",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 1080,
                  "has_drm": true
                }
              ]
            }
            """));

        Assert.Equal(YoutubeDownloader.LicenseProtectedMessage, error.Message);
    }

    [Fact]
    public void AClearStreamIsKeptWhenAnotherFormatIsProtected()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Mixed",
              "formats": [
                {
                  "format_id": "drm",
                  "url": "https://cdn.example/drm.mp4",
                  "vcodec": "avc1",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 1080,
                  "has_drm": true
                },
                {
                  "format_id": "18",
                  "url": "https://cdn.example/720.mp4",
                  "vcodec": "avc1.4d401e",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 720,
                  "tbr": 500
                }
              ]
            }
            """);

        Assert.Equal("https://cdn.example/720.mp4", choice.Media.AbsoluteUri);
        Assert.Null(choice.Audio);
    }

    [Fact]
    public void SubtitlesKeepUploadedTracksAndTheSpokenAutomaticTrack()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Captions",
              "language": "en",
              "formats": [
                {
                  "format_id": "18",
                  "url": "https://cdn.example/video.mp4",
                  "vcodec": "avc1.4d401e",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 360
                }
              ],
              "subtitles": {
                "en": [{ "name": "English", "ext": "vtt" }],
                "de": [{ "name": "German", "ext": "vtt" }],
                "live_chat": [{ "name": "Live chat", "ext": "json" }]
              },
              "automatic_captions": {
                "en-orig": [{ "name": "English (original)", "ext": "vtt", "url": "https://cdn.example/auto" }],
                "tr": [{ "name": "Turkish", "ext": "vtt", "url": "https://cdn.example/tr?tlang=tr" }]
              }
            }
            """);

        Assert.Equal("en, de, en-orig, tr", string.Join(", ", choice.Subtitles.Select(track => track.Language)));
        Assert.Equal("English, German, English · Automatic, Turkish · Automatic", string.Join(", ", choice.Subtitles.Select(track => track.Label)));
        Assert.False(choice.Subtitles[0].Automatic);
        Assert.False(choice.Subtitles[1].Automatic);
        Assert.True(choice.Subtitles[2].Automatic);
        Assert.False(choice.Subtitles[2].Translated);
        Assert.True(choice.Subtitles[3].Automatic);
        Assert.True(choice.Subtitles[3].Translated);
    }

    [Fact]
    public void AutomaticCaptionsKeepTranslationsAndUseEnglishNames()
    {
        var choice = YoutubeDownloader.ReadPlayback("""
            {
              "title": "Names",
              "language": "en",
              "formats": [
                {
                  "format_id": "18",
                  "url": "https://cdn.example/video.mp4",
                  "vcodec": "avc1.4d401e",
                  "acodec": "mp4a.40.2",
                  "protocol": "https",
                  "ext": "mp4",
                  "height": 360
                }
              ],
              "subtitles": {
                "ar": [{ "name": "العربية", "ext": "vtt" }],
                "nl-NL": [{ "name": "Dutch (Netherlands)", "ext": "vtt" }],
                "live_chat": [{ "name": "Live chat", "ext": "json" }]
              },
              "automatic_captions": {
                "en": [{ "name": "English", "ext": "vtt", "url": "https://cdn.example/en" }],
                "en-orig": [{ "name": "English (Original)", "ext": "vtt", "url": "https://cdn.example/orig" }],
                "en-ar": [{ "name": "English from Arabic", "ext": "vtt", "url": "https://cdn.example/en-ar?tlang=en" }],
                "bn": [{ "name": "bn", "ext": "vtt", "url": "https://cdn.example/bn?tlang=bn" }],
                "bn-ar": [{ "name": "Bangla from Arabic", "ext": "vtt", "url": "https://cdn.example/bn-ar?tlang=bn" }],
                "bn-nl-NL": [{ "name": "Bangla from Dutch (Netherlands)", "ext": "vtt", "url": "https://cdn.example/bn-nl?tlang=bn" }],
                "es-US": [{ "name": "es-US", "ext": "vtt", "url": "https://cdn.example/es?tlang=es-US" }],
                "es-419": [{ "name": "es-419", "ext": "vtt", "url": "https://cdn.example/es419?tlang=es-419" }],
                "zh": [{ "ext": "vtt", "url": "https://cdn.example/zh" }],
                "zh-Hans": [{ "name": "unknown", "ext": "vtt", "url": "https://cdn.example/hans?tlang=zh-Hans" }],
                "de": [{ "name": "German", "ext": "vtt", "url": "https://cdn.example/de" }],
                "de-en": [{ "name": "German from English", "ext": "vtt", "url": "https://cdn.example/de-en?tlang=de" }],
                "hi": [{ "name": "हिन्दी", "ext": "vtt", "url": "https://cdn.example/hi?tlang=hi" }]
              }
            }
            """);

        Assert.Equal(
            "ar, nl-NL, en-orig, bn, zh, zh-Hans, de, hi, es-419, es-US",
            string.Join(", ", choice.Subtitles.Select(track => track.Language)));
        Assert.Equal(
            "Arabic, Dutch (Netherlands), English · Automatic, Bangla · Automatic, Chinese · Automatic, Chinese (Simplified) · Automatic, German · Automatic, Hindi · Automatic, Spanish (Latin America) · Automatic, Spanish (United States) · Automatic",
            string.Join(", ", choice.Subtitles.Select(track => track.Label)));
        Assert.DoesNotContain(choice.Subtitles, track => track.Language is "live_chat" or "en" or "en-ar" or "bn-ar" or "bn-nl-NL" or "de-en");
        Assert.All(choice.Subtitles.Take(2), track => Assert.False(track.Translated));
        Assert.False(choice.Subtitles.Single(track => track.Language == "en-orig").Translated);
        Assert.False(choice.Subtitles.Single(track => track.Language == "zh").Translated);
        Assert.False(choice.Subtitles.Single(track => track.Language == "de").Translated);
        Assert.True(choice.Subtitles.Single(track => track.Language == "bn").Translated);
        Assert.True(choice.Subtitles.Single(track => track.Language == "es-US").Translated);
        Assert.All(choice.Subtitles.Skip(2), track => Assert.True(track.Automatic));
    }

    [Fact]
    public void ResolveIsOnOnlyWhenTheLaunchAsksForIt()
    {
        var page = "https://www.youtube.com/watch?v=abc";
        var asked = "personalmediaplayer://play/?url=" + Uri.EscapeDataString(page) + "&resolve=1&t=12.5";
        Assert.True(StreamHandoff.TryReadLaunch(asked, out var launch));
        Assert.True(launch.Resolve);
        Assert.Equal(page, launch.Url!.AbsoluteUri);
        Assert.Null(launch.Referrer);
        Assert.Equal(12_500, launch.StartMs);

        var plain = "personalmediaplayer://play/?url=" + Uri.EscapeDataString("https://example.com/a.mp4");
        Assert.True(StreamHandoff.TryReadLaunch(plain, out var direct));
        Assert.False(direct.Resolve);

        var zero = plain + "&resolve=0";
        Assert.True(StreamHandoff.TryReadLaunch(zero, out var off));
        Assert.False(off.Resolve);
    }

    [Fact]
    public void CaptionClocksCanOmitHours()
    {
        var cues = SubtitleCues.Parse("""
            1
            00:00:01,500 --> 00:00:02,000
            Hello there

            00:01.250 --> 00:02.500
            Short clock
            """);

        Assert.Equal(2, cues.Count);
        Assert.Equal(1_500, cues[0].StartMs);
        Assert.Equal(2_000, cues[0].EndMs);
        Assert.Equal("Hello there", cues[0].Text);
        Assert.Equal(1_250, cues[1].StartMs);
        Assert.Equal(2_500, cues[1].EndMs);
        Assert.Equal("Short clock", cues[1].Text);
    }
}
