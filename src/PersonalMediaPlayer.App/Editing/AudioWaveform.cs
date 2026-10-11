using System.Diagnostics;

namespace PersonalMediaPlayer.App.Editing;

// Peaks are the loudest moment in each slice of the file, scaled so the
// loudest slice is 1. A later save changes the file length or write time,
// so the next read is that edit and not the previous sound.
internal static class AudioWaveform
{
    internal const int DecodeRate = 8000;

    internal const int PeaksPerSecond = 80;

    internal const int MaxPeaks = 240_000;

    internal readonly record struct WaveformKey(string Path, long Length, long WriteTicks)
    {
        internal static WaveformKey For(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return new WaveformKey(path, -1, 0);
                }

                return new WaveformKey(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
            }
            catch (IOException)
            {
                return new WaveformKey(path, -1, 0);
            }
            catch (UnauthorizedAccessException)
            {
                return new WaveformKey(path, -1, 0);
            }
        }
    }

    internal readonly record struct Levels(float[] Peaks, double DurationSeconds)
    {
        public bool HasPeaks => Peaks is { Length: > 0 };

        public static Levels Empty { get; } = new([], 0);
    }

    internal static Levels FromSamples(ReadOnlySpan<float> samples, int sampleRate, int peaksPerSecond, int maxPeaks)
    {
        var builder = new Builder(sampleRate, peaksPerSecond, maxPeaks);
        foreach (var sample in samples)
        {
            builder.Add(sample);
        }

        return builder.Finish();
    }

    // Bars cover startFraction..endFraction of the timeline. Each bar keeps the
    // loudest peak in its slice, so zooming and scrolling stay on the file.
    internal static float[] Window(Levels levels, double timelineSeconds, double startFraction, double endFraction, int bars)
    {
        if (bars < 1)
        {
            return [];
        }

        var result = new float[bars];
        if (!levels.HasPeaks)
        {
            return result;
        }

        var timeline = timelineSeconds > 0 ? timelineSeconds : levels.DurationSeconds;
        var audio = levels.DurationSeconds;
        if (timeline <= 0 || audio <= 0)
        {
            return result;
        }

        var start = Math.Clamp(Math.Min(startFraction, endFraction), 0, 1);
        var end = Math.Clamp(Math.Max(startFraction, endFraction), 0, 1);
        if (end <= start)
        {
            end = Math.Min(1, start + 1d / bars);
        }

        var count = levels.Peaks.Length;
        for (var bar = 0; bar < bars; bar++)
        {
            var from = (start + (end - start) * bar / bars) * timeline;
            var to = (start + (end - start) * (bar + 1) / bars) * timeline;
            if (to <= 0 || from >= audio)
            {
                continue;
            }

            var first = (int)Math.Floor(Math.Max(0, from) / audio * count);
            var last = (int)Math.Ceiling(Math.Min(audio, to) / audio * count);
            first = Math.Clamp(first, 0, count - 1);
            last = Math.Clamp(Math.Max(last, first + 1), 0, count);
            var peak = 0f;
            for (var index = first; index < last; index++)
            {
                if (levels.Peaks[index] > peak)
                {
                    peak = levels.Peaks[index];
                }
            }

            result[bar] = peak;
        }

        return result;
    }

    internal static void Paint(ReadOnlySpan<float> bars, Span<byte> bgra, int width, int height, byte blue, byte green, byte red, byte alpha)
    {
        if (width < 1 || height < 1 || bgra.Length < width * height * 4)
        {
            return;
        }

        bgra.Clear();
        if (bars.IsEmpty)
        {
            return;
        }

        var usable = Math.Max(1, height - 2);
        for (var bar = 0; bar < bars.Length; bar++)
        {
            var left = bar * width / bars.Length;
            var right = (bar + 1) * width / bars.Length;
            if (right <= left)
            {
                right = Math.Min(width, left + 1);
            }

            if (right - left > 2)
            {
                right--;
            }

            var level = Math.Clamp(bars[bar], 0, 1);
            var mark = Math.Max(1, (int)Math.Round(level * usable));
            var top = (height - mark) / 2;
            for (var x = left; x < right; x++)
            {
                for (var y = top; y < top + mark; y++)
                {
                    var pixel = (y * width + x) * 4;
                    bgra[pixel] = blue;
                    bgra[pixel + 1] = green;
                    bgra[pixel + 2] = red;
                    bgra[pixel + 3] = alpha;
                }
            }
        }
    }

    internal static bool TryGet(WaveformKey key, out Levels levels)
    {
        lock (CacheLock)
        {
            return Cache.TryGetValue(key, out levels);
        }
    }

    internal static void Remember(WaveformKey key, Levels levels)
    {
        if (key.Length < 0)
        {
            return;
        }

        lock (CacheLock)
        {
            if (!Cache.ContainsKey(key))
            {
                Order.Enqueue(key);
            }

            Cache[key] = levels;
            while (Order.Count > CacheLimit && Order.TryDequeue(out var old))
            {
                if (!old.Equals(key))
                {
                    Cache.Remove(old);
                }
            }
        }
    }

    internal static async Task<Levels?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var key = WaveformKey.For(path);
        if (key.Length < 0)
        {
            return Levels.Empty;
        }

        if (TryGet(key, out var cached))
        {
            return cached;
        }

        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            return null;
        }

        await ReadGate.WaitAsync(cancellationToken);
        try
        {
            if (TryGet(key, out cached))
            {
                return cached;
            }

            var levels = await DecodeAsync(ffmpeg, path, cancellationToken);
            if (levels is null || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (WaveformKey.For(path).Equals(key))
            {
                Remember(key, levels.Value);
            }

            return levels;
        }
        finally
        {
            ReadGate.Release();
        }
    }

    private static async Task<Levels?> DecodeAsync(string ffmpeg, string path, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-loglevel");
        start.ArgumentList.Add("error");
        start.ArgumentList.Add("-vn");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(path);
        start.ArgumentList.Add("-map");
        start.ArgumentList.Add("0:a:0");
        start.ArgumentList.Add("-ac");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add("-ar");
        start.ArgumentList.Add(DecodeRate.ToString());
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("f32le");
        start.ArgumentList.Add("pipe:1");
        using var process = Process.Start(start);
        if (process is null)
        {
            return null;
        }

        try
        {
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            var levels = await ReadPeaksAsync(process.StandardOutput.BaseStream, cancellationToken);
            var error = await errors;
            await process.WaitForExitAsync(cancellationToken);
            if (!levels.HasPeaks && process.ExitCode != 0 && !NoAudio(error))
            {
                return null;
            }

            return levels;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        finally
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
                // The process already stopped.
            }
        }
    }

    private static async Task<Levels> ReadPeaksAsync(Stream stream, CancellationToken cancellationToken)
    {
        var builder = new Builder(DecodeRate, PeaksPerSecond, MaxPeaks);
        var buffer = new byte[16384];
        var carry = new byte[4];
        var carryCount = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            var offset = 0;
            if (carryCount > 0)
            {
                var need = 4 - carryCount;
                if (read < need)
                {
                    Buffer.BlockCopy(buffer, 0, carry, carryCount, read);
                    carryCount += read;
                    continue;
                }

                Buffer.BlockCopy(buffer, 0, carry, carryCount, need);
                builder.Add(BitConverter.ToSingle(carry, 0));
                offset = need;
                carryCount = 0;
            }

            var end = offset + (read - offset) / 4 * 4;
            for (var index = offset; index < end; index += 4)
            {
                builder.Add(BitConverter.ToSingle(buffer, index));
            }

            carryCount = read - end;
            if (carryCount > 0)
            {
                Buffer.BlockCopy(buffer, end, carry, 0, carryCount);
            }
        }

        return builder.Finish();
    }

    private static bool NoAudio(string? error)
        => error is not null
            && (error.Contains("matches no streams", StringComparison.OrdinalIgnoreCase)
                || error.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase));

    private static string? FindFfmpeg()
    {
        var bundled = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalMediaPlayer",
            "tools",
            "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class Builder
    {
        private readonly List<float> _peaks = [];
        private readonly int _sampleRate;
        private readonly int _maxPeaks;
        private float _current;
        private int _filled;
        private int _samplesPerPeak;
        private int _totalSamples;

        public Builder(int sampleRate, int peaksPerSecond, int maxPeaks)
        {
            _sampleRate = Math.Max(1, sampleRate);
            _maxPeaks = Math.Max(2, maxPeaks);
            var perSecond = Math.Max(1, peaksPerSecond);
            _samplesPerPeak = Math.Max(1, _sampleRate / perSecond);
        }

        public void Add(float sample)
        {
            _totalSamples++;
            var level = Math.Abs(sample);
            if (level > _current)
            {
                _current = level;
            }

            _filled++;
            if (_filled < _samplesPerPeak)
            {
                return;
            }

            _peaks.Add(_current);
            _current = 0;
            _filled = 0;
            if (_peaks.Count >= _maxPeaks)
            {
                Compact();
            }
        }

        public Levels Finish()
        {
            if (_filled > 0)
            {
                _peaks.Add(_current);
            }

            var peaks = _peaks.ToArray();
            var max = 0f;
            foreach (var peak in peaks)
            {
                if (peak > max)
                {
                    max = peak;
                }
            }

            if (max >= 0.0001f)
            {
                for (var index = 0; index < peaks.Length; index++)
                {
                    peaks[index] /= max;
                }
            }
            else
            {
                Array.Clear(peaks);
            }

            return new Levels(peaks, _totalSamples / (double)_sampleRate);
        }

        private void Compact()
        {
            var target = 0;
            for (var index = 0; index < _peaks.Count; index += 2)
            {
                var peak = _peaks[index];
                if (index + 1 < _peaks.Count && _peaks[index + 1] > peak)
                {
                    peak = _peaks[index + 1];
                }

                _peaks[target++] = peak;
            }

            if (target < _peaks.Count)
            {
                _peaks.RemoveRange(target, _peaks.Count - target);
            }

            _samplesPerPeak *= 2;
        }
    }

    private const int CacheLimit = 32;

    private static readonly object CacheLock = new();

    private static readonly Dictionary<WaveformKey, Levels> Cache = [];

    private static readonly Queue<WaveformKey> Order = new();

    private static readonly SemaphoreSlim ReadGate = new(2);
}
