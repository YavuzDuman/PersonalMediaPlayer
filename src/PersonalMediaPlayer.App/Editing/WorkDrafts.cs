using System.Globalization;
using System.Text.Json;

namespace PersonalMediaPlayer.App.Editing;

// Edit and merge drafts. The media files stay where they are.
// The file stays out of backups.
internal static class WorkDrafts
{
    internal const string FileName = "work-drafts.json";
    internal const int MaxClips = 24;

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        FileName);

    private static string? _storeOverride;

    internal static string? StoreOverride
    {
        get => _storeOverride;
        set => _storeOverride = value;
    }

    internal static IReadOnlyList<EditDraft> Edits()
        => Read().Edits
            .OrderByDescending(draft => Stamp(draft.SavedUtc))
            .ToArray();

    internal static IReadOnlyList<MergeDraft> Merges()
        => Read().Merges
            .OrderByDescending(draft => Stamp(draft.SavedUtc))
            .ToArray();

    internal static EditDraft? Edit(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Edits().FirstOrDefault(draft => draft.Id == id);

    internal static MergeDraft? Merge(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Merges().FirstOrDefault(draft => draft.Id == id);

    internal static EditDraft? SaveEdit(EditDraft draft)
    {
        var clean = NormalizeEdit(draft);
        if (clean is null)
        {
            return null;
        }

        var file = Read();
        file.Edits.RemoveAll(item => item.Id == clean.Id);
        file.Edits.Insert(0, clean);
        return Write(file) ? clean : null;
    }

    internal static MergeDraft? SaveMerge(MergeDraft draft)
    {
        var clean = NormalizeMerge(draft);
        if (clean is null)
        {
            return null;
        }

        var file = Read();
        file.Merges.RemoveAll(item => item.Id == clean.Id);
        file.Merges.Insert(0, clean);
        return Write(file) ? clean : null;
    }

    internal static EditDraft? PointEdit(string? id, string path)
    {
        var draft = Edit(id);
        if (draft is null || !TryFullPath(path, out var full))
        {
            return null;
        }

        draft.VideoPath = full;
        draft.VideoName = Path.GetFileName(full);
        return SaveEdit(draft);
    }

    internal static MergeDraft? PointMerge(string? id, int index, string path)
    {
        var draft = Merge(id);
        if (draft is null || index < 0 || index >= draft.Clips.Count || !TryFullPath(path, out var full))
        {
            return null;
        }

        var clip = draft.Clips[index];
        clip.Path = full;
        clip.Name = Path.GetFileName(full);
        return SaveMerge(draft);
    }

    internal static void RemoveEdit(string? id) => Remove(id, edits: true);

    internal static void RemoveMerge(string? id) => Remove(id, edits: false);

    internal static string When(string? savedUtc)
    {
        if (!DateTimeOffset.TryParse(savedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var saved))
        {
            return "Saved";
        }

        return "Saved " + saved.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    // The saved draft is the baseline. A later speed, trim, or clip change differs.
    // A screen that was never drafted uses differsFromBlank, which is the editor's own edit check.
    internal static bool EditDiffersFromSaved(EditDraft? saved, EditDraft current, bool differsFromBlank)
        => saved is null ? differsFromBlank : !SameEditWork(saved, current);

    internal static bool SameEditWork(EditDraft? left, EditDraft? right)
        => left is not null && right is not null && EditKey(left) == EditKey(right);

    // An empty list has nothing to lose. A list with no saved draft is unsaved work.
    internal static bool MergeDiffersFromSaved(MergeDraft? saved, MergeDraft? current)
    {
        if (current?.Clips is null || current.Clips.Count == 0)
        {
            return false;
        }

        return saved is null || !SameMergeWork(saved, current);
    }

    internal static bool SameMergeWork(MergeDraft? left, MergeDraft? right)
        => left is not null && right is not null && MergeKey(left) == MergeKey(right);

    // Save as new from a merge clip belongs in that slot only while the editor is still on that clip.
    internal static bool SameVideo(string? left, string? right)
    {
        if (!TryFullPath(left, out var first) || !TryFullPath(right, out var second))
        {
            return false;
        }

        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }

    internal static EditDraft CopyEdit(EditDraft draft)
    {
        return new EditDraft
        {
            Id = draft.Id,
            Name = draft.Name,
            SavedUtc = draft.SavedUtc,
            VideoPath = draft.VideoPath,
            VideoName = draft.VideoName,
            Speed = draft.Speed,
            Volume = draft.Volume,
            TrimStartMs = draft.TrimStartMs,
            TrimEndMs = draft.TrimEndMs,
            Cuts = (draft.Cuts ?? []).Where(mark => mark is not null).Select(mark => new TimeMark { StartMs = mark.StartMs, EndMs = mark.EndMs }).ToList(),
            Parts = (draft.Parts ?? []).Where(mark => mark is not null).Select(mark => new LevelMark { StartMs = mark.StartMs, EndMs = mark.EndMs, VolumePercent = mark.VolumePercent }).ToList(),
            MarkStartMs = draft.MarkStartMs,
            MarkEndMs = draft.MarkEndMs,
            CropLeft = draft.CropLeft,
            CropTop = draft.CropTop,
            CropRight = draft.CropRight,
            CropBottom = draft.CropBottom,
            CropPreview = draft.CropPreview,
            QuarterTurns = draft.QuarterTurns,
            FlipHorizontal = draft.FlipHorizontal,
            FlipVertical = draft.FlipVertical,
            VideoFadeIn = draft.VideoFadeIn,
            VideoFadeInSeconds = draft.VideoFadeInSeconds,
            VideoFadeOut = draft.VideoFadeOut,
            VideoFadeOutSeconds = draft.VideoFadeOutSeconds,
            AudioFadeIn = draft.AudioFadeIn,
            AudioFadeInSeconds = draft.AudioFadeInSeconds,
            AudioFadeOut = draft.AudioFadeOut,
            AudioFadeOutSeconds = draft.AudioFadeOutSeconds
        };
    }

    internal static MergeDraft CopyMerge(MergeDraft draft)
    {
        return new MergeDraft
        {
            Id = draft.Id,
            Name = draft.Name,
            SavedUtc = draft.SavedUtc,
            Clips = (draft.Clips ?? []).Where(clip => clip is not null).Select(CopyClip).ToList()
        };
    }

    private static MergeClipDraft CopyClip(MergeClipDraft clip)
    {
        return new MergeClipDraft
        {
            Path = clip.Path,
            Name = clip.Name,
            DurationSeconds = clip.DurationSeconds,
            StartSeconds = clip.StartSeconds,
            EndSeconds = clip.EndSeconds,
            HasAudio = clip.HasAudio,
            Width = clip.Width,
            Height = clip.Height,
            VideoFadeIn = clip.VideoFadeIn,
            VideoFadeInSeconds = clip.VideoFadeInSeconds,
            VideoFadeOut = clip.VideoFadeOut,
            VideoFadeOutSeconds = clip.VideoFadeOutSeconds,
            AudioFadeIn = clip.AudioFadeIn,
            AudioFadeInSeconds = clip.AudioFadeInSeconds,
            AudioFadeOut = clip.AudioFadeOut,
            AudioFadeOutSeconds = clip.AudioFadeOutSeconds
        };
    }

    private static string EditKey(EditDraft draft)
    {
        return string.Join(
            '\n',
            PathKey(draft.VideoPath),
            Thousandths(draft.Speed).ToString(CultureInfo.InvariantCulture),
            Thousandths(draft.Volume).ToString(CultureInfo.InvariantCulture),
            draft.TrimStartMs.ToString(CultureInfo.InvariantCulture),
            draft.TrimEndMs.ToString(CultureInfo.InvariantCulture),
            MarksKey(draft.Cuts),
            PartsKey(draft.Parts),
            draft.MarkStartMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            draft.MarkEndMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Thousandths(draft.CropLeft).ToString(CultureInfo.InvariantCulture),
            Thousandths(draft.CropTop).ToString(CultureInfo.InvariantCulture),
            Thousandths(draft.CropRight).ToString(CultureInfo.InvariantCulture),
            Thousandths(draft.CropBottom).ToString(CultureInfo.InvariantCulture),
            draft.CropPreview ? "1" : "0",
            draft.QuarterTurns.ToString(CultureInfo.InvariantCulture),
            draft.FlipHorizontal ? "1" : "0",
            draft.FlipVertical ? "1" : "0",
            draft.VideoFadeIn ? "1" : "0",
            Thousandths(draft.VideoFadeInSeconds).ToString(CultureInfo.InvariantCulture),
            draft.VideoFadeOut ? "1" : "0",
            Thousandths(draft.VideoFadeOutSeconds).ToString(CultureInfo.InvariantCulture),
            draft.AudioFadeIn ? "1" : "0",
            Thousandths(draft.AudioFadeInSeconds).ToString(CultureInfo.InvariantCulture),
            draft.AudioFadeOut ? "1" : "0",
            Thousandths(draft.AudioFadeOutSeconds).ToString(CultureInfo.InvariantCulture));
    }

    private static string MergeKey(MergeDraft draft)
    {
        var clips = draft.Clips ?? [];
        return string.Join('\n', clips.Where(clip => clip is not null).Select(ClipKey));
    }

    private static string ClipKey(MergeClipDraft clip)
    {
        return string.Join(
            '|',
            PathKey(clip.Path),
            Thousandths(clip.DurationSeconds).ToString(CultureInfo.InvariantCulture),
            Thousandths(clip.StartSeconds).ToString(CultureInfo.InvariantCulture),
            Thousandths(clip.EndSeconds).ToString(CultureInfo.InvariantCulture),
            clip.HasAudio ? "1" : "0",
            clip.Width.ToString(CultureInfo.InvariantCulture),
            clip.Height.ToString(CultureInfo.InvariantCulture),
            clip.VideoFadeIn ? "1" : "0",
            Thousandths(clip.VideoFadeInSeconds).ToString(CultureInfo.InvariantCulture),
            clip.VideoFadeOut ? "1" : "0",
            Thousandths(clip.VideoFadeOutSeconds).ToString(CultureInfo.InvariantCulture),
            clip.AudioFadeIn ? "1" : "0",
            Thousandths(clip.AudioFadeInSeconds).ToString(CultureInfo.InvariantCulture),
            clip.AudioFadeOut ? "1" : "0",
            Thousandths(clip.AudioFadeOutSeconds).ToString(CultureInfo.InvariantCulture));
    }

    private static string MarksKey(IEnumerable<TimeMark>? marks)
    {
        if (marks is null)
        {
            return string.Empty;
        }

        return string.Join(';', marks.Where(mark => mark is not null).Select(mark =>
            mark.StartMs.ToString(CultureInfo.InvariantCulture) + "-" + mark.EndMs.ToString(CultureInfo.InvariantCulture)));
    }

    private static string PartsKey(IEnumerable<LevelMark>? marks)
    {
        if (marks is null)
        {
            return string.Empty;
        }

        return string.Join(';', marks.Where(mark => mark is not null).Select(mark =>
            mark.StartMs.ToString(CultureInfo.InvariantCulture) + "-" + mark.EndMs.ToString(CultureInfo.InvariantCulture) + "@" + mark.VolumePercent.ToString(CultureInfo.InvariantCulture)));
    }

    private static string PathKey(string? path)
    {
        if (TryFullPath(path, out var full))
        {
            return full.ToUpperInvariant();
        }

        return (path ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static long Thousandths(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return (long)Math.Round(value * 1000d, MidpointRounding.AwayFromZero);
    }

    private static void Remove(string? id, bool edits)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var file = Read();
        var removed = edits ? file.Edits.RemoveAll(item => item.Id == id) : file.Merges.RemoveAll(item => item.Id == id);
        if (removed > 0)
        {
            Write(file);
        }
    }

    private static EditDraft? NormalizeEdit(EditDraft draft)
    {
        if (!TryFullPath(draft.VideoPath, out var path))
        {
            return null;
        }

        draft.VideoPath = path;
        draft.VideoName = string.IsNullOrWhiteSpace(draft.VideoName) ? Path.GetFileName(path) : draft.VideoName.Trim();
        draft.Name = CleanName(draft.Name, Path.GetFileNameWithoutExtension(draft.VideoName));
        draft.Id = CleanId(draft.Id);
        draft.SavedUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        draft.Speed = Finite(draft.Speed, 1, 0.25, 4);
        draft.Volume = Finite(draft.Volume, 100, 0, 100);
        draft.TrimStartMs = Math.Max(0, draft.TrimStartMs);
        draft.TrimEndMs = Math.Max(0, draft.TrimEndMs);
        draft.Cuts = CleanMarks(draft.Cuts);
        draft.Parts = CleanParts(draft.Parts);
        draft.CropLeft = Fraction(draft.CropLeft);
        draft.CropTop = Fraction(draft.CropTop);
        draft.CropRight = Fraction(draft.CropRight);
        draft.CropBottom = Fraction(draft.CropBottom);
        if (draft.CropRight <= draft.CropLeft || draft.CropBottom <= draft.CropTop)
        {
            draft.CropLeft = 0;
            draft.CropTop = 0;
            draft.CropRight = 1;
            draft.CropBottom = 1;
        }

        draft.QuarterTurns = ((draft.QuarterTurns % 4) + 4) % 4;
        draft.VideoFadeInSeconds = Finite(draft.VideoFadeInSeconds, 1, 0.1, 30);
        draft.VideoFadeOutSeconds = Finite(draft.VideoFadeOutSeconds, 1, 0.1, 30);
        draft.AudioFadeInSeconds = Finite(draft.AudioFadeInSeconds, 1, 0.1, 30);
        draft.AudioFadeOutSeconds = Finite(draft.AudioFadeOutSeconds, 1, 0.1, 30);
        return draft;
    }

    private static MergeDraft? NormalizeMerge(MergeDraft draft)
    {
        draft.Clips ??= [];
        var clips = new List<MergeClipDraft>();
        foreach (var clip in draft.Clips)
        {
            if (clip is null || !TryFullPath(clip.Path, out var path))
            {
                continue;
            }

            clip.Path = path;
            clip.Name = string.IsNullOrWhiteSpace(clip.Name) ? Path.GetFileName(path) : clip.Name.Trim();
            clip.DurationSeconds = Math.Max(0.1, Finite(clip.DurationSeconds, 0.1, 0.1, 604_800));
            var start = Math.Clamp(Math.Min(clip.StartSeconds, clip.EndSeconds), 0, clip.DurationSeconds);
            var end = Math.Clamp(Math.Max(clip.StartSeconds, clip.EndSeconds), 0, clip.DurationSeconds);
            if (end - start < 0.1)
            {
                end = Math.Min(clip.DurationSeconds, start + 0.1);
                if (end - start < 0.1)
                {
                    start = Math.Max(0, end - 0.1);
                }
            }

            clip.StartSeconds = start;
            clip.EndSeconds = end;
            clip.Width = Math.Clamp(clip.Width, 0, 7680);
            clip.Height = Math.Clamp(clip.Height, 0, 4320);
            clip.VideoFadeInSeconds = Finite(clip.VideoFadeInSeconds, 1, 0.1, 10);
            clip.VideoFadeOutSeconds = Finite(clip.VideoFadeOutSeconds, 1, 0.1, 10);
            clip.AudioFadeInSeconds = Finite(clip.AudioFadeInSeconds, 1, 0.1, 10);
            clip.AudioFadeOutSeconds = Finite(clip.AudioFadeOutSeconds, 1, 0.1, 10);
            clips.Add(clip);
            if (clips.Count == MaxClips)
            {
                break;
            }
        }

        if (clips.Count == 0)
        {
            return null;
        }

        draft.Clips = clips;
        draft.Name = CleanName(draft.Name, clips.Count == 1 ? Path.GetFileNameWithoutExtension(clips[0].Name) : clips.Count + " videos");
        draft.Id = CleanId(draft.Id);
        draft.SavedUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        return draft;
    }

    private static List<TimeMark> CleanMarks(List<TimeMark>? marks)
    {
        var clean = new List<TimeMark>();
        foreach (var mark in marks ?? [])
        {
            if (mark is null)
            {
                continue;
            }

            var start = Math.Max(0, Math.Min(mark.StartMs, mark.EndMs));
            var end = Math.Max(0, Math.Max(mark.StartMs, mark.EndMs));
            if (end - start < 400)
            {
                continue;
            }

            clean.Add(new TimeMark { StartMs = start, EndMs = end });
            if (clean.Count == 400)
            {
                break;
            }
        }

        return clean;
    }

    private static List<LevelMark> CleanParts(List<LevelMark>? marks)
    {
        var clean = new List<LevelMark>();
        foreach (var mark in marks ?? [])
        {
            if (mark is null)
            {
                continue;
            }

            var start = Math.Max(0, Math.Min(mark.StartMs, mark.EndMs));
            var end = Math.Max(0, Math.Max(mark.StartMs, mark.EndMs));
            if (end - start < 400)
            {
                continue;
            }

            clean.Add(new LevelMark
            {
                StartMs = start,
                EndMs = end,
                VolumePercent = Math.Clamp(mark.VolumePercent, 0, 200)
            });
            if (clean.Count == 400)
            {
                break;
            }
        }

        return clean;
    }

    private static DraftFile Read()
    {
        try
        {
            var path = StorePath();
            if (!File.Exists(path))
            {
                return new DraftFile();
            }

            var file = JsonSerializer.Deserialize<DraftFile>(File.ReadAllText(path));
            file ??= new DraftFile();
            file.Edits ??= [];
            file.Merges ??= [];
            file.Edits = file.Edits.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Id)).ToList();
            file.Merges = file.Merges.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Id)).ToList();
            return file;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new DraftFile();
        }
    }

    private static bool Write(DraftFile file)
    {
        try
        {
            var path = StorePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string StorePath() => string.IsNullOrWhiteSpace(_storeOverride) ? DefaultFilePath : _storeOverride;

    private static bool TryFullPath(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(path.Trim());
            return full.Length > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static string CleanId(string? id)
        => string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();

    private static string CleanName(string? name, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        if (text.Length > 80)
        {
            text = text[..80].Trim();
        }

        return string.IsNullOrWhiteSpace(text) ? "Draft" : text;
    }

    private static double Finite(double value, double fallback, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
        {
            return fallback;
        }

        return value;
    }

    private static double Fraction(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static long Stamp(string? savedUtc)
        => DateTimeOffset.TryParse(savedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var saved)
            ? saved.UtcTicks
            : 0;

    private sealed class DraftFile
    {
        public List<EditDraft> Edits { get; set; } = [];

        public List<MergeDraft> Merges { get; set; } = [];
    }
}

internal sealed class EditDraft
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string SavedUtc { get; set; } = string.Empty;

    public string VideoPath { get; set; } = string.Empty;

    public string VideoName { get; set; } = string.Empty;

    public double Speed { get; set; } = 1;

    public double Volume { get; set; } = 100;

    public long TrimStartMs { get; set; }

    public long TrimEndMs { get; set; }

    public List<TimeMark> Cuts { get; set; } = [];

    public List<LevelMark> Parts { get; set; } = [];

    public long? MarkStartMs { get; set; }

    public long? MarkEndMs { get; set; }

    public double CropLeft { get; set; }

    public double CropTop { get; set; }

    public double CropRight { get; set; } = 1;

    public double CropBottom { get; set; } = 1;

    public bool CropPreview { get; set; }

    public int QuarterTurns { get; set; }

    public bool FlipHorizontal { get; set; }

    public bool FlipVertical { get; set; }

    public bool VideoFadeIn { get; set; }

    public double VideoFadeInSeconds { get; set; } = 1;

    public bool VideoFadeOut { get; set; }

    public double VideoFadeOutSeconds { get; set; } = 1;

    public bool AudioFadeIn { get; set; }

    public double AudioFadeInSeconds { get; set; } = 1;

    public bool AudioFadeOut { get; set; }

    public double AudioFadeOutSeconds { get; set; } = 1;
}

internal sealed class MergeDraft
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string SavedUtc { get; set; } = string.Empty;

    public List<MergeClipDraft> Clips { get; set; } = [];
}

internal sealed class MergeClipDraft
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double DurationSeconds { get; set; }

    public double StartSeconds { get; set; }

    public double EndSeconds { get; set; }

    public bool HasAudio { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool VideoFadeIn { get; set; }

    public double VideoFadeInSeconds { get; set; } = 1;

    public bool VideoFadeOut { get; set; }

    public double VideoFadeOutSeconds { get; set; } = 1;

    public bool AudioFadeIn { get; set; }

    public double AudioFadeInSeconds { get; set; } = 1;

    public bool AudioFadeOut { get; set; }

    public double AudioFadeOutSeconds { get; set; } = 1;
}

internal sealed class TimeMark
{
    public long StartMs { get; set; }

    public long EndMs { get; set; }
}

internal sealed class LevelMark
{
    public long StartMs { get; set; }

    public long EndMs { get; set; }

    public int VolumePercent { get; set; }
}
