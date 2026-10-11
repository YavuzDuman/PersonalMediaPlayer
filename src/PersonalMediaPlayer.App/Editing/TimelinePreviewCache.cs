using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PersonalMediaPlayer.App.Editing;

// One strip of small frames is saved per file, at the real timestamps.
// Later hovers read that strip instead of decoding the video again.
// A frame is used only when it is close to the time under the pointer.
// A new length or write time builds another strip.
internal static class TimelinePreviewCache
{
    internal const long SampleMs = 100;

    internal const long MaxGapMs = 500;

    internal const int MaxFrames = 4_000;

    private const string ReadyMark = "v2";

    internal static string? StoreOverride { get; set; }

    private static readonly object Gate = new();

    private static readonly SemaphoreSlim BuildGate = new(1, 1);

    private static Strip? _strip;

    private static CancellationTokenSource? _build;

    private static int _pass;

    private static string? _decodedFile;

    private static TimelinePreviewFrame.PreviewPixels? _decoded;

    private static StripKey? _exactKey;

    private static readonly List<ExactShot> _exact = [];

    internal readonly record struct StripKey(string Path, long Length, long WriteTicks);

    internal static long SlotMs(double durationSeconds)
    {
        if (durationSeconds <= 0 || double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds))
        {
            return SampleMs;
        }

        var spaced = (long)(durationSeconds * 1000d / MaxFrames);
        if (spaced < SampleMs)
        {
            return SampleMs;
        }

