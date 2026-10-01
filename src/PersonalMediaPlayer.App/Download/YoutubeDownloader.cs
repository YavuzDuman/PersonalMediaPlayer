using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalMediaPlayer.App.Playback;

[assembly: InternalsVisibleTo("PersonalMediaPlayer.Tests")]

namespace PersonalMediaPlayer.App.Download;

public sealed record DownloadQuality(string Label, string Format, bool AudioOnly);

public sealed record DownloadSubtitle(string? Language, string Label, bool Automatic, bool Translated = false);

public sealed record DownloadListing(string Title, IReadOnlyList<DownloadQuality> Qualities, IReadOnlyList<DownloadSubtitle> Subtitles);

public sealed record PlaylistVideo(string Title, string Url, string? Length);

public sealed record PlaylistListing(string Title, IReadOnlyList<PlaylistVideo> Videos, int TotalCount);

internal sealed record PlaybackSource(string Title, Uri Media, Uri? Audio, DownloadQuality Quality, IReadOnlyList<DownloadSubtitle> Subtitles);

internal static class YoutubeDownloader
{
    internal const string LicenseProtectedMessage = "This video is protected by a license and stays in the browser.";

    internal const string NoPlayableStreamMessage = "This page did not offer a playable stream.";

    // YouTube rejects a translated caption until this lookup's session is about a minute old.
    // The wait has to happen inside the same run that downloads the caption.
    private const int TranslatedSubtitleDelaySeconds = 65;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly Regex ProgressPattern = new(@"\[download\]\s+(?<percent>\d+(?:\.\d+)?)%", RegexOptions.Compiled);

    public static bool TryNormalize(string text, out Uri uri)
    {
        uri = null!;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var host = parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? parsed.Host[4..]
            : parsed.Host;
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
            || host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        return false;
    }

    internal static bool SameLinkText(string lookedUp, string current)
        => string.Equals(lookedUp.Trim(), current.Trim(), StringComparison.Ordinal);

    internal const int PlaylistListLimit = 200;

    public static bool IsPlaylist(string text)
    {
        if (!TryNormalize(text, out var uri))
        {
            return false;
        }

        if (uri.AbsolutePath.Contains("/playlist", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(Query(uri, "list"));
        }

        var list = Query(uri, "list");
        var video = Query(uri, "v");
        var shortVideo = uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Trim('/').Length > 0;
        return !string.IsNullOrEmpty(list) && string.IsNullOrEmpty(video) && !shortVideo;
    }

    public static IReadOnlyList<DownloadQuality> PlaylistQualities()
        =>
        [
            new DownloadQuality("Best available", "bestvideo+bestaudio/best", false),
            new DownloadQuality("1080p", "bestvideo[height<=1080]+bestaudio/best[height<=1080]", false),
            new DownloadQuality("720p", "bestvideo[height<=720]+bestaudio/best[height<=720]", false),
            new DownloadQuality("480p", "bestvideo[height<=480]+bestaudio/best[height<=480]", false),
            new DownloadQuality("360p", "bestvideo[height<=360]+bestaudio/best[height<=360]", false),
            new DownloadQuality("Audio only", "bestaudio/best", true)
        ];

    public static async Task<PlaylistListing> ListPlaylistAsync(string url, IProgress<string>? status, CancellationToken cancellationToken)
    {
        status?.Report("Reading the playlist…");
        var ytdlp = await EnsureYtDlpAsync(status, cancellationToken);
        var runtime = await EnsureJsRuntimeAsync(status, cancellationToken);
        var args = new List<string>
        {
            "--yes-playlist",
            "--flat-playlist",
            "--no-warnings",
            "-4",
            "--playlist-end",
            PlaylistListLimit.ToString(CultureInfo.InvariantCulture)
        };
        if (runtime is not null)
        {
            args.Add("--js-runtimes");
            args.Add(runtime);
        }

        args.Add("--dump-single-json");
        args.Add(url);
        var json = await RunAsync(ytdlp, args, null, cancellationToken);
        return ReadPlaylist(JsonBody(json));
    }

    internal static PlaylistListing ReadPlaylist(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var title = Text(root, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Playlist";
        }

        var videos = new List<PlaylistVideo>();
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var address = EntryUrl(entry);
                if (address is null)
                {
                    continue;
                }

                var name = Text(entry, "title");
                videos.Add(new PlaylistVideo(
                    string.IsNullOrWhiteSpace(name) ? "Video" : name.Trim(),
                    address,
                    LengthLabel(entry)));
            }
        }

