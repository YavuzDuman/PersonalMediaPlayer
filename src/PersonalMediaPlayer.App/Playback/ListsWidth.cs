using System.Text.Json;

namespace PersonalMediaPlayer.App.Playback;

// The lists panel width beside the video. The choice stays after the app closes.
// Backups leave the file out.
internal static class ListsWidth
{
    internal const double Default = 340;
    internal const double Min = 260;
    internal const double Max = 640;
    internal const double PictureFloor = 420;
    internal const double Gap = 12;
    internal const double HardMin = 220;
    internal const double TightPicture = 280;

    internal const string FileName = "lists-width.json";

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        FileName);

    private static string? _storeOverride;
    private static double _current = Default;
    private static string? _readyPath;
    private static bool _ready;

    internal static string? StoreOverride
    {
        get => _storeOverride;
        set
        {
            _storeOverride = value;
            _ready = false;
        }
    }

    internal static double Current
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

    internal static void Save(double width)
    {
        width = Clamp(width, 0);
        var path = FilePath();
        if (_ready && string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase) && width == _current)
        {
            return;
        }

        _current = width;
        _readyPath = path;
        _ready = true;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(new StoredWidth { Width = width }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static double Clamp(double width, double room)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            width = Default;
        }

        var lower = Min;
        var upper = Max;
        if (!double.IsNaN(room) && !double.IsInfinity(room) && room > 0)
        {
            var share = room - PictureFloor - Gap;
            if (share >= Min)
            {
                upper = Math.Min(Max, share);
            }
            else
            {
                var tight = Math.Max(HardMin, room - Gap - TightPicture);
                upper = Math.Min(Min, tight);
                lower = Math.Min(Min, upper);
            }
        }

        if (upper < lower)
        {
            upper = lower;
        }

        if (width < lower)
        {
            return lower;
        }

        if (width > upper)
        {
            return upper;
        }

        return width;
    }

    internal static double Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Width", out var width)
                || width.ValueKind != JsonValueKind.Number
                || !width.TryGetDouble(out var value))
            {
                return Default;
            }

            return Clamp(value, 0);
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    private static double ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Default;
            }

            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Default;
        }
    }

    private static string FilePath()
        => string.IsNullOrWhiteSpace(_storeOverride) ? DefaultFilePath : _storeOverride;

    private sealed class StoredWidth
    {
        public double Width { get; set; }
    }
}