        return spaced > MaxGapMs ? MaxGapMs : spaced;
    }

    internal static bool KeepFrame(long timeMs, long lastKeptMs, long slotMs)
        => lastKeptMs < 0 || timeMs - lastKeptMs >= slotMs;

    // The nearest saved frame is used only when it is close to the wanted time.
    // A keyframe several seconds away is not a stand-in for that moment.
    internal static int Choose(IReadOnlyList<long> timesMs, long wantedMs, long maxDistanceMs)
    {
        if (timesMs.Count == 0 || wantedMs < 0 || maxDistanceMs < 0)
        {
            return -1;
        }

        var index = -1;
        var best = long.MaxValue;
        for (var i = 0; i < timesMs.Count; i++)
        {
            var distance = Math.Abs(timesMs[i] - wantedMs);
            if (distance < best)
            {
                best = distance;
                index = i;
            }
        }

        return index >= 0 && best <= maxDistanceMs ? index : -1;
    }

    internal static void Ensure(string path, double durationSeconds)
        => _ = BuildAsync(path, durationSeconds);

    internal static Task BuildAsync(string path, double durationSeconds)
    {
        var key = KeyFor(path);
        if (key is null || durationSeconds <= 0 || double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds))
        {
            return Task.CompletedTask;
        }

        lock (Gate)
        {
            if (_strip is { } current
                && current.Key.Equals(key.Value)
                && Math.Abs(current.DurationSeconds - durationSeconds) < 0.001
                && (current.Complete || current.Build is { IsCompleted: false }))
            {
                return current.Build ?? Task.CompletedTask;
            }

            if (LoadReady(key.Value))
            {
                return Task.CompletedTask;
            }

            var previous = _build;
            var build = new CancellationTokenSource();
            _build = build;
            var pass = ++_pass;
            previous?.Cancel();
            var slot = SlotMs(durationSeconds);
            var task = RunBuild(key.Value, slot, pass, build.Token);
            _strip = new Strip(key.Value, durationSeconds, FolderFor(key.Value), pass, task, slot);
            _decoded = null;
            _decodedFile = null;
            if (_exactKey is not { } exactKey || !exactKey.Equals(key.Value))
            {
                _exact.Clear();
                _exactKey = null;
            }

            return task;
        }
    }

    // Stops a strip that is still being built. A finished strip stays in memory
    // and on disk. Hiding the hover card does not call this.
    internal static void Cancel()
    {
        CancellationTokenSource? build;
        lock (Gate)
        {
            build = _build;
            _build = null;
            if (_strip is { Complete: false })
            {
                _strip = null;
            }
        }

        build?.Cancel();
    }

    internal static void ClearMemory()
    {
        lock (Gate)
        {
            _strip = null;
            _decoded = null;
            _decodedFile = null;
            _exact.Clear();
            _exactKey = null;
        }
    }

    internal static void Remember(string path, TimeSpan time, TimelinePreviewFrame.PreviewPixels pixels)
    {
        var key = KeyFor(path);
        if (key is null || pixels.Width < 2 || pixels.Height < 2 || pixels.Bgra.Length < 16)
        {
            return;
        }

        var ms = Math.Max(0, (long)time.TotalMilliseconds);
        lock (Gate)
        {
            if (_exactKey is not { } exactKey || !exactKey.Equals(key.Value))
            {
                _exact.Clear();
                _exactKey = key.Value;
            }

            for (var i = 0; i < _exact.Count; i++)
            {
                if (Math.Abs(_exact[i].Ms - ms) <= 40)
                {
                    _exact[i] = new ExactShot(ms, pixels);
                    return;
                }
            }

            _exact.Add(new ExactShot(ms, pixels));
            if (_exact.Count > 64)
            {
                _exact.RemoveAt(0);
            }
        }
    }

    internal static TimelinePreviewFrame.PreviewPixels? TryGet(string path, TimeSpan time)
    {
        var key = KeyFor(path);
        if (key is null)
        {
            return null;
        }

        var wanted = (long)time.TotalMilliseconds;
        string? file;
        lock (Gate)
        {
            if (_strip is not { } strip || !strip.Key.Equals(key.Value))
            {
                if (!LoadReady(key.Value))
                {
                    return ClosestExact(key.Value, wanted, SampleMs)?.Pixels;
                }

                strip = _strip!;
            }

            var exact = ClosestExact(key.Value, wanted, strip.MaxDistance);
            var times = strip.Times;
            var index = Choose(times, wanted, strip.MaxDistance);
            var gridDistance = index < 0 ? long.MaxValue : Math.Abs(times[index] - wanted);
            if (exact is { } shot && shot.Distance <= gridDistance)
            {
                return shot.Pixels;
            }

            if (index < 0)
            {
                return null;
            }

            file = Path.Combine(strip.Folder, strip.Frames[index].Name);
            if (string.Equals(_decodedFile, file, StringComparison.OrdinalIgnoreCase) && _decoded is { } cached)
            {
                return cached;
            }
        }

        try
        {
            var pixels = TimelinePreviewFrame.FromEncoded(File.ReadAllBytes(file));
            if (pixels is null)
            {
                return null;
            }

            lock (Gate)
            {
                _decodedFile = file;
                _decoded = pixels;
            }

            return pixels;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task RunBuild(StripKey key, long slotMs, int pass, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            await BuildGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (!StillCurrent(pass, cancellationToken))
            {
                return;
            }

            var folder = FolderFor(key);
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }

                Directory.CreateDirectory(folder);
                RemoveOlder(folder, PathHash(key));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            var saved = await ExtractAsync(key.Path, folder, slotMs, pass, cancellationToken);
            if (saved is null || saved.Count == 0 || !StillCurrent(pass, cancellationToken))
            {
                return;
            }

            try
            {
                var lines = new string[saved.Count];
                for (var i = 0; i < saved.Count; i++)
                {
                    lines[i] = string.Create(CultureInfo.InvariantCulture, $"{saved[i].Ms} {saved[i].Name}");
                }

                File.WriteAllLines(Path.Combine(folder, "frames.txt"), lines);
                File.WriteAllText(Path.Combine(folder, "ready.txt"), string.Create(CultureInfo.InvariantCulture, $"{ReadyMark} {slotMs}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            lock (Gate)
            {
                if (_strip is { } strip && strip.Pass == pass)
                {
                    if (strip.Frames.Count != saved.Count)
                    {
                        strip.Frames.Clear();
                        strip.Frames.AddRange(saved);
                    }

                    strip.Complete = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // The hover can still read one frame directly.
        }
        finally
        {
            BuildGate.Release();
        }
    }

    private static async Task<List<SavedFrame>?> ExtractAsync(string path, string folder, long slotMs, int pass, CancellationToken cancellationToken)
    {
        var ffmpeg = TimelinePreviewFrame.FindFfmpeg();
        if (ffmpeg is null || !StillCurrent(pass, cancellationToken))
        {
            return null;
        }

        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-nostdin");
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-nostats");
        start.ArgumentList.Add("-loglevel");
        start.ArgumentList.Add("info");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(path);
        start.ArgumentList.Add("-map");
        start.ArgumentList.Add("0:v:0");
        start.ArgumentList.Add("-an");
        start.ArgumentList.Add("-vf");
        start.ArgumentList.Add(SampleFilter(slotMs));
        start.ArgumentList.Add("-fps_mode");
        start.ArgumentList.Add("passthrough");
        start.ArgumentList.Add("-q:v");
        start.ArgumentList.Add("8");
        start.ArgumentList.Add("-start_number");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add(Path.Combine(folder, "%04d.jpg"));

        using var process = Process.Start(start);
        if (process is null)
        {
            return null;
        }

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception)
        {
            // Priority is a courtesy so playback keeps the processor.
        }

        using var cancel = cancellationToken.Register(() => TryKill(process));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var saved = new List<SavedFrame>();
        var lastKept = -1L;
        var nextIndex = 0;
        var pending = new Queue<(long Ms, int Index)>();
        var keepGap = Math.Max(1, slotMs / 2);
        try
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!TryReadPts(line, out var seconds))
                {
                    continue;
                }

                nextIndex++;
                pending.Enqueue(((long)Math.Round(seconds * 1000), nextIndex));
                Drain(final: false);
            }

            await process.WaitForExitAsync(cancellationToken);
            await stdout;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            TryKill(process);
            try
            {
                await stdout;
            }
            catch (Exception)
            {
                // The pipe closed when the process was stopped.
            }

            return null;
        }

        if (!StillCurrent(pass, cancellationToken) || process.ExitCode != 0)
        {
            return null;
        }

        Drain(final: true);
        return saved;

        void Drain(bool final)
        {
            while (pending.Count > 0)
            {
                if (!final && pending.Count == 1)
                {
                    return;
                }

                var item = pending.Peek();
                if (!FrameReady(item.Index))
                {
                    if (!final)
                    {
                        return;
                    }

                    pending.Dequeue();
                    continue;
                }

                pending.Dequeue();
                Commit(item.Ms, item.Index);
            }
        }

        bool FrameReady(int index)
        {
            try
            {
                var info = new FileInfo(Path.Combine(folder, $"{index:0000}.jpg"));
                return info.Exists && info.Length >= 32;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        void Commit(long ms, int index)
        {
            if (index <= 0)
            {
                return;
            }

            var name = $"{index:0000}.jpg";
            var full = Path.Combine(folder, name);
            if (!File.Exists(full))
            {
                return;
            }

            try
            {
                if (new FileInfo(full).Length < 32)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            if (!KeepFrame(ms, lastKept, keepGap))
            {
                TryDelete(full);
                return;
            }

            var frame = new SavedFrame(ms, name);
            saved.Add(frame);
            Publish(pass, frame);
            lastKept = ms;
        }
    }

    private static string SampleFilter(long slotMs)
    {
        var fps = (1000d / Math.Max(1, slotMs)).ToString("0.###", CultureInfo.InvariantCulture);
        return "fps=" + fps + ",scale=320:-2,showinfo";
    }

    private static void Publish(int pass, SavedFrame frame)
    {
        lock (Gate)
        {
            if (_strip is not { } strip || strip.Pass != pass || strip.Complete)
            {
                return;
            }

            strip.Frames.Add(frame);
        }
    }

    private static ExactHit? ClosestExact(StripKey key, long wantedMs, long maxDistanceMs)
    {
        if (_exactKey is not { } exactKey || !exactKey.Equals(key) || _exact.Count == 0 || maxDistanceMs < 0)
        {
            return null;
        }

        var best = long.MaxValue;
        var index = -1;
        for (var i = 0; i < _exact.Count; i++)
        {
            var distance = Math.Abs(_exact[i].Ms - wantedMs);
            if (distance < best)
            {
                best = distance;
                index = i;
            }
        }

        return index >= 0 && best <= maxDistanceMs ? new ExactHit(best, _exact[index].Pixels) : null;
    }

    private static bool LoadReady(StripKey key)
    {
        var folder = FolderFor(key);
        var ready = Path.Combine(folder, "ready.txt");
        var index = Path.Combine(folder, "frames.txt");
        if (!File.Exists(ready) || !File.Exists(index))
        {
            return false;
        }

        List<string> lines;
        long maxDistance;
        try
        {
            var readyText = File.ReadAllText(ready).Trim();
            if (!readyText.StartsWith(ReadyMark, StringComparison.Ordinal))
            {
                return false;
            }

            maxDistance = SampleMs;
            if (readyText.Length > ReadyMark.Length
                && long.TryParse(readyText.AsSpan(ReadyMark.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                && parsed >= SampleMs
                && parsed <= MaxGapMs)
            {
                maxDistance = parsed;
            }

            lines = [.. File.ReadAllLines(index)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var frames = new List<SavedFrame>();
        foreach (var line in lines)
        {
            var split = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length != 2 || !long.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            {
                continue;
            }

            frames.Add(new SavedFrame(ms, split[1]));
        }

        if (frames.Count == 0)
        {
            return false;
        }

        _strip = new Strip(key, 0, folder, _pass, Task.CompletedTask, maxDistance)
        {
            Complete = true
        };
        _strip.Frames.AddRange(frames);
        _decoded = null;
        _decodedFile = null;
        return true;
    }

    private static bool StillCurrent(int pass, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        lock (Gate)
        {
            return _strip?.Pass == pass;
        }
    }

    private static StripKey? KeyFor(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0)
            {
                return null;
            }

            return new StripKey(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string FolderFor(StripKey key)
        => Path.Combine(Root(), $"{key.Length:x}-{key.WriteTicks:x}-{PathHash(key)}");

    private static string PathHash(StripKey key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Path.ToUpperInvariant())))[..12];

    private static string Root()
        => string.IsNullOrWhiteSpace(StoreOverride)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMediaPlayer", "timeline-cache")
            : StoreOverride;

    private static void RemoveOlder(string folder, string hash)
    {
        var root = Root();
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(dir, folder, StringComparison.OrdinalIgnoreCase)
                || !dir.EndsWith("-" + hash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool TryReadPts(string line, out double seconds)
    {
        seconds = 0;
        const string marker = "pts_time:";
        var at = line.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var start = at + marker.Length;
        var end = start;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.' || line[end] == ','))
        {
            end++;
        }

        if (end <= start)
        {
            return false;
        }

        var text = line.Substring(start, end - start).Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Strip
    {
        internal Strip(StripKey key, double durationSeconds, string folder, int pass, Task build, long maxDistance)
        {
            Key = key;
            DurationSeconds = durationSeconds;
            Folder = folder;
            Pass = pass;
            Build = build;
            MaxDistance = maxDistance;
        }

        internal StripKey Key { get; }

        internal double DurationSeconds { get; }

        internal string Folder { get; }

        internal int Pass { get; }

        internal Task Build { get; }

        internal long MaxDistance { get; }

        internal bool Complete { get; set; }

        internal List<SavedFrame> Frames { get; } = [];

        internal long[] Times => Frames.Select(frame => frame.Ms).ToArray();
    }

    private readonly record struct SavedFrame(long Ms, string Name);

    private readonly record struct ExactShot(long Ms, TimelinePreviewFrame.PreviewPixels Pixels);

    private readonly record struct ExactHit(long Distance, TimelinePreviewFrame.PreviewPixels Pixels);
}
