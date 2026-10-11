using System.Globalization;
using System.Text.Json;

namespace PersonalMediaPlayer.App.Subtitles;

internal enum SubtitleSize
{
    Small = 0,
    Medium = 1,
    Large = 2
}

internal enum SubtitleTextColor
{
    White = 0,
    Yellow = 1,
    Cyan = 2,
    Green = 3,
    Black = 4
}

internal enum SubtitlePlate
{
    Dark = 0,
    Light = 1,
    None = 2
}

internal enum SubtitleSpot
{
    Bottom = 0,
    Middle = 1,
    Top = 2
}

internal readonly record struct SubtitleStyle(
    int DelayMs,
    SubtitleSize Size,
    SubtitleTextColor Color,
    SubtitlePlate Plate,
    SubtitleSpot Spot)
{
    public static SubtitleStyle Default { get; } = new(
        0,
        SubtitleSize.Medium,
        SubtitleTextColor.White,
        SubtitlePlate.Dark,
        SubtitleSpot.Bottom);
}

// The on-picture caption look. A positive delay shows a line later: the cue is chosen
// from the picture time minus that delay. The file stays out of backups.
internal static class SubtitleLook
{
    internal const int MinDelayMs = -15_000;
    internal const int MaxDelayMs = 15_000;
    internal const int TickMs = 100;
    internal const int StepMs = 500;
    internal const double SideMargin = 24;
    internal const double BottomGap = 176;
    internal const double TopGap = 72;

