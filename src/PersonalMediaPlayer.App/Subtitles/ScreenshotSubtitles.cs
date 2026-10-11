namespace PersonalMediaPlayer.App.Subtitles;

// Whether saved and copied frames include the subtitle drawn by the app.
// The choice stays after the app closes. Backups leave the file out.
internal static class ScreenshotSubtitles
{
    internal const string FileName = "screenshot-subtitles.txt";

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        FileName);

    private static string? _storeOverride;
    private static bool _include;
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

    internal static bool Include
    {
        get
        {
            var path = FilePath();
            if (!_ready || !string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _include = Read(path);
                _readyPath = path;
                _ready = true;
            }

            return _include;
        }
    }

    internal static void Save(bool include)
    {
        var path = FilePath();
        if (_ready && string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase) && include == _include)
        {
            return;
        }

        _include = include;
        _readyPath = path;
        _ready = true;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, include ? "true" : "false");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            return bool.TryParse(File.ReadAllText(path).Trim(), out var include) && include;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string FilePath()
        => string.IsNullOrWhiteSpace(_storeOverride) ? DefaultFilePath : _storeOverride;
}
