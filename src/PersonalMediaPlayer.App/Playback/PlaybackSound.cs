using System.Globalization;
using System.Text.Json;
using LibVLCSharp.Shared;

namespace PersonalMediaPlayer.App.Playback;

internal enum SoundPreset
{
    Flat = 0,
    Bass = 1,
    Voice = 2,
    Treble = 3,
    Custom = 4
}

internal readonly record struct PlaybackSoundState(
    SoundPreset Preset,
    int Bass,
    int Voice,
    int Treble,
    bool Normalize)
{
    public static PlaybackSoundState Default { get; } = new(SoundPreset.Flat, 0, 0, 0, false);
}

// Playback-only tone and level. The media file is never rewritten.
// The file stays out of backups.
internal static class PlaybackSound
{
    internal const int MinGain = -12;
    internal const int MaxGain = 12;
    internal const string FileName = "playback-sound.json";

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        FileName);

    private static string? _storeOverride;
    private static PlaybackSoundState _current = PlaybackSoundState.Default;
    private static PlaybackSoundState _applied = PlaybackSoundState.Default;
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

    internal static PlaybackSoundState Current
    {
        get
        {
            Ensure();
            return _current;
        }
    }

    internal static void Save(PlaybackSoundState state, bool apply)
    {
        state = Normalize(state);
        Ensure();
        if (!state.Equals(_current))
        {
            _current = state;
            try
            {
                var path = FilePath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, JsonSerializer.Serialize(new StoredSound
                {
                    Preset = state.Preset.ToString(),
                    Bass = state.Bass,
                    Voice = state.Voice,
                    Treble = state.Treble,
                    Normalize = state.Normalize
                }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (apply)
        {
            Publish();
        }
    }

    internal static void Commit()
    {
        Ensure();
        Publish();
    }

    internal static void Reset() => Save(PlaybackSoundState.Default, apply: true);

    internal static void AddTo(Media media)
    {
        foreach (var option in MediaOptions(Current))
        {
            media.AddOption(option);
        }
    }

    internal static PlaybackSoundState Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return PlaybackSoundState.Default;
            }

            return Normalize(new PlaybackSoundState(
                FieldPreset(root),
                FieldInt(root, "Bass"),
                FieldInt(root, "Voice"),
                FieldInt(root, "Treble"),
                FieldBool(root, "Normalize")));
        }
        catch (JsonException)
        {
            return PlaybackSoundState.Default;
        }
    }

    internal static PlaybackSoundState Normalize(PlaybackSoundState state)
        => new(
            Enum.IsDefined(state.Preset) ? state.Preset : SoundPreset.Flat,
            ClampGain(state.Bass),
            ClampGain(state.Voice),
            ClampGain(state.Treble),
            state.Normalize);

    internal static int ClampGain(int gain)
    {
        if (gain < MinGain)
        {
            return MinGain;
        }

        if (gain > MaxGain)
        {
            return MaxGain;
        }

        return gain;
    }

    internal static string GainLabel(int gain)
    {
        gain = ClampGain(gain);
        return gain > 0 ? "+" + gain.ToString(CultureInfo.InvariantCulture) + " dB" : gain.ToString(CultureInfo.InvariantCulture) + " dB";
    }

    // Ten bands, low to high: 60, 170, 310, 600, 1000, 3000, 6000, 12000, 14000, 16000 Hz.
    internal static int[] Bands(PlaybackSoundState state)
    {
        state = Normalize(state);
        return state.Preset switch
        {
            SoundPreset.Bass => [6, 5, 4, 1, 0, 0, -1, -2, -2, -2],
            SoundPreset.Voice => [-2, -1, 0, 2, 4, 4, 2, 0, -1, -2],
            SoundPreset.Treble => [-2, -1, -1, 0, 0, 1, 3, 5, 5, 4],
            SoundPreset.Custom =>
            [
                state.Bass,
                state.Bass,
                state.Bass / 2,
                state.Voice / 2,
                state.Voice,
                state.Voice,
                state.Treble / 2,
                state.Treble,
                state.Treble,
                state.Treble
            ],
            _ => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
        };
    }

    internal static int PreampDb(PlaybackSoundState state)
    {
        var peak = 0;
        foreach (var band in Bands(state))
        {
            if (band > peak)
            {
                peak = band;
            }
        }

        var cut = peak * 35 / 100;
        return cut > 8 ? -8 : -cut;
    }

    internal static IReadOnlyList<string> MediaOptions(PlaybackSoundState state)
    {
        state = Normalize(state);
        var equalizer = false;
        foreach (var band in Bands(state))
        {
            if (band != 0)
            {
                equalizer = true;
                break;
            }
        }

        if (!equalizer && !state.Normalize)
        {
            return [];
        }

        var options = new List<string>(12);
        var filter = equalizer && state.Normalize
            ? "equalizer:compressor"
            : equalizer ? "equalizer" : "compressor";
        options.Add(":audio-filter=" + filter);
        if (equalizer)
        {
            options.Add(":equalizer-vlcfreqs=1");
            options.Add(":equalizer-preamp=" + PreampDb(state).ToString(CultureInfo.InvariantCulture));
            options.Add(":equalizer-bands=" + string.Join(" ", Bands(state)));
        }

        if (state.Normalize)
        {
            options.Add(":compressor-rms-peak=0.5");
            options.Add(":compressor-attack=20");
            options.Add(":compressor-release=250");
            options.Add(":compressor-threshold=-18");
            options.Add(":compressor-ratio=3");
            options.Add(":compressor-knee=6");
            options.Add(":compressor-makeup-gain=8");
        }

        return options;
    }

    private static void Publish()
    {
        if (_ready && _applied.Equals(_current))
        {
            return;
        }

        _applied = _current;
        Changed?.Invoke();
    }

    private static void Ensure()
    {
        var path = FilePath();
        if (_ready && string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _current = ReadFile(path);
        _applied = _current;
        _readyPath = path;
        _ready = true;
    }

    private static PlaybackSoundState ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return PlaybackSoundState.Default;
            }

            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PlaybackSoundState.Default;
        }
    }

    private static string FilePath()
        => string.IsNullOrWhiteSpace(_storeOverride) ? DefaultFilePath : _storeOverride;

    private static int FieldInt(JsonElement root, string name)
    {
        var field = Field(root, name);
        if (field is JsonElement element
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var value))
        {
            return value;
        }

        return 0;
    }

    private static bool FieldBool(JsonElement root, string name)
    {
        var field = Field(root, name);
        return field is JsonElement element
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            && element.GetBoolean();
    }

    private static SoundPreset FieldPreset(JsonElement root)
    {
        var field = Field(root, "Preset");
        if (field is not JsonElement element || element.ValueKind != JsonValueKind.String)
        {
            return SoundPreset.Flat;
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text) || char.IsDigit(text[0]) || text[0] == '-' || text[0] == '+')
        {
            return SoundPreset.Flat;
        }

        return Enum.TryParse<SoundPreset>(text, ignoreCase: true, out var preset) && Enum.IsDefined(preset)
            ? preset
            : SoundPreset.Flat;
    }

    private static JsonElement? Field(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals(name))
            {
                return property.Value;
            }
        }

        return null;
    }

    private sealed class StoredSound
    {
        public string? Preset { get; set; }

        public int Bass { get; set; }

        public int Voice { get; set; }

        public int Treble { get; set; }

        public bool Normalize { get; set; }
    }
}
