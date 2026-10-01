using System.Text.Json;

namespace PersonalMediaPlayer.App.Subtitles;

internal static class TurkishDictionary
{
    private static readonly Dictionary<string, string> Contractions = new(StringComparer.Ordinal)
    {
        ["can't"] = "yapamaz",
        ["cannot"] = "yapamaz",
        ["don't"] = "yapma",
        ["doesn't"] = "yapmaz",
        ["didn't"] = "yapmadı",
        ["isn't"] = "değil",
        ["aren't"] = "değiller",
        ["wasn't"] = "değildi",
        ["weren't"] = "değillerdi",
        ["won't"] = "yapmayacak",
        ["couldn't"] = "yapamazdı",
        ["shouldn't"] = "yapmamalı",
        ["wouldn't"] = "yapmazdı",
        ["haven't"] = "yapmadım",
        ["hasn't"] = "yapmadı",
        ["hadn't"] = "yapmamıştı",
        ["mustn't"] = "yapmamalı",
        ["needn't"] = "gerekmez",
        ["ain't"] = "değil",
        ["i'm"] = "ben",
        ["i've"] = "sahibim",
        ["i'll"] = "yapacağım",
        ["i'd"] = "yapardım",
        ["you're"] = "sen",
        ["you've"] = "sahipsin",
        ["you'll"] = "yapacaksın",
        ["you'd"] = "yapardın",
        ["we're"] = "biz",
        ["we've"] = "sahibiz",
        ["we'll"] = "yapacağız",
        ["we'd"] = "yapardık",
        ["they're"] = "onlar",
        ["they've"] = "sahipler",
        ["they'll"] = "yapacaklar",
        ["they'd"] = "yaparlardı",
        ["he's"] = "o",
        ["he'll"] = "yapacak",
        ["he'd"] = "yapardı",
        ["she's"] = "o",
        ["she'll"] = "yapacak",
        ["she'd"] = "yapardı",
        ["it's"] = "o",
        ["it'll"] = "o olacak",
        ["it'd"] = "o olurdu",
        ["that's"] = "o",
        ["that'll"] = "o olacak",
        ["what's"] = "ne",
        ["who's"] = "kim",
        ["there's"] = "var",
        ["here's"] = "işte",
        ["let's"] = "hadi"
    };

    private static Dictionary<string, string>? _words;
    private static Dictionary<string, string>? _phrases;

    public readonly record struct PhraseHit(int Start, int Length, string Turkish);

    public static string? Lookup(string word, IReadOnlyList<string> line, int index)
    {
        return FindPhrase(line, index)?.Turkish ?? Lookup(word);
    }

    public static PhraseHit? FindPhrase(IReadOnlyList<string> line, int index)
    {
        return MatchPhrase(line, index);
    }

    public static string? Lookup(string word)
    {
        var key = Normalize(word);
        if (key.Length == 0)
        {
            return null;
        }

        if (Contractions.TryGetValue(key, out var meaning))
        {
            return meaning;
        }

        if (key.EndsWith("'s", StringComparison.Ordinal) && key.Length > 2)
        {
            return Find(key[..^2]);
        }

        if (KeepsApostrophe(key))
        {
            return null;
        }

        return Find(key.Replace("'", string.Empty, StringComparison.Ordinal));
    }

    private static PhraseHit? MatchPhrase(IReadOnlyList<string> line, int index)
    {
        if (index < 0 || index >= line.Count)
        {
            return null;
        }

        var tokens = new string[line.Count];
        for (var i = 0; i < line.Count; i++)
        {
            tokens[i] = PhraseToken(line[i]);
        }

        if (tokens[index].Length == 0)
        {
            return null;
        }

        var phrases = Phrases();
        for (var length = Math.Min(4, line.Count); length >= 2; length--)
        {
            var startMin = Math.Max(0, index - length + 1);
            var startMax = Math.Min(index, line.Count - length);
            for (var start = startMin; start <= startMax; start++)
            {
                var blank = false;
                for (var i = start; i < start + length; i++)
                {
                    if (tokens[i].Length == 0)
                    {
                        blank = true;
                        break;
                    }
                }

                if (blank)
                {
                    continue;
                }

                var key = string.Join(' ', tokens[start..(start + length)]);
                if (phrases.TryGetValue(key, out var meaning))
                {
                    return new PhraseHit(start, length, meaning);
                }
            }
        }

        return null;
    }

    private static string PhraseToken(string word)
    {
        return Normalize(word).Replace("'", string.Empty, StringComparison.Ordinal);
    }

