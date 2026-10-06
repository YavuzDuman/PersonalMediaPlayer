using System.IO;
using Microsoft.UI.Dispatching;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.Core;
using PersonalMediaPlayer.Core.Library;
using PersonalMediaPlayer.Core.Storage;

namespace PersonalMediaPlayer.App.Storage;

internal static class LibraryWatch
{
    private const int FolderPollDelayMs = 2000;
    private static readonly object Gate = new();
    private static readonly object Maintenance = new();
    private static readonly object TimerGate = new();
    private static readonly Dictionary<string, PendingFile> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Timer Timer = new(static _ => CheckPending(), null, Timeout.Infinite, Timeout.Infinite);
    private static readonly Timer FolderTimer = new(static _ => PollFolders(), null, Timeout.Infinite, Timeout.Infinite);
    private static IMediaLibrary? _library;
    private static DispatcherQueue? _queue;
    private static List<FileSystemWatcher> _watchers = [];
    private static HashSet<string> _present = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, long> AcceptedLength = new(StringComparer.OrdinalIgnoreCase);
    private static CancellationTokenSource? _notify;

    public static event EventHandler? Changed;

    public static void Start(IMediaLibrary library, DispatcherQueue queue)
    {
        _library = library;
        _queue = queue;
        Enqueue(() =>
        {
            try
            {
                SyncWatchers();
                try
                {
                    RememberExistingLengths(library);
                    foreach (var folder in library.ConnectedFolders())
                    {
                        ScheduleArrivedFolder(folder);
                    }
                }
                catch (Exception)
                {
                    // The library still opens when a connected folder cannot be read.
                }
            }
            finally
            {
                FolderTimer.Change(FolderPollDelayMs, Timeout.Infinite);
            }
        });
    }

    public static void FoldersChanged() => Enqueue(SyncWatchers);

    public static void Rescan() => Enqueue(() => ScheduleConnectedFolders(_library));

