using System.Globalization;

namespace PersonalMediaPlayer.App.Editing;

internal static class AudioSilence
{
    internal const long MinimumMs = 400;
    internal const int OriginalVolume = 100;
    internal const int MinimumVolume = 0;
    internal const int MaximumVolume = 200;

    internal readonly record struct Span(long StartMs, long EndMs, int VolumePercent);

    internal static List<Span> Normalize(IReadOnlyList<Span> spans, long durationMs)
    {
        var duration = Math.Max(0, durationMs);
        var result = new List<Span>();
        foreach (var raw in spans)
        {
            var start = Math.Clamp(Math.Min(raw.StartMs, raw.EndMs), 0, duration);
            var end = Math.Clamp(Math.Max(raw.StartMs, raw.EndMs), 0, duration);
            if (end - start < MinimumMs)
            {
                continue;
            }

            var volume = Math.Clamp(raw.VolumePercent, MinimumVolume, MaximumVolume);
            result = Cut(result, start, end);
            if (volume == OriginalVolume)
            {
                continue;
            }

            result.Add(new Span(start, end, volume));
            result = Merge(result);
        }

        result.RemoveAll(span => span.EndMs - span.StartMs < MinimumMs);
        return result;
    }

    internal static int GainPercent(IReadOnlyList<Span> spans, long timeMs)
    {
        foreach (var span in spans)
        {
            if (timeMs >= span.StartMs && timeMs < span.EndMs)
            {
                return span.VolumePercent;
            }
        }

        return OriginalVolume;
    }

    // DirectSound turns the player volume into amplitude with a cube
    // (6000 * log10 below 100, and volume^3 above it). A cube root keeps
    // a 30% part at 30% of the rest, which is what the saved file does.
    // At 100% the listening level and the fade are unchanged.
    internal static int PlayerVolume(double listen, double fade, int gainPercent)
    {
        var part = Math.Clamp(gainPercent, MinimumVolume, MaximumVolume) / 100d;
        var level = Math.Max(0, listen) * Math.Clamp(fade, 0, 1);
        var curved = level * Math.Cbrt(part);
        return (int)Math.Round(Math.Clamp(curved, 0, 200));
    }

    internal static bool Contains(IReadOnlyList<Span> spans, long timeMs)
        => spans.Any(span => timeMs >= span.StartMs && timeMs < span.EndMs);

    internal static string? VolumeFilter(IReadOnlyList<Span> spans)
    {
        if (spans.Count == 0)
        {
            return null;
        }

        var culture = CultureInfo.InvariantCulture;
        var filters = new List<string>();
        foreach (var group in spans.GroupBy(span => span.VolumePercent).OrderBy(group => group.Key))
        {
            var windows = group.Select(span => string.Create(
                culture,
                $"between(t,{span.StartMs / 1000d:0.000},{span.EndMs / 1000d:0.000})"));
            var gain = (group.Key / 100d).ToString("0.###", culture);
            filters.Add($"volume={gain}:enable='{string.Join('+', windows)}'");
        }

        return string.Join(',', filters);
    }

    private static List<Span> Cut(List<Span> spans, long start, long end)
    {
        var next = new List<Span>();
        foreach (var span in spans)
        {
            if (span.EndMs <= start || span.StartMs >= end)
            {
                next.Add(span);
                continue;
            }

            if (span.StartMs < start)
            {
                next.Add(span with { EndMs = start });
            }

            if (span.EndMs > end)
            {
                next.Add(span with { StartMs = end });
            }
        }

        return next;
    }

    private static List<Span> Merge(List<Span> spans)
    {
        if (spans.Count == 0)
        {
            return spans;
        }

        spans.Sort((left, right) => left.StartMs.CompareTo(right.StartMs));
        var merged = new List<Span> { spans[0] };
        for (var index = 1; index < spans.Count; index++)
        {
            var span = spans[index];
            var previous = merged[^1];
            if (span.VolumePercent == previous.VolumePercent && span.StartMs <= previous.EndMs)
            {
                merged[^1] = previous with { EndMs = Math.Max(previous.EndMs, span.EndMs) };
                continue;
            }

            merged.Add(span);
        }

        return merged;
    }
}
