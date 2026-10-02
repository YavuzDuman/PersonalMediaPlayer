using System.Globalization;
using System.Net;
using System.Text;

namespace PersonalMediaPlayer.App.Playback;

internal sealed record HlsAudioOption(string Key, string Label);

internal sealed record HlsQualityOption(int Height, string Label);

internal sealed record HlsPrepared(string Body, HlsMaster Master, string AudioKey, int Height, string? PlaylistPath);

internal sealed class HlsMaster
{
    // LibVLC 3.0.21 opens an HLS master with five audio renditions in a group and rejects the master at six.
    internal const int MaxAudioRenditionsPerGroup = 5;

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly List<Rendition> _audios;
    private readonly List<Variant> _variants;
    private readonly List<string> _preamble;

    private HlsMaster(List<Rendition> audios, List<Variant> variants, List<string> preamble)
    {
        _audios = audios;
        _variants = variants;
        _preamble = preamble;
        Audios = BuildAudios(audios);
        Qualities = variants
            .Where(item => item.Height > 0)
            .Select(item => item.Height)
            .Distinct()
            .OrderByDescending(height => height)
            .Select(height => new HlsQualityOption(height, height.ToString(CultureInfo.InvariantCulture) + "p"))
            .ToList();
        MaxAudioRenditionsInAnyGroup = audios
            .GroupBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();
    }

    internal IReadOnlyList<HlsAudioOption> Audios { get; }

    internal IReadOnlyList<HlsQualityOption> Qualities { get; }

    internal int MaxAudioRenditionsInAnyGroup { get; }

    internal bool ExceedsAudioLimit => MaxAudioRenditionsInAnyGroup > MaxAudioRenditionsPerGroup;

    internal static bool TryParse(string? text, out HlsMaster? master)
    {
        master = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var source = text[0] == '\uFEFF' ? text[1..] : text;
        if (!source.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal))
        {
            return false;
        }

        var audios = new List<Rendition>();
        var variants = new List<Variant>();
        var preamble = new List<string>();
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#EXTM3U", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            {
                var attributes = ReadAttributes(line["#EXT-X-STREAM-INF:".Length..]);
                var uri = string.Empty;
                while (i + 1 < lines.Length)
                {
                    var next = lines[i + 1].Trim();
                    if (next.Length == 0)
                    {
                        i++;
                        continue;
                    }

                    if (next.StartsWith('#'))
                    {
                        break;
                    }

                    uri = next;
                    i++;
                    break;
                }

                if (uri.Length > 0)
                {
                    variants.Add(new Variant(ReadHeight(Attribute(attributes, "RESOLUTION")), attributes, Unquote(uri)));
                }