    private static string? Find(string key)
    {
        if (key.Length == 0)
        {
            return null;
        }

        var words = Words();
        if (words.TryGetValue(key, out var meaning))
        {
            return meaning;
        }

        foreach (var stem in Stems(key))
        {
            if (stem.Length < 3 || !words.TryGetValue(stem, out meaning))
            {
                continue;
            }

            return meaning;
        }

        return null;
    }

    private static bool KeepsApostrophe(string key)
    {
        return key.Contains("'t", StringComparison.Ordinal)
            || key.EndsWith("'ve", StringComparison.Ordinal)
            || key.EndsWith("'re", StringComparison.Ordinal)
            || key.EndsWith("'ll", StringComparison.Ordinal)
            || key.EndsWith("'d", StringComparison.Ordinal)
            || key.EndsWith("'m", StringComparison.Ordinal)
            || key.EndsWith("'s", StringComparison.Ordinal);
    }

    private static string Normalize(string word)
    {
        var trimmed = word.Trim().ToLowerInvariant()
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u02BC', '\'')
            .Replace('\uFF07', '\'');
        return trimmed.Trim('\'', '"', '.', ',', '!', '?', ';', ':', '(', ')', '[', ']', '…', '-', '–', '—');
    }

    private static IEnumerable<string> Stems(string key)
    {
        if (key.EndsWith("ies", StringComparison.Ordinal) && key.Length > 4)
        {
            yield return string.Concat(key.AsSpan(0, key.Length - 3), "y");
        }

        if (key.EndsWith("es", StringComparison.Ordinal) && key.Length > 3)
        {
            yield return key[..^2];
        }

        if (key.EndsWith('s') && key.Length > 3)
        {
            yield return key[..^1];
        }

        if (key.EndsWith("ing", StringComparison.Ordinal) && key.Length > 4)
        {
            var stem = key[..^3];
            yield return stem + "e";
            if (stem.Length > 2 && stem[^1] == stem[^2])
            {
                yield return stem[..^1];
            }

            yield return stem;
        }

        if (key.EndsWith("ied", StringComparison.Ordinal) && key.Length > 4)
        {
            yield return string.Concat(key.AsSpan(0, key.Length - 3), "y");
        }

        if (key.EndsWith("ed", StringComparison.Ordinal) && key.Length > 3)
        {
            var stem = key[..^2];
            yield return stem + "e";
            if (stem.Length > 2 && stem[^1] == stem[^2])
            {
                yield return stem[..^1];
            }

            yield return stem;
        }

        if (key.EndsWith("est", StringComparison.Ordinal) && key.Length > 4)
        {
            var stem = key[..^3];
            yield return stem + "e";
            if (stem.Length > 2 && stem[^1] == stem[^2])
            {
                yield return stem[..^1];
            }

            yield return stem;
        }

        if (key.EndsWith("er", StringComparison.Ordinal) && key.Length > 4)
        {
            var stem = key[..^2];
            yield return stem + "e";
            if (stem.Length > 2 && stem[^1] == stem[^2])
            {
                yield return stem[..^1];
            }

            yield return stem;
        }

        if (key.EndsWith("ily", StringComparison.Ordinal) && key.Length > 5)
        {
            yield return string.Concat(key.AsSpan(0, key.Length - 3), "y");
        }

        if (key.EndsWith("ally", StringComparison.Ordinal) && key.Length > 6)
        {
            yield return key[..^4];
        }

        if (key.EndsWith("ly", StringComparison.Ordinal) && key.Length > 4)
        {
            yield return key[..^2];
        }
    }

    private static Dictionary<string, string> Words()
    {
        if (_words is not null)
        {
            return _words;
        }

        _words = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "en-tr.json");
        if (!File.Exists(path))
        {
            return _words;
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        if (parsed is null)
        {
            return _words;
        }

        foreach (var pair in parsed)
        {
            if (pair.Key.Length == 0 || pair.Value.Length == 0)
            {
                continue;
            }

            _words[pair.Key.ToLowerInvariant()] = pair.Value;
        }

        return _words;
    }

    private static Dictionary<string, string> Phrases()
    {
        if (_phrases is not null)
        {
            return _phrases;
        }

        _phrases = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "en-tr-phrases.json");
        if (!File.Exists(path))
        {
            return _phrases;
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        if (parsed is null)
        {
            return _phrases;
        }

        foreach (var pair in parsed)
        {
            if (pair.Key.Length == 0 || pair.Value.Length == 0)
            {
                continue;
            }

            _phrases[pair.Key] = pair.Value;
        }

        return _phrases;
    }
}
