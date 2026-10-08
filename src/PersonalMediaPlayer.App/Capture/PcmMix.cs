namespace PersonalMediaPlayer.App.Capture;

internal static class PcmMix
{
    public static byte[] Convert(byte[] pcm, int fromChannels, int fromRate, int toChannels, int toRate)
    {
        if (pcm.Length == 0
            || fromChannels is < 1 or > 2
            || toChannels is < 1 or > 2
            || fromRate <= 0
            || toRate <= 0)
        {
            return [];
        }

        var mapped = fromChannels == toChannels ? pcm : MapChannels(pcm, fromChannels, toChannels);
        return fromRate == toRate ? mapped : Resample(mapped, toChannels, fromRate, toRate);
    }

    public static byte[] Add(byte[] primary, byte[] extra)
    {
        if (extra.Length == 0)
        {
            return primary;
        }

        var output = new byte[primary.Length];
        Buffer.BlockCopy(primary, 0, output, 0, primary.Length);
        var samples = Math.Min(primary.Length, extra.Length) / 2;
        for (var index = 0; index < samples; index++)
        {
            var mixed = Read(primary, index) + Read(extra, index);
            Write(output, index, (short)Math.Clamp(mixed, short.MinValue, short.MaxValue));
        }

        return output;
    }

    private static byte[] MapChannels(byte[] pcm, int fromChannels, int toChannels)
    {
        var frames = pcm.Length / (fromChannels * 2);
        var output = new byte[frames * toChannels * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            if (fromChannels == 1)
            {
                var sample = (short)Read(pcm, frame);
                Write(output, frame * 2, sample);
                Write(output, frame * 2 + 1, sample);
                continue;
            }

            var left = Read(pcm, frame * 2);
            var right = Read(pcm, frame * 2 + 1);
            Write(output, frame, (short)((left + right) / 2));
        }

        return output;
    }

    private static byte[] Resample(byte[] pcm, int channels, int fromRate, int toRate)
    {
        var frameBytes = channels * 2;
        var frames = pcm.Length / frameBytes;
        if (frames == 0)
        {
            return [];
        }

        var outFrames = (int)Math.Clamp(Math.Round(frames * (double)toRate / fromRate), 1d, int.MaxValue / frameBytes);
        var output = new byte[outFrames * frameBytes];
        var span = Math.Max(1, frames - 1);
        var outSpan = Math.Max(1, outFrames - 1);
        for (var frame = 0; frame < outFrames; frame++)
        {
            var position = outFrames == 1 ? 0d : frame * (double)span / outSpan;
            var index = (int)Math.Floor(position);
            var next = Math.Min(index + 1, frames - 1);
            var fraction = position - index;
            for (var channel = 0; channel < channels; channel++)
            {
                var start = Read(pcm, index * channels + channel);
                var end = Read(pcm, next * channels + channel);
                var sample = (int)Math.Round(start + ((end - start) * fraction));
                Write(output, frame * channels + channel, (short)Math.Clamp(sample, short.MinValue, short.MaxValue));
            }
        }

        return output;
    }

    private static int Read(byte[] pcm, int sample)
    {
        var offset = sample * 2;
        return (short)(pcm[offset] | (pcm[offset + 1] << 8));
    }

    private static void Write(byte[] pcm, int sample, short value)
    {
        var offset = sample * 2;
        pcm[offset] = (byte)value;
        pcm[offset + 1] = (byte)(value >> 8);
    }
}

internal sealed class PcmBuffer
{
    private byte[] _bytes = [];
    private int _count;

    public int Count => _count;

    public void Append(byte[] pcm)
    {
        if (pcm.Length == 0)
        {
            return;
        }

        var needed = _count + pcm.Length;
        if (_bytes.Length < needed)
        {
            var grown = new byte[Math.Max(needed, Math.Max(4096, _bytes.Length * 2))];
            if (_count > 0)
            {
                Buffer.BlockCopy(_bytes, 0, grown, 0, _count);
            }

            _bytes = grown;
        }

        Buffer.BlockCopy(pcm, 0, _bytes, _count, pcm.Length);
        _count += pcm.Length;
    }

    public byte[] Take(int bytes)
    {
        if (bytes <= 0)
        {
            return [];
        }

        var result = new byte[bytes];
        var taken = Math.Min(bytes, _count);
        if (taken > 0)
        {
            Buffer.BlockCopy(_bytes, 0, result, 0, taken);
            _count -= taken;
            if (_count > 0)
            {
                Buffer.BlockCopy(_bytes, taken, _bytes, 0, _count);
            }
        }

        return result;
    }

    public void TrimTo(int maxBytes, int frameBytes)
    {
        if (frameBytes <= 0 || _count <= maxBytes)
        {
            return;
        }

        var drop = _count - maxBytes;
        drop -= drop % frameBytes;
        if (drop <= 0)
        {
            return;
        }

        _count -= drop;
        Buffer.BlockCopy(_bytes, drop, _bytes, 0, _count);
    }
}