                continue;
            }

            if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal))
            {
                var attributes = ReadAttributes(line["#EXT-X-MEDIA:".Length..]);
                if (!string.Equals(Attribute(attributes, "TYPE"), "AUDIO", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var uri = Attribute(attributes, "URI");
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                var language = Attribute(attributes, "LANGUAGE");
                var name = Attribute(attributes, "NAME");
                var key = !string.IsNullOrWhiteSpace(language) ? language.Trim() : name.Trim();
                if (key.Length == 0)
                {
                    key = "Audio";
                }

                var label = !string.IsNullOrWhiteSpace(name) ? name.Trim() : key;
                var group = Attribute(attributes, "GROUP-ID");
                if (group.Length == 0)
                {
                    group = "audio";
                }

                var isDefault = string.Equals(Attribute(attributes, "DEFAULT"), "YES", StringComparison.OrdinalIgnoreCase);
                var isOriginal = name.Contains("original", StringComparison.OrdinalIgnoreCase);
                audios.Add(new Rendition(group, key, label, isDefault, isOriginal, attributes));
                continue;
            }

            if (line.StartsWith("#EXT-X-VERSION:", StringComparison.Ordinal)
                || line.StartsWith("#EXT-X-INDEPENDENT-SEGMENTS", StringComparison.Ordinal))
            {
                preamble.Add(line);
            }
        }

        if (variants.Count == 0)
        {
            return false;
        }

        master = new HlsMaster(audios, variants, preamble);
        return true;
    }

    internal static async Task<HlsPrepared?> TryPrepareAsync(
        Uri playlistUrl,
        string? body,
        string? audioKey,
        int height,
        Uri? referrer,
        string? userAgent,
        CancellationToken cancellationToken,
        bool matchLanguagePreference = false,
        string? preferredLanguage = null)
    {
        if (string.IsNullOrEmpty(body))
        {
            body = await DownloadAsync(playlistUrl, referrer, userAgent, cancellationToken);
        }

        if (string.IsNullOrEmpty(body) || !TryParse(body, out var master) || master is null)
        {
            return null;
        }

        var selectedAudio = matchLanguagePreference
            ? master.ChooseAudio(audioKey, preferredLanguage)
            : master.SelectAudio(audioKey);
        var selectedHeight = master.ResolveHeight(height);
        var rewrite = master.ExceedsAudioLimit || master.Audios.Count > 1 || master.Qualities.Count > 1 || selectedHeight > 0;
        string? path = null;
        if (rewrite)
        {
            path = WritePlaylist(master.Rewrite(selectedAudio, selectedHeight, playlistUrl));
        }

        return new HlsPrepared(body, master, selectedAudio, selectedHeight, path);
    }

    internal string SelectAudio(string? requested)
    {
        if (FindKey(requested) is string key)
        {
            return key;
        }

        return Audios.Count == 0 ? string.Empty : Audios[0].Key;
    }

    internal string ChooseAudio(string? requestedKey, string? preferredLanguage)
    {
        if (FindKey(requestedKey) is string requested)
        {
            return requested;
        }

        if (string.IsNullOrWhiteSpace(requestedKey) && FindLanguage(preferredLanguage) is string preferred)
        {
            return preferred;
        }

        return OriginalKey();
    }

    private string? FindKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        foreach (var option in Audios)
        {
            if (option.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return option.Key;
            }
        }

        return null;
    }

    private string? FindLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        string? prefix = null;
        foreach (var option in Audios)
        {
            if (option.Key.Equals(language, StringComparison.OrdinalIgnoreCase))
            {
                return option.Key;
            }

            if (prefix is null && StreamLanguageSettings.SameLanguage(option.Key, language))
            {
                prefix = option.Key;
            }
        }

        return prefix;
    }

    // A track named original wins over the rendition marked DEFAULT.
    private string OriginalKey()
    {
        foreach (var rendition in _audios)
        {
            if (rendition.IsOriginal && FindKey(rendition.Key) is string key)
            {
                return key;
            }
        }

        return Audios.Count == 0 ? string.Empty : Audios[0].Key;
    }

    internal int ResolveHeight(int wanted)
    {
        if (wanted <= 0 || Qualities.Count == 0)
        {
            return 0;
        }

        foreach (var option in Qualities)
        {
            if (option.Height == wanted)
            {
                return wanted;
            }
        }

        var closest = Qualities[0].Height;
        var distance = Math.Abs(closest - wanted);
        foreach (var option in Qualities)
        {
            var gap = Math.Abs(option.Height - wanted);
            if (gap < distance)
            {
                closest = option.Height;
                distance = gap;
            }
        }

        return closest;
    }

    internal string Rewrite(string? audioKey, int height, Uri playlistUrl)
    {
        var selectedAudio = SelectAudio(audioKey);
        var selectedHeight = ResolveHeight(height);
        var kept = KeepAudio(selectedAudio);
        var keptGroups = new HashSet<string>(kept.Select(item => item.Group), StringComparer.OrdinalIgnoreCase);
        var fallbackGroup = kept.Count > 0 ? kept[0].Group : null;
        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");
        foreach (var line in _preamble)
        {
            builder.AppendLine(line);
        }

        var defaulted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rendition in kept)
        {
            var attributes = rendition.Attributes.ToList();
            var first = defaulted.Add(rendition.Group);
            SetAttribute(attributes, "DEFAULT", first ? "YES" : "NO");
            SetAttribute(attributes, "AUTOSELECT", first ? "YES" : "NO");
            var uri = Attribute(attributes, "URI");
            if (uri.Length > 0)
            {
                SetAttribute(attributes, "URI", Absolute(uri, playlistUrl), quoted: true);
            }

            builder.Append("#EXT-X-MEDIA:");
            builder.AppendLine(FormatAttributes(attributes));
        }

        foreach (var variant in KeepVariants(selectedHeight))
        {
            var attributes = variant.Attributes
                .Where(item => !item.Key.Equals("SUBTITLES", StringComparison.OrdinalIgnoreCase)
                    && !item.Key.Equals("CLOSED-CAPTIONS", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var audioGroup = Attribute(attributes, "AUDIO");
            if (audioGroup.Length > 0 && !keptGroups.Contains(audioGroup) && fallbackGroup is not null)
            {
                SetAttribute(attributes, "AUDIO", fallbackGroup, quoted: true);
            }

            builder.Append("#EXT-X-STREAM-INF:");
            builder.AppendLine(FormatAttributes(attributes));
            builder.AppendLine(Absolute(variant.Uri, playlistUrl));
        }

        return builder.ToString();
    }

    internal static async Task<string?> DownloadAsync(Uri playlistUrl, Uri? referrer, string? userAgent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, playlistUrl);
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        if (referrer is not null)
        {
            request.Headers.Referrer = referrer;
        }

        request.Headers.Accept.ParseAdd("*/*");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var buffer = new char[4096];
        var builder = new StringBuilder();
        while (builder.Length < 2_000_000)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        var text = builder.ToString();
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        return text.Contains("#EXTM3U", StringComparison.Ordinal) ? text : null;
    }

    private List<Rendition> KeepAudio(string audioKey)
    {
        var matches = _audios
            .Where(item => audioKey.Length == 0 || item.Key.Equals(audioKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            matches = _audios.ToList();
        }

        var kept = new List<Rendition>();
        foreach (var group in matches.GroupBy(item => item.Group, StringComparer.OrdinalIgnoreCase))
        {
            kept.AddRange(group
                .OrderByDescending(item => item.IsDefault)
                .ThenByDescending(item => item.IsOriginal)
                .Take(MaxAudioRenditionsPerGroup));
        }

        return kept;
    }

    private List<Variant> KeepVariants(int height)
    {
        var matches = _variants.Where(item => height <= 0 || item.Height == height).ToList();
        if (height <= 0)
        {
            return matches;
        }

        var avc = matches.Where(item => Attribute(item.Attributes, "CODECS").Contains("avc1", StringComparison.OrdinalIgnoreCase)
            || Attribute(item.Attributes, "CODECS").Contains("h264", StringComparison.OrdinalIgnoreCase)).ToList();
        return avc.Count > 0 ? avc : matches;
    }

    private static List<HlsAudioOption> BuildAudios(List<Rendition> audios)
    {
        var preferred = audios.FirstOrDefault(item => item.IsDefault)?.Key
            ?? audios.FirstOrDefault(item => item.IsOriginal)?.Key
            ?? audios.FirstOrDefault()?.Key;
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(preferred) && seen.Add(preferred))
        {
            order.Add(preferred);
        }

        foreach (var rendition in audios)
        {
            if (seen.Add(rendition.Key))
            {
                order.Add(rendition.Key);
            }
        }

        var options = new List<HlsAudioOption>();
        foreach (var key in order)
        {
            var rendition = audios.First(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            var label = LanguageLabels.ForAudio(rendition.Key, rendition.Label);
            if (options.Any(item => item.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
                && !label.Contains(rendition.Key, StringComparison.OrdinalIgnoreCase))
            {
                label = label + " (" + rendition.Key + ")";
            }

            options.Add(new HlsAudioOption(rendition.Key, label));
        }

        return options;
    }

    private static string WritePlaylist(string body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PersonalMediaPlayer-hls");
        Directory.CreateDirectory(folder);
        Sweep(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".m3u8");
        File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static void Sweep(string folder)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-15);
            foreach (var file in Directory.EnumerateFiles(folder, "*.m3u8"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static List<(string Key, string Value, bool Quoted)> ReadAttributes(string text)
    {
        var attributes = new List<(string Key, string Value, bool Quoted)>();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && (text[index] == ',' || char.IsWhiteSpace(text[index])))
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var start = index;
            while (index < text.Length && text[index] != '=' && text[index] != ',')
            {
                index++;
            }

            if (index >= text.Length || text[index] != '=')
            {
                break;
            }

            var key = text[start..index].Trim();
            index++;
            string value;
            var quoted = false;
            if (index < text.Length && text[index] == '"')
            {
                quoted = true;
                index++;
                var builder = new StringBuilder();
                while (index < text.Length)
                {
                    if (text[index] == '\\' && index + 1 < text.Length)
                    {
                        builder.Append(text[index + 1]);
                        index += 2;
                        continue;
                    }

                    if (text[index] == '"')
                    {
                        index++;
                        break;
                    }

                    builder.Append(text[index]);
                    index++;
                }

                value = builder.ToString();
            }
            else
            {
                var valueStart = index;
                while (index < text.Length && text[index] != ',')
                {
                    index++;
                }

                value = text[valueStart..index].Trim();
            }

            if (key.Length > 0)
            {
                attributes.Add((key, value, quoted));
            }
        }

        return attributes;
    }

    private static string Attribute(IReadOnlyList<(string Key, string Value, bool Quoted)> attributes, string key)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return attribute.Value;
            }
        }

        return string.Empty;
    }

    private static void SetAttribute(List<(string Key, string Value, bool Quoted)> attributes, string key, string value, bool? quoted = null)
    {
        for (var i = 0; i < attributes.Count; i++)
        {
            if (attributes[i].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                attributes[i] = (attributes[i].Key, value, quoted ?? attributes[i].Quoted);
                return;
            }
        }

        attributes.Add((key, value, quoted ?? NeedsQuotes(value)));
    }

    private static string FormatAttributes(IReadOnlyList<(string Key, string Value, bool Quoted)> attributes)
    {
        var parts = new string[attributes.Count];
        for (var i = 0; i < attributes.Count; i++)
        {
            var quote = attributes[i].Quoted || NeedsQuotes(attributes[i].Value);
            parts[i] = attributes[i].Key + "=" + (quote ? Quote(attributes[i].Value) : attributes[i].Value);
        }

        return string.Join(",", parts);
    }

    private static bool NeedsQuotes(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_')
            {
                return true;
            }
        }

        return false;
    }

    private static string Quote(string value)
        => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' ? trimmed[1..^1] : trimmed;
    }

    private static int ReadHeight(string resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            return 0;
        }

        var split = resolution.LastIndexOf('x');
        if (split < 0)
        {
            split = resolution.LastIndexOf('X');
        }

        if (split < 0 || split == resolution.Length - 1)
        {
            return 0;
        }

        return int.TryParse(resolution[(split + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            ? height
            : 0;
    }

    private static string Absolute(string uri, Uri playlistUrl)
    {
        var trimmed = uri.Trim().Trim('"');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            return trimmed;
        }

        return Uri.TryCreate(playlistUrl, trimmed, out var combined) ? combined.AbsoluteUri : trimmed;
    }

    private sealed record Rendition(
        string Group,
        string Key,
        string Label,
        bool IsDefault,
        bool IsOriginal,
        List<(string Key, string Value, bool Quoted)> Attributes);

    private sealed record Variant(int Height, List<(string Key, string Value, bool Quoted)> Attributes, string Uri);
}
