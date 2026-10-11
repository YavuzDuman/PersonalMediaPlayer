namespace PersonalMediaPlayer.App.Subtitles;

// A practice card for a saved word. The sentence is shown with the word covered.
// Nothing is written back to the saved-word list.
internal static class WordReview
{
    internal const string Blank = "_____";

    internal const string LinePrompt = "What was this line?";

    internal static string Prompt(string? sentence, string? word)
    {
        var line = sentence?.Trim() ?? string.Empty;
        var term = word?.Trim() ?? string.Empty;
        if (line.Length == 0 || term.Length == 0 || string.Equals(line, term, StringComparison.OrdinalIgnoreCase))
        {
            return LinePrompt;
        }

        return Hide(line, term) ?? line;
    }

    internal static string? Hide(string line, string term)
    {
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        return ReplaceTokens(line, term.Trim(), contained: false)
            ?? ReplaceTokens(line, term.Trim(), contained: true)
            ?? ReplaceText(line, term.Trim());
    }

    internal static List<SavedWord> Shuffle(IReadOnlyList<SavedWord> words, Random random)
    {
        var copy = words.ToList();
        for (var i = copy.Count - 1; i > 0; i--)
        {
            var swap = random.Next(i + 1);
            (copy[i], copy[swap]) = (copy[swap], copy[i]);
        }

        return copy;
    }

    private static string? ReplaceTokens(string line, string term, bool contained)
    {
        var builder = new System.Text.StringBuilder(line.Length);
        var changed = false;
        var index = 0;
        while (index < line.Length)
        {
            if (!char.IsLetter(line[index]))
            {
                builder.Append(line[index]);
                index++;
                continue;
            }

            var start = index;
            while (index < line.Length && char.IsLetter(line[index]))
            {
                index++;
            }

            var token = line.AsSpan(start, index - start);
            var hide = contained
                ? token.Contains(term, StringComparison.OrdinalIgnoreCase)
                : token.Equals(term, StringComparison.OrdinalIgnoreCase);
            if (hide)
            {
                builder.Append(Blank);
                changed = true;
            }
            else
            {
                builder.Append(token);
            }
        }

        return changed ? builder.ToString() : null;
    }

    private static string? ReplaceText(string line, string term)
    {
        var builder = new System.Text.StringBuilder(line.Length);
        var changed = false;
        var index = 0;
        while (index < line.Length)
        {
            var found = line.IndexOf(term, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                builder.Append(line, index, line.Length - index);
                break;
            }

            builder.Append(line, index, found - index);
            builder.Append(Blank);
            changed = true;
            index = found + term.Length;
        }

        return changed ? builder.ToString() : null;
    }
}
