using System.Security.Cryptography;
using System.Text;
using LibVLCSharp.Shared;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;
using Xunit;
using Xunit.Abstractions;

namespace PersonalMediaPlayer.Tests;

public class YoutubePlaybackIssueTests
{
    private const string Page = "https://www.youtube.com/watch?v=aqz-KE-bpKQ";

    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly ITestOutputHelper _output;

    public YoutubePlaybackIssueTests(ITestOutputHelper output) => _output = output;

    [Fact(Timeout = 60000)]
    public async Task APublicYoutubeVideoParsesBeforePlayback()
    {
        using var resolveLimit = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var choice = await YoutubeDownloader.ResolvePlaybackAsync(Page, null, resolveLimit.Token);
        var libvlc = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
        LibVLCSharp.Shared.Core.Initialize(libvlc);
        var lib = new LibVLC("--no-video", "--aout=dummy");
        var media = new Media(lib, choice.Media.AbsoluteUri, FromType.FromLocation);
        media.AddOption(":http-user-agent=" + BrowserUserAgent);
        media.AddOption(":http-referrer=" + Page);
        if (choice.Audio is Uri audio)
        {
            media.AddSlave(MediaSlaveType.Audio, 1, audio.AbsoluteUri);
        }

        using var parseLimit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var parse = media.Parse(MediaParseOptions.ParseNetwork, 10_000, parseLimit.Token);
        var finished = await Task.WhenAny(parse, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(finished == parse, "LibVLC did not finish the network parse.");
        var status = await parse;
        var player = new MediaPlayer(lib);
        var errors = 0;
        player.EncounteredError += (_, _) => Interlocked.Increment(ref errors);
        var played = status != MediaParsedStatus.Failed && player.Play(media);
        if (played)
        {
            for (var i = 0; i < 25 && Volatile.Read(ref errors) == 0 && player.Time < 300; i++)
            {
                await Task.Delay(200);
            }
        }

        var playlist = choice.Media.AbsoluteUri.Contains("m3u8", StringComparison.OrdinalIgnoreCase)
            || choice.Media.Host.Contains("manifest", StringComparison.OrdinalIgnoreCase);
        var detail = $"status={status}; played={played}; state={player.State}; time={player.Time}; errors={Volatile.Read(ref errors)}; audioTracks={player.AudioTrackDescription.Length}; host={choice.Media.Host}; separateAudio={choice.Audio is not null}; playlist={playlist}";
        _output.WriteLine(detail);
        _ = Task.Run(() =>
        {
            try
            {
                player.Stop();
                player.Dispose();
                media.Dispose();
                lib.Dispose();
            }
            catch (Exception)
            {
            }
        });
        Assert.True(status != MediaParsedStatus.Failed && played && Volatile.Read(ref errors) == 0 && player.Time >= 300, detail);
    }

    [Fact(Timeout = 90000)]
    public async Task ARewrittenPlaylistStillPlays()
    {
        await PlayRewrittenPageAsync(Page);
    }

    [Fact(Timeout = 90000)]
    public async Task AVideoWithManyAudioLanguagesPlaysAfterThePlaylistIsTrimmed()
    {
        await PlayRewrittenPageAsync("https://www.youtube.com/watch?v=FkXhKu80CWU");
    }

    [Fact(Timeout = 180000)]
    public async Task AnAutomaticYoutubeCaptionDownloads()
    {
        const string page = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
        using var resolveLimit = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var choice = await YoutubeDownloader.ResolveCaptionsAsync(page, resolveLimit.Token);
        var automatic = choice.Where(track => track.Automatic).ToList();
        Assert.True(automatic.Count >= 20, "automatic captions=" + automatic.Count);
        Assert.DoesNotContain(choice, track => track.Language != null && track.Language.Contains("live_chat", StringComparison.OrdinalIgnoreCase));
        var banglaCount = choice.Count(track => track.Label.StartsWith("Bangla", StringComparison.Ordinal));
        Assert.Equal(1, banglaCount);
        foreach (var track in choice)
        {
            Assert.False(string.IsNullOrWhiteSpace(track.Label));
            Assert.False(HasNonLatin(track.Label), track.Language + " " + track.Label);
        }

        var bangla = choice.First(track => track.Automatic && track.Label == "Bangla · Automatic");
        Assert.True(bangla.Translated);
        var german = choice.First(track => string.Equals(track.Language, "de", StringComparison.OrdinalIgnoreCase));
        Assert.False(german.Translated, german.Label);

        var germanCues = await FreshCuesAsync(page, german, 40);
        var banglaCues = await FreshCuesAsync(page, bangla, 120);
        Assert.True(germanCues.Count > 0 && germanCues.Any(cue => cue.Text.Length > 0), "german cues=" + germanCues.Count);
        Assert.True(banglaCues.Count > 0 && banglaCues.Any(cue => cue.Text.Length > 0), "bangla cues=" + banglaCues.Count);
        _output.WriteLine($"captions={choice.Count}; automatic={automatic.Count}; german={german.Language}/{german.Label}/{germanCues.Count}; bangla={bangla.Language}/{bangla.Label}/{banglaCues.Count}");
    }

    private static async Task<IReadOnlyList<SubtitleCue>> FreshCuesAsync(string page, DownloadSubtitle track, int seconds)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page + "\n" + track.Language))).ToLowerInvariant();
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "subtitle-cache", "page-" + hash + ".vtt");
        if (File.Exists(cache))
        {
            File.Delete(cache);
        }

        using var fetchLimit = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var path = await YoutubeDownloader.FetchSubtitleAsync(page, track, fetchLimit.Token);
        Assert.False(string.IsNullOrWhiteSpace(path));
        return SubtitleCues.Parse(await File.ReadAllTextAsync(path!));
    }

    [Fact]
    public void TryingTheStreamAgainKeepsTheRetryCount()
    {
        var retries = 0;
        Assert.True(StreamRetry.TrySpend(ref retries));
        Assert.Equal(1, retries);

        retries = StreamRetry.Clear();
        Assert.Equal(1, retries);

        Assert.False(StreamRetry.TrySpend(ref retries));
    }

    private async Task PlayRewrittenPageAsync(string page)
    {
        using var resolveLimit = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var choice = await YoutubeDownloader.ResolvePlaybackAsync(page, null, resolveLimit.Token);
        var playlist = choice.Media.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
        Assert.True(playlist, "expected an m3u8 playlist from " + choice.Media.Host);
        using var downloadLimit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var body = await HlsMaster.DownloadAsync(choice.Media, new Uri(page), BrowserUserAgent, downloadLimit.Token);
        Assert.NotNull(body);
        Assert.True(HlsMaster.TryParse(body, out var master), "the playlist could not be read");
        Assert.NotNull(master);
        var rewritten = master.Rewrite(null, 0, choice.Media);
        Assert.True(HlsMaster.TryParse(rewritten, out var trimmed), "the trimmed playlist could not be read");
        Assert.NotNull(trimmed);
        Assert.True(trimmed.MaxAudioRenditionsInAnyGroup <= HlsMaster.MaxAudioRenditionsPerGroup);
        if (master.ExceedsAudioLimit)
        {
            Assert.True(trimmed.Audios.Count <= 1);
        }

        var folder = Path.Combine(Path.GetTempPath(), "PersonalMediaPlayer-hls-test");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".m3u8");
        await File.WriteAllTextAsync(path, rewritten);
        var libvlc = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
        LibVLCSharp.Shared.Core.Initialize(libvlc);
        var lib = new LibVLC("--no-video", "--aout=dummy");
        var media = new Media(lib, path, FromType.FromPath);
        media.AddOption(":http-user-agent=" + BrowserUserAgent);
        media.AddOption(":http-referrer=" + page);
        using var parseLimit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var parse = media.Parse(MediaParseOptions.ParseNetwork, 10_000, parseLimit.Token);
        var finished = await Task.WhenAny(parse, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(finished == parse, "LibVLC did not finish the network parse.");
        var status = await parse;
        var player = new MediaPlayer(lib);
        var errors = 0;
        player.EncounteredError += (_, _) => Interlocked.Increment(ref errors);
        var played = status != MediaParsedStatus.Failed && player.Play(media);
        if (played)
        {
            for (var i = 0; i < 25 && Volatile.Read(ref errors) == 0 && player.Time < 300; i++)
            {
                await Task.Delay(200);
            }
        }

        var detail = $"languages={master.Audios.Count}; max={master.MaxAudioRenditionsInAnyGroup}; trimmedLanguages={trimmed.Audios.Count}; trimmedMax={trimmed.MaxAudioRenditionsInAnyGroup}; status={status}; played={played}; state={player.State}; time={player.Time}; errors={Volatile.Read(ref errors)}; audioTracks={player.AudioTrackDescription.Length}";
        _output.WriteLine(detail);
        _ = Task.Run(() =>
        {
            try
            {
                player.Stop();
                player.Dispose();
                media.Dispose();
                lib.Dispose();
                File.Delete(path);
            }
            catch (Exception)
            {
            }
        });
        Assert.True(status != MediaParsedStatus.Failed && played && Volatile.Read(ref errors) == 0 && player.Time >= 300, detail);
    }

    private static bool HasNonLatin(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
            {
                continue;
            }

            var value = rune.Value;
            var latin = value <= 0x024F
                || (value >= 0x1E00 && value <= 0x1EFF)
                || (value >= 0x2C60 && value <= 0x2C7F)
                || (value >= 0xA720 && value <= 0xA7FF)
                || (value >= 0xAB30 && value <= 0xAB6F);
            if (!latin)
            {
                return true;
            }
        }

        return false;
    }
}
