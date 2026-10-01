using System.Globalization;
using System.Text;

namespace PersonalMediaPlayer.App.Playback;

internal static class LanguageLabels
{
    internal static string ForAudio(string key, string? rawName)
    {
        var name = EnglishFromCode(key);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Readable(rawName) ?? key;
        }

        if (IsOriginal(rawName, key))
        {
            return name + " · Original";
        }

        if (IsDubbed(rawName))
        {
            return name + " · Dubbed";
        }

        return name;
    }

    internal static string ForCaption(string code, string? givenName, bool automatic)
    {
        var bare = StripOrig(code.Trim());
        var name = LooksTranslated(bare, givenName)
            ? EnglishFromCode(Group(code, givenName))
            : Readable(givenName) ?? EnglishFromCode(code);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = string.IsNullOrWhiteSpace(code) ? "Subtitles" : code;
        }

        name = StripMarkers(name);
        if (automatic)
        {
            return name + " · Automatic";
        }

        if (IsDubbed(givenName))
        {
            return name + " · Dubbed";
        }

        if (IsOriginal(givenName, code))
        {
            return name + " · Original";
        }

        return name;
    }

    // Tracks that describe the same language collapse to one row. en and en-orig share a group.
    // YouTube appends the source language, including regions, as in bn-ar and bn-nl-NL.
    internal static string Group(string code, string? givenName = null)
    {
        var bare = StripOrig(code.Trim());
        return LooksTranslated(bare, givenName) && TryPeel(bare, out var target) ? target : bare;
    }

    private static string EnglishFromCode(string code)
    {
        var bare = StripOrig(code.Trim());
        if (bare.Length == 0)
        {
            return string.Empty;
        }

        if (TrySplitTranslation(bare, out var target, out var source))
        {
            var translated = EnglishFromCode(target);
            var from = EnglishFromCode(source);
            if (translated.Length > 0 && from.Length > 0)
            {
                return translated + " from " + from;
            }
        }

        if (TryCultureName(bare, out var name))
        {
            return name;
        }

        var dash = bare.IndexOf('-');
        if (dash > 0 && TryCultureName(bare[..dash], out name))
        {
            return name;
        }

        return bare;
    }

    private static bool LooksTranslated(string code, string? givenName)
    {
        if (!string.IsNullOrWhiteSpace(givenName) && givenName.Contains(" from ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !IsStandaloneCulture(code) && TryPeel(code, out _);
    }

    private static bool IsStandaloneCulture(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(Alias(code));
            if (culture.EnglishName.Equals(culture.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parts = code.Split('-');
            var tail = parts[^1];
            if (parts.Length >= 2
                && tail.All(character => char.IsLetter(character) && char.IsLower(character))
                && IsNeutralLanguage(Alias(tail)))
            {
                return false;
            }

            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool TryPeel(string code, out string target)
    {
        target = string.Empty;
        var parts = code.Split('-');
        for (var head = 1; head < parts.Length; head++)
        {
            var left = string.Join('-', parts[..head]);
            var right = string.Join('-', parts[head..]);
            if (IsKnownLanguage(left) && IsKnownLanguage(right))
            {
                target = left;
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownLanguage(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(Alias(code));
            return !culture.EnglishName.Equals(culture.Name, StringComparison.OrdinalIgnoreCase);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool TrySplitTranslation(string code, out string target, out string source)
    {
        target = string.Empty;
        source = string.Empty;
        var split = code.LastIndexOf('-');
        if (split <= 0 || split >= code.Length - 1)
        {
            return false;
        }

        source = code[(split + 1)..];
        target = code[..split];
        if (source.Length is < 2 or > 3 || source.Any(character => !char.IsLetter(character) || !char.IsLower(character)))
        {
            return false;
        }

        if (target.Length < 2 || !IsNeutralLanguage(Alias(source)))
        {
            return false;
        }

        return IsLanguageCode(target) || TryCultureName(target, out _);
    }

    private static bool TryCultureName(string code, out string name)
    {
        name = string.Empty;
        try
        {
            var culture = CultureInfo.GetCultureInfo(Alias(code));
            name = culture.EnglishName;
            if (string.IsNullOrWhiteSpace(name)
                || HasNonLatinLetter(name)
                || name.Equals(code, StringComparison.OrdinalIgnoreCase)
                || name.Equals(culture.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static string Alias(string code)
    {
        var parts = code.Split('-');
        if (parts.Length == 0)
        {
            return code;
        }

        parts[0] = parts[0].ToLowerInvariant() switch
        {
            "iw" => "he",
            "in" => "id",
            "ji" => "yi",
            _ => parts[0]
        };
        return string.Join('-', parts);
    }

    private static bool IsNeutralLanguage(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            return culture.IsNeutralCulture
                && !culture.EnglishName.Equals(culture.Name, StringComparison.OrdinalIgnoreCase);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static string? Readable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        if (trimmed.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Default", StringComparison.OrdinalIgnoreCase)
            || HasNonLatinLetter(trimmed))
        {
            return null;
        }

        var stripped = StripMarkers(trimmed);
        if (stripped.Length == 0 || IsLanguageCode(stripped) || HasNonLatinLetter(stripped))
        {
            return null;
        }

        return stripped;
    }

    private static string StripMarkers(string name)
    {
        var cleaned = name.Trim();
        string[] suffixes =
        [
            " (original)",
            " (auto-generated)",
            " (automatic)",
            " (dubbed)",
            " - original",
            " - dubbed",
            ", original",
            ", dubbed"
        ];
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in suffixes)
            {
                if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned[..^suffix.Length].Trim().TrimEnd('-', ',', ' ');
                    changed = true;
                }
            }
        }

        return cleaned.Length == 0 ? name.Trim() : cleaned;
    }

    private static string StripOrig(string code)
    {
        const string suffix = "-orig";
        return code.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && code.Length > suffix.Length
            ? code[..^suffix.Length]
            : code;
    }

    private static bool IsOriginal(string? rawName, string code)
    {
        if (code.EndsWith("-orig", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        var trimmed = rawName.Trim();
        return trimmed.Equals("Default", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("original", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDubbed(string? rawName)
        => !string.IsNullOrWhiteSpace(rawName) && rawName.Contains("dubbed", StringComparison.OrdinalIgnoreCase);

    private static bool IsLanguageCode(string text)
    {
        var parts = text.Split('-');
        if (parts.Length is < 1 or > 4 || parts[0].Length is < 2 or > 3 || !IsAsciiLetters(parts[0]))
        {
            return false;
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (parts[index].Length is < 2 or > 8 || !IsAsciiToken(parts[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetters(string text)
        => text.Length > 0 && text.All(char.IsAsciiLetter);

    private static bool IsAsciiToken(string text)
        => text.Length > 0 && text.All(character => char.IsAsciiLetter(character) || char.IsAsciiDigit(character));

    private static bool HasNonLatinLetter(string text)
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
