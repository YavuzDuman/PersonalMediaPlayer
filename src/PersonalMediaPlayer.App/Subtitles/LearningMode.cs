namespace PersonalMediaPlayer.App.Subtitles;

// Whether word tools are shown. A missing or unreadable file stays on, so today's
// behavior remains until the switch is turned off. Backups leave the file out.
// Turning this off does not change saved words.
internal static class LearningMode
{
    internal const string FileName = "learning-mode.txt";

    internal const string SaveFailedMessage =
        "Could not save Learning mode. This choice lasts until you close the app.";

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        FileName);

    private static string? _storeOverride;
    private static bool _enabled = true;
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

    internal static bool Enabled
    {
        get
        {
            var path = FilePath();
            if (!_ready || !string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _enabled = Read(path);
                _readyPath = path;
                _ready = true;
            }

            return _enabled;
        }
    }

    internal static bool Save(bool enabled)
    {
        var path = FilePath();
        if (!_ready || !string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _enabled = Read(path);
            _readyPath = path;
            _ready = true;
        }

        if (enabled == _enabled)
        {
            return true;
        }

        _enabled = enabled;
        var wrote = true;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, enabled ? "true" : "false");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            wrote = false;
        }

        Changed?.Invoke();
        return wrote;
    }

    private static bool Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return true;
            }

            return !bool.TryParse(File.ReadAllText(path).Trim(), out var enabled) || enabled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string FilePath()
        => string.IsNullOrWhiteSpace(_storeOverride) ? DefaultFilePath : _storeOverride;
}
