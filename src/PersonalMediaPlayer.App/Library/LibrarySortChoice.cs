using System.Globalization;

namespace PersonalMediaPlayer.App.Library;

// The library sort, including its direction. The choice stays after leaving the page
// and after the app closes. A missing or unreadable file uses newest. Backups leave the file out.
internal static class LibrarySortChoice
{
    internal const string FileName = "library-sort.txt";

    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        FileName);

    private static string? _storeOverride;
    private static int _current = LibrarySort.Newest;
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

    internal static int Current
    {
        get
        {
            var path = FilePath();
            if (!_ready || !string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _current = Read(path);
                _readyPath = path;
                _ready = true;
            }

            return _current;
        }
    }

    internal static int Normalize(int sortIndex)
        => sortIndex >= LibrarySort.Newest && sortIndex <= LibrarySort.Type
            ? sortIndex
            : LibrarySort.Newest;

    internal static void Save(int sortIndex)
    {
        sortIndex = Normalize(sortIndex);
        var path = FilePath();
        if (!_ready || !string.Equals(_readyPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _current = Read(path);
            _readyPath = path;
            _ready = true;
        }

        if (sortIndex == _current)
        {
            return;
        }

        _current = sortIndex;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, sortIndex.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static int Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return LibrarySort.Newest;
            }

            var text = File.ReadAllText(path).Trim();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                ? Normalize(index)
                : LibrarySort.Newest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LibrarySort.Newest;
        }
    }

    private static string FilePath()
        => string.IsNullOrWhiteSpace(_storeOverride) ? DefaultFilePath : _storeOverride;
}