        if (videos.Count == 0)
        {
            throw new InvalidOperationException("This playlist has no videos.");
        }

        var total = videos.Count;
        if (root.TryGetProperty("playlist_count", out var count) && count.TryGetInt32(out var listed) && listed > total)
        {
            total = listed;
        }

        return new PlaylistListing(title.Trim(), videos, total);
    }

    private static string? EntryUrl(JsonElement entry)
    {
        foreach (var name in new[] { "webpage_url", "url" })
        {
            var text = Text(entry, name);
            if (text is not null && text.StartsWith("http", StringComparison.OrdinalIgnoreCase) && TryNormalize(text, out var uri))
            {
                return uri.AbsoluteUri;
            }
        }

        var id = Text(entry, "id");
        if (string.IsNullOrWhiteSpace(id) || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            return null;
        }

        return "https://www.youtube.com/watch?v=" + id;
    }

    private static string? LengthLabel(JsonElement entry)
    {
        if (!entry.TryGetProperty("duration", out var duration) || duration.ValueKind != JsonValueKind.Number || !duration.TryGetDouble(out var seconds) || seconds <= 0)
        {
            return null;
        }

        var total = (int)Math.Round(seconds);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{secs:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{secs:00}");
    }

    private static string? Query(Uri uri, string key)
    {
        var query = uri.Query;
        if (query.StartsWith('?'))
        {
            query = query[1..];
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            if (split.Length == 2 && split[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(split[1]);
            }
        }

        return null;
    }

    public static async Task<DownloadListing> ListAsync(string url, IProgress<string>? status, CancellationToken cancellationToken)
    {
        var ytdlp = await EnsureYtDlpAsync(status, cancellationToken);
        var runtime = await EnsureJsRuntimeAsync(status, cancellationToken);
        var json = await RunAsync(ytdlp, CommonArgs(runtime, "--write-auto-subs", "--dump-single-json", url), null, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var title = root.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Video";
        }

        return new DownloadListing(title, ReadQualities(root), ReadSubtitles(root));
    }

    internal static async Task<PlaybackSource> ResolvePlaybackAsync(string url, IProgress<string>? status, CancellationToken cancellationToken)
    {
        var ytdlp = await EnsureYtDlpAsync(status, cancellationToken);
        var runtime = await EnsureJsRuntimeAsync(status, cancellationToken);
        string json;
        try
        {
            json = await RunAsync(ytdlp, CommonArgs(runtime, "--write-auto-subs", "--dump-single-json", url), null, cancellationToken);
        }
        catch (InvalidOperationException ex) when (MentionsDrm(ex.Message))
        {
            throw new InvalidOperationException(LicenseProtectedMessage, ex);
        }

        try
        {
            return ReadPlayback(JsonBody(json));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The page lookup did not return a video.");
        }
    }

    internal static PlaybackSource ReadPlayback(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var title = root.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Video";
        }

        var candidates = new List<PlayCandidate>();
        var sawDrmVideo = false;
        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var format in formats.EnumerateArray())
            {
                if (IsStoryboard(format) || IsImage(format))
                {
                    continue;
                }

                var videoCodec = Text(format, "vcodec");
                var audioCodec = Text(format, "acodec");
                var hasVideo = HasCodec(videoCodec) && !videoCodec!.Equals("images", StringComparison.OrdinalIgnoreCase);
                var hasAudio = HasCodec(audioCodec);
                if (!hasVideo && !hasAudio)
                {
                    continue;
                }

                if (IsDrm(format))
                {
                    if (hasVideo)
                    {
                        sawDrmVideo = true;
                    }

                    continue;
                }

                var address = PlayableAddress(format);
                if (address is null)
                {
                    continue;
                }

                var height = 0;
                if (hasVideo && format.TryGetProperty("height", out var heightElement) && heightElement.ValueKind == JsonValueKind.Number)
                {
                    height = heightElement.GetInt32();
                }

                var protocol = Text(format, "protocol");
                var extension = Text(format, "ext");
                var score = format.TryGetProperty("tbr", out var rate) && rate.ValueKind == JsonValueKind.Number ? rate.GetDouble() : 0;
                var rank = hasVideo
                    ? FormatRank(protocol, extension, videoCodec, hasAudio)
                    : FormatRank(protocol, extension, audioCodec, true);
                candidates.Add(new PlayCandidate(height, rank, score, address, hasVideo, hasAudio, IsFragmented(format)));
            }
        }

        // LibVLC stays at time zero on a DASH file URL, so a manifest or progressive file wins when one exists.
        var playable = candidates.Any(item => item.Video && !item.Fragmented)
            ? candidates.Where(item => !item.Fragmented)
            : candidates;
        var combined = Best(playable.Where(item => item.Video && item.Audio));
        var picture = Best(playable.Where(item => item.Video && !item.Audio));
        var sound = Best(playable.Where(item => !item.Video && item.Audio));
        if (combined is null && picture is null)
        {
            throw new InvalidOperationException(sawDrmVideo ? LicenseProtectedMessage : NoPlayableStreamMessage);
        }

        PlayCandidate chosen;
        Uri? audio = null;
        if (picture is PlayCandidate silent && sound is PlayCandidate track && (combined is not PlayCandidate both || silent.Height > both.Height))
        {
            chosen = silent;
            audio = track.Address;
        }
        else if (combined is PlayCandidate ready)
        {
            chosen = ready;
        }
        else
        {
            chosen = picture!.Value;
        }

        var qualities = ReadQualities(root);
        var quality = qualities.FirstOrDefault(item => !item.AudioOnly)
            ?? new DownloadQuality("Best available", "bestvideo+bestaudio/best", false);
        return new PlaybackSource(title, chosen.Address, audio, quality, ReadSubtitles(root));
    }

    internal static async Task<string?> FetchSubtitleAsync(string pageUrl, DownloadSubtitle subtitle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subtitle.Language))
        {
            return null;
        }

        var cache = PageSubtitlePath(pageUrl, subtitle.Language);
        if (File.Exists(cache) && new FileInfo(cache).Length > 0)
        {
            return cache;
        }

        var ytdlp = await EnsureYtDlpAsync(null, cancellationToken);
        var runtime = await EnsureJsRuntimeAsync(null, cancellationToken);
        var folder = Path.Combine(Path.GetTempPath(), "PersonalMediaPlayer-subs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var arguments = CommonArgs(runtime, "--skip-download");
            AddSubtitleRequest(arguments, subtitle);
            arguments.Add("--sub-format");
            arguments.Add("vtt/srt/best");
            arguments.Add("-o");
            arguments.Add(Path.Combine(folder, "caption"));
            arguments.Add(pageUrl);
            try
            {
                await RunAsync(ytdlp, arguments, null, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // A subtitle file can still be on disk when yt-dlp exits with a warning.
            }

            return StoreSubtitle(folder, cache);
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    public static string CreateOutputPath(bool audioOnly)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PersonalMediaPlayer-download", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, audioOnly ? "video.m4a" : "video.mp4");
    }

    public static async Task<string> DownloadAsync(string url, DownloadQuality quality, DownloadSubtitle? subtitle, string title, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var ytdlp = await EnsureYtDlpAsync(null, cancellationToken);
        var runtime = await EnsureJsRuntimeAsync(null, cancellationToken);
        var folder = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var arguments = CommonArgs(runtime, "--newline", "--continue", "--ffmpeg-location", ToolsDirectory(), "-f", quality.Format, "-o", destination);
        if (quality.AudioOnly)
        {
            arguments.Add("-x");
            arguments.Add("--audio-format");
            arguments.Add("m4a");
        }
        else
        {
            arguments.Add("--merge-output-format");
            arguments.Add("mp4");
        }

        if (subtitle?.Language is not null && !quality.AudioOnly)
        {
            AddSubtitleRequest(arguments, subtitle);
            arguments.Add("--embed-subs");
            arguments.Add("--convert-subs");
            arguments.Add("srt");
        }

        arguments.Add(url);
        try
        {
            await RunAsync(ytdlp, arguments, progress, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && subtitle?.Language is not null && IsCompleteMedia(destination))
        {
            return destination;
        }
        _ = title;
        return ResolveProducedFile(destination);
    }

    internal static string ResolveProducedFile(string destination)
    {
        if (IsCompleteMedia(destination))
        {
            return destination;
        }

        var folder = Path.GetDirectoryName(destination);
        var stem = Path.GetFileNameWithoutExtension(destination);
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(stem) || !Directory.Exists(folder))
        {
            throw new InvalidOperationException("The download did not produce a file.");
        }

        var produced = Directory.EnumerateFiles(folder)
            .Select(path => new FileInfo(path))
            .Where(info => BelongsToDownload(info, stem))
            .OrderByDescending(info => info.Length)
            .ThenByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault();
        if (produced is null)
        {
            throw new InvalidOperationException("The download did not produce a file.");
        }

        return produced.FullName;
    }

    private static bool BelongsToDownload(FileInfo info, string stem)
    {
        if (!IsCompleteMedia(info.FullName))
        {
            return false;
        }

        var name = info.Name;
        if (name.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".ass", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".ttml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!name.StartsWith(stem, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = name[stem.Length..];
        return !rest.StartsWith(".f", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompleteMedia(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var name = Path.GetFileName(path);
        if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return new FileInfo(path).Length >= 1024;
    }

    private static List<string> CommonArgs(string? runtime, params string[] more)
    {
        var args = new List<string> { "--no-playlist", "--no-warnings", "-4" };
        if (runtime is not null)
        {
            args.Add("--js-runtimes");
            args.Add(runtime);
        }

        args.AddRange(more);
        return args;
    }

    public static string FileName(string title, bool audioOnly)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(character => invalid.Contains(character) ? ' ' : character).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].Trim();
        }

        if (cleaned.Length == 0)
        {
            cleaned = audioOnly ? "Audio" : "Video";
        }

        return cleaned;
    }

    private static IReadOnlyList<DownloadQuality> ReadQualities(JsonElement root)
    {
        var videos = new Dictionary<int, (int Rank, double Score, string Id, bool HasAudio, long? Bytes, bool Approx)>();
        string? audioId = null;
        long? audioBytes = null;
        var audioApprox = false;
        var audioRank = int.MinValue;
        var audioScore = -1d;
        if (root.TryGetProperty("formats", out var formats))
        {
            foreach (var format in formats.EnumerateArray())
            {
                if (!format.TryGetProperty("format_id", out var idElement))
                {
                    continue;
                }

                var id = idElement.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var videoCodec = Text(format, "vcodec");
                var audioCodec = Text(format, "acodec");
                var protocol = Text(format, "protocol");
                var extension = Text(format, "ext");
                var note = Text(format, "format_note");
                var hasVideo = !string.IsNullOrEmpty(videoCodec) && !videoCodec.Equals("none", StringComparison.OrdinalIgnoreCase) && !videoCodec.Equals("images", StringComparison.OrdinalIgnoreCase);
                var hasAudio = !string.IsNullOrEmpty(audioCodec) && !audioCodec.Equals("none", StringComparison.OrdinalIgnoreCase);
                var score = format.TryGetProperty("tbr", out var rate) && rate.ValueKind == JsonValueKind.Number ? rate.GetDouble() : 0;
                if (note?.Contains("storyboard", StringComparison.OrdinalIgnoreCase) == true)
                {
                    continue;
                }

                if (hasVideo && format.TryGetProperty("height", out var heightElement) && heightElement.ValueKind == JsonValueKind.Number)
                {
                    var height = heightElement.GetInt32();
                    var rank = FormatRank(protocol, extension, videoCodec, hasAudio);
                    if (height >= 144 && (!videos.TryGetValue(height, out var current) || rank > current.Rank || (rank == current.Rank && score > current.Score)))
                    {
                        videos[height] = (rank, score, id, hasAudio, ByteCount(format), !HasExactSize(format));
                    }
                }
                else if (hasAudio)
                {
                    var rank = FormatRank(protocol, extension, audioCodec, true);
                    if (rank > audioRank || (rank == audioRank && score >= audioScore))
                    {
                        audioRank = rank;
                        audioScore = score;
                        audioId = id;
                        audioBytes = ByteCount(format);
                        audioApprox = !HasExactSize(format);
                    }
                }
            }
        }

        var qualities = videos
            .OrderByDescending(pair => pair.Key)
            .Select(pair =>
            {
                var bytes = pair.Value.Bytes;
                var approx = pair.Value.Approx || bytes is null;
                if (!pair.Value.HasAudio)
                {
                    if (bytes is not null && audioBytes is not null)
                    {
                        bytes += audioBytes;
                    }

                    approx = approx || audioApprox || audioBytes is null;
                }

                return new DownloadQuality($"{pair.Key}p{SizeSuffix(bytes, approx && bytes is not null)}", pair.Value.HasAudio ? pair.Value.Id : pair.Value.Id + "+bestaudio[ext=m4a]/bestaudio/best", false);
            })
            .ToList();
        if (audioId is not null)
        {
            var bitrate = audioScore > 0 ? $" · {audioScore.ToString("0", CultureInfo.CurrentCulture)} kbps" : string.Empty;
            qualities.Add(new DownloadQuality("Audio only" + bitrate + SizeSuffix(audioBytes, audioApprox && audioBytes is not null), audioId, true));
        }

        if (qualities.Count == 0)
        {
            qualities.Add(new DownloadQuality("Best available", "bestvideo+bestaudio/best", false));
        }

        return qualities;
    }

    private static IReadOnlyList<DownloadSubtitle> ReadSubtitles(JsonElement root)
    {
        var found = new List<DownloadSubtitle>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTracks(root, "subtitles", automatic: false, seen, found);
        AddAutomatic(root, seen, found);
        return found;
    }

    private static void AddTracks(JsonElement root, string property, bool automatic, HashSet<string> seen, List<DownloadSubtitle> found)
    {
        if (!root.TryGetProperty(property, out var map) || map.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var language in map.EnumerateObject())
        {
            if (language.Name.Contains("live_chat", StringComparison.OrdinalIgnoreCase) || !seen.Add(language.Name))
            {
                continue;
            }

            found.Add(new DownloadSubtitle(language.Name, LanguageLabels.ForCaption(language.Name, GivenName(language.Value), automatic), automatic));
        }
    }

    private static void AddAutomatic(JsonElement root, HashSet<string> seen, List<DownloadSubtitle> found)
    {
        if (!root.TryGetProperty("automatic_captions", out var map) || map.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var videoLanguage = root.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String
            ? language.GetString()
            : null;
        JsonProperty? spoken = null;
        var rest = new List<JsonProperty>();
        foreach (var entry in map.EnumerateObject())
        {
            if (entry.Name.Contains("live_chat", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsSpokenAutomatic(entry.Name, videoLanguage, entry.Value))
            {
                if (spoken is null || entry.Name.EndsWith("-orig", StringComparison.OrdinalIgnoreCase))
                {
                    spoken = entry;
                }

                continue;
            }

            rest.Add(entry);
        }

        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (spoken is { } original)
        {
            groups.Add(LanguageLabels.Group(original.Name, GivenName(original.Value)));
            if (seen.Add(original.Name))
            {
                found.Add(new DownloadSubtitle(original.Name, LanguageLabels.ForCaption(original.Name, GivenName(original.Value), true), true));
            }
        }

        var winners = new Dictionary<string, JsonProperty>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in rest)
        {
            var group = LanguageLabels.Group(entry.Name, GivenName(entry.Value));
            if (groups.Contains(group))
            {
                continue;
            }

            if (!winners.TryGetValue(group, out var current) || PrefersCaption(entry, current))
            {
                winners[group] = entry;
            }
        }

        foreach (var entry in winners.Values
            .Select(entry => (Entry: entry, Label: LanguageLabels.ForCaption(entry.Name, GivenName(entry.Value), true)))
            .OrderBy(item => item.Label, StringComparer.InvariantCultureIgnoreCase))
        {
            if (!seen.Add(entry.Entry.Name))
            {
                continue;
            }

            found.Add(new DownloadSubtitle(entry.Entry.Name, entry.Label, true, IsForeignTranslation(entry.Entry.Value)));
        }
    }

    private static bool PrefersCaption(JsonProperty candidate, JsonProperty current)
    {
        var candidateTranslated = IsForeignTranslation(candidate.Value);
        var currentTranslated = IsForeignTranslation(current.Value);
        if (candidateTranslated != currentTranslated)
        {
            return !candidateTranslated;
        }

        var group = LanguageLabels.Group(candidate.Name, GivenName(candidate.Value));
        var candidateBase = candidate.Name.Equals(group, StringComparison.OrdinalIgnoreCase);
        var currentBase = current.Name.Equals(group, StringComparison.OrdinalIgnoreCase);
        return candidateBase && !currentBase;
    }

    private static void AddSubtitleRequest(List<string> arguments, DownloadSubtitle subtitle)
    {
        if (subtitle.Translated)
        {
            arguments.Add("--sleep-subtitles");
            arguments.Add(TranslatedSubtitleDelaySeconds.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add(subtitle.Automatic ? "--write-auto-subs" : "--write-subs");
        arguments.Add("--sub-langs");
        arguments.Add(subtitle.Language!);
    }

    private static bool IsSpokenAutomatic(string code, string? videoLanguage, JsonElement formats)
    {
        if (code.Contains("live_chat", StringComparison.OrdinalIgnoreCase) || IsForeignTranslation(formats))
        {
            return false;
        }

        var bare = code.EndsWith("-orig", StringComparison.OrdinalIgnoreCase) ? code[..^5] : code;
        if (string.IsNullOrWhiteSpace(videoLanguage))
        {
            return !bare.Contains('-') && code.EndsWith("-orig", StringComparison.OrdinalIgnoreCase);
        }

        return SameLanguage(bare, videoLanguage);
    }

    private static bool IsForeignTranslation(JsonElement formats)
    {
        if (formats.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var format in formats.EnumerateArray())
        {
            var url = Text(format, "url");
            if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrEmpty(QueryValue(url, "tlang")))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameLanguage(string code, string videoLanguage)
        => code.Equals(videoLanguage, StringComparison.OrdinalIgnoreCase)
            || code.Split('-')[0].Equals(videoLanguage.Split('-')[0], StringComparison.OrdinalIgnoreCase);

    private static string? GivenName(JsonElement formats)
    {
        if (formats.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var format in formats.EnumerateArray())
        {
            var given = Text(format, "name");
            if (string.IsNullOrWhiteSpace(given) || given.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return given;
        }

        return null;
    }

    private static string? QueryValue(string url, string key)
    {
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
        {
            return null;
        }

        foreach (var part in url[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            if (split.Length == 2 && split[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(split[1]);
            }
        }

        return null;
    }

    private static async Task<string> EnsureYtDlpAsync(IProgress<string>? status, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ToolsDirectory());
        var path = Path.Combine(ToolsDirectory(), "yt-dlp.exe");
        if (File.Exists(path))
        {
            return path;
        }

        status?.Report("Getting the downloader…");
        await DownloadFileAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", path, cancellationToken);
        return path;
    }

    private static async Task<string?> EnsureJsRuntimeAsync(IProgress<string>? status, CancellationToken cancellationToken)
    {
        var existing = FindRuntime("deno") ?? FindRuntime("node");
        if (existing is not null)
        {
            return existing;
        }

        var deno = Path.Combine(ToolsDirectory(), "deno.exe");
        if (!File.Exists(deno))
        {
            status?.Report("Getting the YouTube runtime…");
            var zipName = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "deno-aarch64-pc-windows-msvc.zip"
                : "deno-x86_64-pc-windows-msvc.zip";
            var zipPath = Path.Combine(ToolsDirectory(), "deno.zip");
            await DownloadFileAsync("https://github.com/denoland/deno/releases/latest/download/" + zipName, zipPath, cancellationToken);
            ZipFile.ExtractToDirectory(zipPath, ToolsDirectory(), overwriteFiles: true);
            File.Delete(zipPath);
        }

        return File.Exists(deno) ? "deno:" + deno : null;
    }

    private static string? FindRuntime(string name)
    {
        var bundled = Path.Combine(ToolsDirectory(), name + ".exe");
        if (File.Exists(bundled))
        {
            return name + ":" + bundled;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim(), name + ".exe");
            if (File.Exists(candidate))
            {
                return name + ":" + candidate;
            }
        }

        return null;
    }

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + ".download";
        try
        {
            await using (var stream = await Http.GetStreamAsync(url, cancellationToken))
            await using (var file = File.Create(temporary))
            {
                await stream.CopyToAsync(file, cancellationToken);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    private static long? ByteCount(JsonElement format)
    {
        if (format.TryGetProperty("filesize", out var exact) && exact.ValueKind == JsonValueKind.Number && exact.GetInt64() > 0)
        {
            return exact.GetInt64();
        }

        if (format.TryGetProperty("filesize_approx", out var approx) && approx.ValueKind == JsonValueKind.Number && approx.GetInt64() > 0)
        {
            return approx.GetInt64();
        }

        return null;
    }

    private static bool HasExactSize(JsonElement format)
        => format.TryGetProperty("filesize", out var exact) && exact.ValueKind == JsonValueKind.Number && exact.GetInt64() > 0;

    private static string SizeSuffix(long? bytes, bool approximate)
    {
        if (bytes is null or <= 0)
        {
            return string.Empty;
        }

        var value = (double)bytes.Value;
        var unit = "B";
        if (value >= 1024d * 1024d * 1024d)
        {
            value /= 1024d * 1024d * 1024d;
            unit = "GB";
        }
        else if (value >= 1024d * 1024d)
        {
            value /= 1024d * 1024d;
            unit = "MB";
        }
        else if (value >= 1024d)
        {
            value /= 1024d;
            unit = "KB";
        }

        var number = value >= 10 ? value.ToString("0", CultureInfo.CurrentCulture) : value.ToString("0.0", CultureInfo.CurrentCulture);
        return " · " + (approximate ? "about " : string.Empty) + number + " " + unit;
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private readonly record struct PlayCandidate(int Height, int Rank, double Score, Uri Address, bool Video, bool Audio, bool Fragmented);

    private static bool IsFragmented(JsonElement format)
    {
        var protocol = Text(format, "protocol");
        var container = Text(format, "container");
        if (protocol?.Contains("dash", StringComparison.OrdinalIgnoreCase) == true
            || container?.Contains("dash", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return format.TryGetProperty("init_range", out var range) && range.ValueKind == JsonValueKind.Object;
    }

    private static PlayCandidate? Best(IEnumerable<PlayCandidate> items)
    {
        PlayCandidate? best = null;
        foreach (var item in items)
        {
            if (best is not PlayCandidate current
                || item.Height > current.Height
                || (item.Height == current.Height && (item.Rank > current.Rank || (item.Rank == current.Rank && item.Score > current.Score))))
            {
                best = item;
            }
        }

        return best;
    }

    private static bool IsStoryboard(JsonElement format)
        => Text(format, "format_note")?.Contains("storyboard", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsImage(JsonElement format)
    {
        var codec = Text(format, "vcodec");
        var extension = Text(format, "ext");
        return codec?.Equals("images", StringComparison.OrdinalIgnoreCase) == true
            || extension is "jpg" or "jpeg" or "png" or "webp" or "mhtml";
    }

    private static bool HasCodec(string? codec)
        => !string.IsNullOrEmpty(codec) && !codec.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static bool IsDrm(JsonElement format)
    {
        if (format.TryGetProperty("has_drm", out var flag))
        {
            if (flag.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (flag.ValueKind == JsonValueKind.Number && flag.TryGetInt32(out var number) && number != 0)
            {
                return true;
            }
        }

        return Text(format, "protocol")?.Contains("drm", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static Uri? PlayableAddress(JsonElement format)
    {
        var protocol = Text(format, "protocol");
        var extension = Text(format, "ext");
        var manifest = Text(format, "manifest_url");
        var direct = Text(format, "url");
        var hls = protocol?.Contains("m3u8", StringComparison.OrdinalIgnoreCase) == true
            || extension?.Equals("m3u8", StringComparison.OrdinalIgnoreCase) == true;
        var chosen = hls && !string.IsNullOrWhiteSpace(manifest) ? manifest : direct;
        if (string.IsNullOrWhiteSpace(chosen))
        {
            chosen = manifest ?? direct;
        }

        if (string.IsNullOrWhiteSpace(chosen) || !Uri.TryCreate(chosen, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme is "http" or "https" ? uri : null;
    }

    private static bool MentionsDrm(string message)
        => message.Contains("DRM", StringComparison.OrdinalIgnoreCase);

    private static string JsonBody(string output)
    {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        return start >= 0 && end > start ? output[start..(end + 1)] : output;
    }

    private static string PageSubtitlePath(string pageUrl, string language)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pageUrl + "\n" + language))).ToLowerInvariant();
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "subtitle-cache");
        return Path.Combine(folder, "page-" + hash + ".vtt");
    }

    private static string? StoreSubtitle(string folder, string cache)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var produced = Directory.EnumerateFiles(folder)
            .Where(path => path.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .Where(info => info.Length > 0)
            .OrderByDescending(info => info.Extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(info => info.Length)
            .FirstOrDefault();
        if (produced is null)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(cache);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(produced.FullName, cache, overwrite: true);
        return cache;
    }

    private static int FormatRank(string? protocol, string? extension, string? codec, bool hasAudio)
    {
        var streamed = protocol?.Contains("m3u8", StringComparison.OrdinalIgnoreCase) == true
            || protocol?.Contains("dash", StringComparison.OrdinalIgnoreCase) == false && protocol?.Contains("http", StringComparison.OrdinalIgnoreCase) != true;
        var https = protocol?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true && !streamed;
        var rank = https ? 100 : 0;
        if (extension?.Equals("mp4", StringComparison.OrdinalIgnoreCase) == true || extension?.Equals("m4a", StringComparison.OrdinalIgnoreCase) == true)
        {
            rank += 40;
        }

        if (codec?.StartsWith("avc1", StringComparison.OrdinalIgnoreCase) == true || codec?.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase) == true)
        {
            rank += 20;
        }

        if (hasAudio)
        {
            rank += 5;
        }

        return rank;
    }

    private static string ToolsDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "tools");

    private static async Task<string> RunAsync(string ytdlp, IReadOnlyList<string> arguments, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = ytdlp,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("The downloader could not be started.");
        }

        var output = new StringBuilderWriter();
        var error = new StringBuilderWriter();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            output.AppendLine(args.Data);
            var match = ProgressPattern.Match(args.Data);
            if (match.Success && double.TryParse(match.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                progress?.Report(percent);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                error.AppendLine(args.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await using var cancel = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });
        await process.WaitForExitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            var detail = error.Text.Trim();
            if (detail.Length == 0)
            {
                detail = output.Text.Trim();
            }

            if (detail.Length > 280)
            {
                detail = detail[^280..];
            }

            throw new InvalidOperationException(detail.Length == 0 ? "The download failed." : detail);
        }

        return output.Text;
    }

    private sealed class StringBuilderWriter
    {
        private readonly System.Text.StringBuilder _builder = new();

        public string Text => _builder.ToString();

        public void AppendLine(string line)
        {
            lock (_builder)
            {
                _builder.AppendLine(line);
            }
        }
    }
}
