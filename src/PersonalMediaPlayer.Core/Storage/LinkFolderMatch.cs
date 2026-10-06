namespace PersonalMediaPlayer.Core.Storage;

public enum LinkMatchState
{
    Ready,
    Choose,
    None
}

public sealed class LinkMatch
{
    public LinkMatch(string currentPath, LinkMatchState state, string? chosenPath, IReadOnlyList<string> candidates)
    {
        CurrentPath = currentPath;
        State = state;
        ChosenPath = chosenPath;
        Candidates = candidates;
    }

    public string CurrentPath { get; }

    public string FileName => Path.GetFileName(CurrentPath);

    public LinkMatchState State { get; }

    public string? ChosenPath { get; }

    public IReadOnlyList<string> Candidates { get; }
}

public static class LinkFolderMatch
{
    public static IReadOnlyList<string> FindMedia(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(folder));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                if (MediaFileTypes.IsImage(entry) || MediaFileTypes.IsVideo(entry))
                {
                    found.Add(Path.GetFullPath(entry));
                }
            }
        }

        return found;
    }

    public static IReadOnlyList<LinkMatch> Match(IReadOnlyList<string> missingPaths, IReadOnlyList<string> files)
    {
        var missing = missingPaths.Select(NormalizeOrOriginal).ToArray();
        var available = files.Select(NormalizeOrNull).Where(path => path is not null).Select(path => path!).ToArray();
        var common = CommonDirectoryCount(missing.Where(path => NormalizeOrNull(path) is not null).ToArray());
        var drafts = new List<Draft>();
        foreach (var current in missing)
        {
            if (NormalizeOrNull(current) is not string full)
            {
                drafts.Add(new Draft(current, LinkMatchState.None, null, []));
                continue;
            }

            var candidates = available
                .Where(file => SameFileName(full, file) && !string.Equals(file, full, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length == 0)
            {
                drafts.Add(new Draft(full, LinkMatchState.None, null, []));
                continue;
            }

            var relative = RelativeSegments(full, common);
            var structural = relative.Length >= 2
                ? candidates.Where(file => EndsWithSegments(file, relative)).ToArray()
                : [];
            if (structural.Length == 1)
            {
                drafts.Add(new Draft(full, LinkMatchState.Ready, structural[0], structural));
            }
            else if (structural.Length > 1)
            {
                drafts.Add(new Draft(full, LinkMatchState.Choose, null, structural));
            }
            else if (candidates.Length == 1)
            {
                drafts.Add(new Draft(full, LinkMatchState.Ready, candidates[0], candidates));
            }
            else
            {
                drafts.Add(new Draft(full, LinkMatchState.Choose, null, candidates));
            }
        }

        foreach (var group in drafts.Where(draft => draft.State == LinkMatchState.Ready && draft.ChosenPath is not null)
                     .GroupBy(draft => draft.ChosenPath!, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() < 2)
            {
                continue;
            }

            foreach (var draft in group)
            {
                draft.State = LinkMatchState.Choose;
                draft.ChosenPath = null;
            }
        }

        return drafts.Select(draft => new LinkMatch(draft.CurrentPath, draft.State, draft.ChosenPath, draft.Candidates)).ToArray();
    }

    private static bool SameFileName(string left, string right)
        => string.Equals(Path.GetFileName(left), Path.GetFileName(right), StringComparison.OrdinalIgnoreCase)
           && string.Equals(Path.GetExtension(left), Path.GetExtension(right), StringComparison.OrdinalIgnoreCase);

    private static int CommonDirectoryCount(IReadOnlyList<string> paths)
    {
        var directories = new List<string[]>();
        foreach (var path in paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                return 0;
            }

            directories.Add(Split(directory));
        }

        if (directories.Count == 0)
        {
            return 0;
        }

        var count = directories.Min(parts => parts.Length);
        var shared = 0;
        while (shared < count && directories.All(parts => string.Equals(parts[shared], directories[0][shared], StringComparison.OrdinalIgnoreCase)))
        {
            shared++;
        }

        return shared;
    }

    private static string[] RelativeSegments(string path, int commonDirectoryCount)
    {
        var parts = Split(path);
        if (commonDirectoryCount <= 0 || commonDirectoryCount >= parts.Length)
        {
            return [Path.GetFileName(path)];
        }

        return parts.Skip(commonDirectoryCount).ToArray();
    }

    private static bool EndsWithSegments(string path, IReadOnlyList<string> suffix)
    {
        var parts = Split(path);
        if (suffix.Count == 0 || parts.Length < suffix.Count)
        {
            return false;
        }

        for (var i = 0; i < suffix.Count; i++)
        {
            if (!string.Equals(parts[parts.Length - suffix.Count + i], suffix[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Split(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(part => part.Length > 0)
            .ToArray();

    private static string NormalizeOrOriginal(string path)
        => NormalizeOrNull(path) ?? path;

    private static string? NormalizeOrNull(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private sealed class Draft
    {
        public Draft(string currentPath, LinkMatchState state, string? chosenPath, IReadOnlyList<string> candidates)
        {
            CurrentPath = currentPath;
            State = state;
            ChosenPath = chosenPath;
            Candidates = candidates;
        }

        public string CurrentPath { get; }

        public LinkMatchState State { get; set; }

        public string? ChosenPath { get; set; }

        public IReadOnlyList<string> Candidates { get; }
    }
}
