using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("PersonalMediaPlayer.Tests")]

namespace PersonalMediaPlayer.App.Download;

public sealed record DownloadQuality(string Label, string Format, bool AudioOnly);

public sealed record DownloadListing(string Title, IReadOnlyList<DownloadQuality> Qualities);

internal static class YoutubeDownloader
{
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

    public static async Task<DownloadListing> ListAsync(string url, IProgress<string>? status, CancellationToken cancellationToken)
    {
        var ytdlp = await EnsureYtDlpAsync(status, cancellationToken);
        var runtime = await EnsureJsRuntimeAsync(status, cancellationToken);
        var json = await RunAsync(ytdlp, CommonArgs(runtime, "--dump-single-json", url), null, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var title = root.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Video";
        }

        return new DownloadListing(title, ReadQualities(root));
    }

    public static string CreateOutputPath(bool audioOnly)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PersonalMediaPlayer-download", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, audioOnly ? "video.m4a" : "video.mp4");
    }

    public static async Task<string> DownloadAsync(string url, DownloadQuality quality, string title, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
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

        arguments.Add(url);
        await RunAsync(ytdlp, arguments, progress, cancellationToken);
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
