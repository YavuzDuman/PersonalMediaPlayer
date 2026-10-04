using System.Globalization;
using System.Text.Json;
using PersonalMediaPlayer.App.Download;

namespace PersonalMediaPlayer.App.Playback;

internal readonly record struct StreamLanguagePreference(string? AudioLanguage, string? CaptionLanguage)
{
    internal static StreamLanguagePreference Default { get; } = new(null, null);
}

internal readonly record struct StreamLanguageOption(string Code, string Name);

internal static class StreamLanguageSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "stream-languages.json");

    private static readonly IReadOnlyList<StreamLanguageOption> Catalog = BuildCatalog();

    internal static string? StoreOverride { get; set; }

    internal static IReadOnlyList<StreamLanguageOption> Languages => Catalog;

    internal static StreamLanguagePreference Load()
    {
        try
        {
            var path = StoreOverride ?? DefaultFilePath;
            if (!File.Exists(path))
            {
                return StreamLanguagePreference.Default;
            }

            var stored = JsonSerializer.Deserialize<StoredPreference>(File.ReadAllText(path), JsonOptions);
            if (stored is null)
            {
                return StreamLanguagePreference.Default;
            }

            return new StreamLanguagePreference(KnownLanguage(stored.Audio), KnownLanguage(stored.Captions));
        }
        catch (IOException)
        {
            return StreamLanguagePreference.Default;
        }
        catch (JsonException)
        {
            return StreamLanguagePreference.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return StreamLanguagePreference.Default;
        }
    }

    internal static void Save(StreamLanguagePreference preference)
    {
        var stored = new StoredPreference
        {
            Audio = KnownLanguage(preference.AudioLanguage) ?? "original",
            Captions = KnownLanguage(preference.CaptionLanguage) ?? "off"
        };
        try
        {
            var path = StoreOverride ?? DefaultFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(stored, JsonOptions));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static int AudioIndex(string? language)
    {
        var known = KnownLanguage(language);
        if (known is null)
        {
            return 0;
        }

        for (var index = 0; index < Catalog.Count; index++)
        {
            if (Catalog[index].Code.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1;
            }
        }

        return 0;
    }

    internal static int CaptionIndex(string? language) => AudioIndex(language);

    internal static string? LanguageFromIndex(int index)
    {
        if (index <= 0 || index > Catalog.Count)
        {
            return null;
        }

        return Catalog[index - 1].Code;
    }

    internal static int ChooseCaption(IReadOnlyList<DownloadSubtitle> tracks, string? requestedLanguage, string? preferredLanguage)
    {
        if (!string.IsNullOrWhiteSpace(requestedLanguage))
        {
            return FindCaption(tracks, requestedLanguage);
        }

        if (string.IsNullOrWhiteSpace(preferredLanguage))
        {
            return -1;
        }

        return FindCaption(tracks, preferredLanguage);
    }

    internal static bool SameLanguage(string key, string language)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        if (key.Equals(language, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (key.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return language.StartsWith(key + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindCaption(IReadOnlyList<DownloadSubtitle> tracks, string language)
    {
        var grouped = -1;
        for (var index = 0; index < tracks.Count; index++)
        {
            var code = tracks[index].Language;
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            if (code.Equals(language, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }

            if (grouped < 0 && (SameLanguage(code, language) || SameLanguage(LanguageLabels.Group(code), language)))
            {
                grouped = index;
            }
        }

        return grouped;
    }

    internal static bool IsStoredChoice(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var trimmed = code.Trim();
        return trimmed.Equals("original", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("off", StringComparison.OrdinalIgnoreCase)
            || AudioIndex(trimmed) > 0;
    }

    private static string? KnownLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var trimmed = language.Trim();
        if (trimmed.Equals("original", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var option in Catalog)
        {
            if (option.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return option.Code;
            }
        }

        return null;
    }

    private static IReadOnlyList<StreamLanguageOption> BuildCatalog()
    {
        var list = new List<StreamLanguageOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (string.IsNullOrWhiteSpace(culture.Name)
                || culture.Name.StartsWith("qps", StringComparison.OrdinalIgnoreCase)
                || culture.EnglishName.Equals(culture.Name, StringComparison.OrdinalIgnoreCase)
                || culture.EnglishName.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = culture.Name.Split('-');
            if (parts[0].Length is < 2 or > 3 || parts.Length > 2)
            {
                continue;
            }

            if (parts.Length == 2 && parts[1].Length != 4)
            {
                continue;
            }

            if (!seen.Add(culture.Name))
            {
                continue;
            }

            list.Add(new StreamLanguageOption(culture.Name, culture.EnglishName));
        }

        list.Sort(static (left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            return byName != 0
                ? byName
                : string.Compare(left.Code, right.Code, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private sealed class StoredPreference
    {
        public string? Audio { get; set; }

        public string? Captions { get; set; }
    }
}