    internal const string FileName = "subtitle-look.json";

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        FileName);

    private static string? _storeOverride;
    private static SubtitleStyle _current = SubtitleStyle.Default;
    private static string? _readyPath;
    private static bool _ready;

    internal static event Action? Changed;

    internal static string? StoreOverride
    {
        get => _storeOverride;
        set
        {
            _storeOverride = value;
            _ready = false;
        }
    }

    internal static SubtitleStyle Current
    {
        get
        {
            var path = FilePath();
            if (!_ready || !string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _current = ReadFile(path);
                _readyPath = path;
                _ready = true;
            }

            return _current;
        }
    }

    internal static void Save(SubtitleStyle style)
    {
        style = Normalize(style);
        var path = FilePath();
        if (_ready && string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase) && style.Equals(_current))
        {
            return;
        }

        _current = style;
        _readyPath = path;
        _ready = true;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(new StoredLook
            {
                DelayMs = style.DelayMs,
                Size = style.Size.ToString(),
                Color = style.Color.ToString(),
                Background = style.Plate.ToString(),
                Position = style.Spot.ToString()
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        Changed?.Invoke();
    }

    internal static void Reset() => Save(SubtitleStyle.Default);

    internal static SubtitleStyle Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return SubtitleStyle.Default;
            }

            return Normalize(new SubtitleStyle(
                FieldInt(root, "DelayMs", 0),
                FieldEnum(root, "Size", SubtitleSize.Medium),
                FieldEnum(root, "Color", SubtitleTextColor.White),
                FieldEnum(root, "Background", SubtitlePlate.Dark),
                FieldEnum(root, "Position", SubtitleSpot.Bottom)));
        }
        catch (JsonException)
        {
            return SubtitleStyle.Default;
        }
    }

    internal static SubtitleStyle Normalize(SubtitleStyle style)
        => new(
            ClampDelay(style.DelayMs),
            Defined(style.Size, SubtitleSize.Medium),
            Defined(style.Color, SubtitleTextColor.White),
            Defined(style.Plate, SubtitlePlate.Dark),
            Defined(style.Spot, SubtitleSpot.Bottom));

    internal static int ClampDelay(int delayMs)
    {
        var snapped = (int)Math.Round(delayMs / (double)TickMs, MidpointRounding.AwayFromZero) * TickMs;
        if (snapped < MinDelayMs)
        {
            return MinDelayMs;
        }

        if (snapped > MaxDelayMs)
        {
            return MaxDelayMs;
        }

        return snapped;
    }

    internal static int Step(int delayMs, int deltaMs)
    {
        var next = (long)ClampDelay(delayMs) + deltaMs;
        if (next > MaxDelayMs)
        {
            return MaxDelayMs;
        }

        if (next < MinDelayMs)
        {
            return MinDelayMs;
        }

        return ClampDelay((int)next);
    }

    internal static int FromSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return 0;
        }

        var tenths = (int)Math.Round(seconds * 10, MidpointRounding.AwayFromZero);
        return ClampDelay(tenths * TickMs);
    }

    internal static double Seconds(int delayMs) => ClampDelay(delayMs) / 1000.0;

    internal static string DelayLabel(int delayMs)
    {
        var delay = ClampDelay(delayMs);
        if (delay == 0)
        {
            return "In sync";
        }

        var tenths = Math.Abs(delay) / TickMs;
        var text = (tenths / 10).ToString(CultureInfo.InvariantCulture)
            + "."
            + (tenths % 10).ToString(CultureInfo.InvariantCulture);
        return delay > 0 ? "+" + text + " s" : "-" + text + " s";
    }

    internal static int FontSize(SubtitleSize size) => size switch
    {
        SubtitleSize.Small => 16,
        SubtitleSize.Large => 34,
        _ => 22
    };

    internal static uint TextArgb(SubtitleTextColor color) => color switch
    {
        SubtitleTextColor.Yellow => 0xFFFFD60A,
        SubtitleTextColor.Cyan => 0xFF7DD3FC,
        SubtitleTextColor.Green => 0xFF7DFF9A,
        SubtitleTextColor.Black => 0xFF1C1C1C,
        _ => 0xFFFFFFFF
    };

    internal static uint PlateArgb(SubtitlePlate plate) => plate switch
    {
        SubtitlePlate.Light => 0xE6FFFFFF,
        SubtitlePlate.None => 0x00000000,
        _ => 0xCC000000
    };

    // A saved word stores the cue start. Opening it lands where that line is showing.
    internal static long PlaybackMs(long markMs, int delayMs)
    {
        var shifted = markMs + ClampDelay(delayMs);
        return shifted < 0 ? 0 : shifted;
    }

    internal static long PlaybackMs(long markMs) => PlaybackMs(markMs, Current.DelayMs);

    // The first cue that covers the shifted time wins. The cue's own end is not included.
    internal static int CueAt(IReadOnlyList<SubtitleCue> cues, long playbackMs, int delayMs)
    {
        var time = playbackMs - ClampDelay(delayMs);
        for (var index = 0; index < cues.Count; index++)
        {
            var cue = cues[index];
            if (time >= cue.StartMs && time < cue.EndMs)
            {
                return index;
            }
        }

        return -1;
    }

    private static SubtitleStyle ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return SubtitleStyle.Default;
            }

            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SubtitleStyle.Default;
        }
    }

    private static string FilePath()
        => string.IsNullOrWhiteSpace(_storeOverride) ? DefaultFilePath : _storeOverride;

    private static TEnum Defined<TEnum>(TEnum value, TEnum fallback) where TEnum : struct, Enum
        => Enum.IsDefined(value) ? value : fallback;

    private static int FieldInt(JsonElement root, string name, int fallback)
    {
        var field = Field(root, name);
        if (field is JsonElement element
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var value))
        {
            return value;
        }

        return fallback;
    }

    private static TEnum FieldEnum<TEnum>(JsonElement root, string name, TEnum fallback) where TEnum : struct, Enum
    {
        var field = Field(root, name);
        if (field is not JsonElement element || element.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text) || char.IsDigit(text[0]) || text[0] == '-' || text[0] == '+')
        {
            return fallback;
        }

        return Enum.TryParse<TEnum>(text, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : fallback;
    }

    private static JsonElement? Field(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private sealed class StoredLook
    {
        public int DelayMs { get; set; }

        public string? Size { get; set; }

        public string? Color { get; set; }

        public string? Background { get; set; }

        public string? Position { get; set; }
    }
}