    private static void Enqueue(Action work)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                work();
            }
            catch (Exception)
            {
                // One folder change must not stop the watcher.
            }
        });
    }

    private static void SyncWatchers()
    {
        List<FileSystemWatcher> previous;
        lock (Maintenance)
        {
            var library = _library;
            if (library is null)
            {
                return;
            }

            var available = AvailableFolders(library);
            var next = new List<FileSystemWatcher>();
            foreach (var folder in available)
            {
                try
                {
                    var watcher = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                        InternalBufferSize = 64 * 1024
                    };
                    watcher.Created += (_, e) => Admit(e.FullPath);
                    watcher.Changed += (_, e) => Touch(e.FullPath);
                    watcher.Renamed += (_, e) => ApplyRename(e.OldFullPath, e.FullPath);
                    watcher.Deleted += (_, e) => ApplyDelete(e.FullPath);
                    watcher.Error += (_, _) => Enqueue(Recover);
                    watcher.EnableRaisingEvents = true;
                    next.Add(watcher);
                }
                catch (Exception)
                {
                    // Refresh remains available when this folder cannot be watched.
                }
            }

            lock (Gate)
            {
                previous = _watchers;
                _watchers = next;
                _present = available;
            }
        }

        foreach (var watcher in previous)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    private static void PollFolders()
    {
        try
        {
            var library = _library;
            if (library is null)
            {
                return;
            }

            var available = AvailableFolders(library);
            HashSet<string> previous;
            lock (Gate)
            {
                previous = _present;
            }

            if (previous.SetEquals(available))
            {
                return;
            }

            SyncWatchers();
            HashSet<string> present;
            lock (Gate)
            {
                present = _present;
            }

            foreach (var folder in previous)
            {
                if (!present.Contains(folder))
                {
                    CancelPending(folder);
                }
            }

            foreach (var folder in present)
            {
                if (!previous.Contains(folder))
                {
                    try
                    {
                        RememberExistingLengths(library, folder);
                    }
                    catch (Exception)
                    {
                        // The folder can still be scanned for files that are not linked yet.
                    }

                    var arrived = folder;
                    Enqueue(() => ScheduleArrivedFolder(arrived));
                }
            }

            Notify();
        }
        catch (Exception)
        {
            // One look at the folders must not stop the next look.
        }
        finally
        {
            FolderTimer.Change(FolderPollDelayMs, Timeout.Infinite);
        }
    }

    private static HashSet<string> AvailableFolders(IMediaLibrary library)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in library.ConnectedFolders())
        {
            if (FolderIsAvailable(folder))
            {
                available.Add(folder);
            }
        }

        return available;
    }

    // A removed drive raises no event when it comes back, and a missing path cannot be watched.
    private static bool FolderIsAvailable(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
            {
                if (!new DriveInfo(root).IsReady)
                {
                    return false;
                }
            }

            return Directory.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Recover()
    {
        SyncWatchers();
        ScheduleConnectedFolders(_library);
        Notify();
    }

    // Windows reports a moved-in folder and not the files already inside it.
    private static void Admit(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (IsDirectory(path))
        {
            var arrived = path;
            Enqueue(() => ScheduleArrivedFolder(arrived));
            return;
        }

        Touch(path);
    }

    private static void ScheduleArrivedFolder(string path)
    {
        var library = _library;
        if (library is null)
        {
            return;
        }

        IReadOnlyList<string> files;
        try
        {
            files = library.FindUnlinkedConnectedFiles(path);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var file in files)
        {
            Touch(file);
        }
    }

    private static void ScheduleConnectedFolders(IMediaLibrary? library)
    {
        if (library is null)
        {
            return;
        }

        try
        {
            RememberExistingLengths(library);
            foreach (var folder in library.ConnectedFolders())
            {
                ScheduleArrivedFolder(folder);
            }
        }
        catch (Exception)
        {
            // The next check can pick up a folder this pass could not read.
        }
    }

    private static void RememberExistingLengths(IMediaLibrary library, string? folder = null)
    {
        foreach (var item in library.GetItems())
        {
            if (!item.IsLinked || string.IsNullOrWhiteSpace(item.FilePath))
            {
                continue;
            }

            if (folder is null)
            {
                if (!library.ConnectedFolders().Any(connected => IsUnder(item.FilePath, connected)))
                {
                    continue;
                }
            }
            else if (!IsUnder(item.FilePath, folder))
            {
                continue;
            }

            if (!TryFileLength(item.FilePath, out var length) || length <= 0)
            {
                continue;
            }

            var key = Normalize(item.FilePath);
            lock (Gate)
            {
                if (!AcceptedLength.ContainsKey(key))
                {
                    AcceptedLength[key] = length;
                }
            }
        }
    }

    private static void Touch(string? path)
    {
        if (!IsMediaFile(path) || path is null)
        {
            return;
        }

        var key = Normalize(path);
        bool tracked;
        lock (Gate)
        {
            tracked = AcceptedLength.ContainsKey(key);
        }

        if (!tracked && _library?.ContainsLinkedPath(key) == true && TryFileLength(key, out var current) && current > 0)
        {
            lock (Gate)
            {
                AcceptedLength.TryAdd(key, current);
            }

            return;
        }

        lock (Gate)
        {
            if (!Pending.TryGetValue(key, out var pending))
            {
                pending = new PendingFile();
                Pending[key] = pending;
            }

            pending.Generation++;
            if (AcceptedLength.ContainsKey(key))
            {
                pending.Confirming = true;
                pending.NextUtc = DateTime.UtcNow.AddMilliseconds(400);
                pending.GiveUpUtc = DateTime.UtcNow.AddMinutes(30);
            }
            else
            {
                pending.Confirming = false;
                pending.Length = 0;
                pending.SinceUtc = default;
                pending.NextUtc = DateTime.UtcNow.AddMilliseconds(800);
                pending.GiveUpUtc = DateTime.UtcNow.AddMinutes(30);
            }

            Timer.Change(400, 400);
        }
    }

    private static void CheckPending()
    {
        if (!Monitor.TryEnter(TimerGate))
        {
            return;
        }

        try
        {
            List<string> due;
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                due = Pending.Where(pair => pair.Value.NextUtc <= now).Select(pair => pair.Key).ToList();
            }

            var library = _library;
            if (library is null)
            {
                return;
            }

            foreach (var path in due)
            {
                int generation;
                lock (Gate)
                {
                    if (!Pending.TryGetValue(path, out var pending))
                    {
                        continue;
                    }

                    generation = pending.Generation;
                }

                var probe = library.ProbeConnectedFile(path, out var probeLength);
                var sawFile = TryFileLength(path, out var fileLength);
                var link = false;
                var release = false;
                long linkedLength = 0;
                lock (Gate)
                {
                    if (!Pending.TryGetValue(path, out var pending) || pending.Generation != generation)
                    {
                        continue;
                    }

                    var comparable = sawFile ? fileLength : probe == ConnectedFileProbe.Ready ? probeLength : 0L;
                    if (pending.Confirming && AcceptedLength.TryGetValue(path, out var accepted))
                    {
                        if (comparable > 0 && comparable != accepted)
                        {
                            AcceptedLength.Remove(path);
                            pending.Confirming = false;
                            pending.Length = comparable;
                            pending.SinceUtc = DateTime.UtcNow;
                            pending.NextUtc = pending.SinceUtc.Add(ConnectedFileGrowth.QuietPeriod);
                            pending.GiveUpUtc = DateTime.UtcNow.AddMinutes(30);
                            release = true;
                        }
                        else if (probe == ConnectedFileProbe.Ignore || (comparable > 0 && comparable == accepted))
                        {
                            Pending.Remove(path);
                        }
                        else if (DateTime.UtcNow >= pending.GiveUpUtc)
                        {
                            Pending.Remove(path);
                        }
                        else
                        {
                            pending.NextUtc = DateTime.UtcNow.AddSeconds(1);
                        }
                    }
                    else if (probe == ConnectedFileProbe.Ignore)
                    {
                        Pending.Remove(path);
                    }
                    else if (probe == ConnectedFileProbe.Wait)
                    {
                        pending.Confirming = false;
                        pending.Length = 0;
                        pending.SinceUtc = default;
                        if (DateTime.UtcNow >= pending.GiveUpUtc)
                        {
                            Pending.Remove(path);
                        }
                        else
                        {
                            pending.NextUtc = DateTime.UtcNow.AddSeconds(1);
                        }
                    }
                    else if (ConnectedFileGrowth.IsStable(ref pending.Length, ref pending.SinceUtc, probeLength, DateTime.UtcNow))
                    {
                        Pending.Remove(path);
                        link = true;
                        linkedLength = probeLength;
                    }
                    else
                    {
                        var now = DateTime.UtcNow;
                        var dueAt = pending.SinceUtc.Add(ConnectedFileGrowth.QuietPeriod);
                        pending.NextUtc = dueAt > now ? dueAt : now.AddMilliseconds(400);
                        pending.GiveUpUtc = now.AddMinutes(30);
                    }

                    if (Pending.Count == 0)
                    {
                        Timer.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                }

                if (release && library.ReleaseUnfinishedConnectedFile(path))
                {
                    Notify();
                }

                if (link && library.LinkConnectedFile(path))
                {
                    if (!TryFileLength(path, out var recorded) || recorded <= 0)
                    {
                        recorded = linkedLength;
                    }

                    lock (Gate)
                    {
                        AcceptedLength[path] = recorded;
                    }

                    Notify();
                }
            }
        }
        finally
        {
            Monitor.Exit(TimerGate);
        }
    }

    private static void ApplyRename(string? oldPath, string? newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            return;
        }

        try
        {
            CancelPending(oldPath);

            var library = _library;
            if (library is null)
            {
                return;
            }

            var moves = library.FollowConnectedRename(oldPath, newPath);
            if (moves.Count > 0)
            {
                lock (Gate)
                {
                    foreach (var move in moves)
                    {
                        if (AcceptedLength.Remove(Normalize(move.OldPath), out var size))
                        {
                            AcceptedLength[Normalize(move.NewPath)] = size;
                        }
                    }
                }

                PublishMoves(moves);
            }

            if (IsDirectory(newPath))
            {
                var arrived = newPath;
                Enqueue(() => ScheduleArrivedFolder(arrived));
                if (moves.Count == 0 && library.ContainsLinkedPath(oldPath))
                {
                    Notify();
                }

                return;
            }

            if (moves.Count > 0)
            {
                return;
            }

            var leftBehind = library.ContainsLinkedPath(oldPath);
            Touch(newPath);
            if (leftBehind)
            {
                Notify();
            }
        }
        catch (Exception)
        {
            // The next Refresh can pick up a change this event could not finish.
        }
    }

    private static void ApplyDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            CancelPending(path);

            if (_library?.ContainsLinkedPath(path) == true)
            {
                Notify();
            }
        }
        catch (Exception)
        {
            // A missed delete still shows Missing after Refresh.
        }
    }

    private static void PublishMoves(IReadOnlyList<ConnectedLinkMove> moves)
    {
        var queue = _queue;
        if (queue is null)
        {
            return;
        }

        queue.TryEnqueue(() =>
        {
            foreach (var move in moves)
            {
                LibraryPaths.Move(move.OldPath, move.NewPath);
            }

            Changed?.Invoke(null, EventArgs.Empty);
        });
    }

    private static void Notify()
    {
        var queue = _queue;
        if (queue is null)
        {
            return;
        }

        queue.TryEnqueue(() =>
        {
            _notify?.Cancel();
            var notify = new CancellationTokenSource();
            _notify = notify;
            _ = PublishAsync(notify.Token);
        });
    }

    private static async Task PublishAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void CancelPending(string path)
    {
        var key = Normalize(path);
        var prefix = key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        lock (Gate)
        {
            Pending.Remove(key);
            List<string> nested = [];
            foreach (var pending in Pending.Keys)
            {
                if (pending.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    nested.Add(pending);
                }
            }

            foreach (var pending in nested)
            {
                Pending.Remove(pending);
            }

            if (Pending.Count == 0)
            {
                Timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    private static bool TryFileLength(string path, out long length)
    {
        try
        {
            length = new FileInfo(path).Length;
            return true;
        }
        catch (Exception)
        {
            length = 0;
            return false;
        }
    }

    private static bool IsUnder(string path, string folder)
    {
        var file = Normalize(path);
        var root = Normalize(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(file, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root + Path.DirectorySeparatorChar;
        return file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectory(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsMediaFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!MediaFileTypes.IsImage(path) && !MediaFileTypes.IsVideo(path)))
        {
            return false;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) == 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private sealed class PendingFile
    {
        public long Length;
        public DateTime SinceUtc;
        public bool Confirming;
        public int Generation;
        public DateTime NextUtc;
        public DateTime GiveUpUtc;
    }
}
