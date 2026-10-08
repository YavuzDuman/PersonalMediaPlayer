namespace PersonalMediaPlayer.App.Capture;

internal readonly record struct MixedAudioChunk(byte[] Pcm, TimeSpan Stamp, TimeSpan Duration);

/// <summary>
/// Mixes speaker and microphone bytes onto the recording clock.
/// Speaker loopback produces no packets while nothing is playing, so the microphone
/// has to be written as recording time passes.
/// </summary>
internal sealed class MixedAudioClock
{
    public const int KeepSeconds = 5;

    private readonly PcmBuffer _speaker = new();
    private readonly PcmBuffer _microphone = new();
    private readonly int _bytesPerSecond;
    private readonly int _blockAlign;
    private readonly TimeSpan _idleGap;
    private TimeSpan _written;

    public MixedAudioClock(int bytesPerSecond, int blockAlign, TimeSpan idleGap)
    {
        _bytesPerSecond = bytesPerSecond;
        _blockAlign = Math.Max(2, blockAlign);
        _idleGap = idleGap;
    }

    public TimeSpan Written => _written;

    public void AppendSpeaker(byte[] pcm) => Append(_speaker, pcm);

    public void AppendMicrophone(byte[] pcm) => Append(_microphone, pcm);

    public List<MixedAudioChunk> CatchUp(TimeSpan elapsed, bool flushShort = false)
    {
        var chunks = new List<MixedAudioChunk>();
        if (_bytesPerSecond <= 0)
        {
            return chunks;
        }

        var gap = elapsed - _written;
        var minimum = flushShort ? TimeSpan.FromTicks(1) : _idleGap;
        if (gap < minimum)
        {
            return chunks;
        }

        var totalBytes = gap.Ticks * _bytesPerSecond / TimeSpan.TicksPerSecond;
        totalBytes -= totalBytes % _blockAlign;
        if (totalBytes <= 0)
        {
            return chunks;
        }

        var chunkBytes = _bytesPerSecond / 10;
        chunkBytes -= chunkBytes % _blockAlign;
        if (chunkBytes <= 0)
        {
            chunkBytes = _blockAlign;
        }

        while (totalBytes > 0)
        {
            var size = (int)Math.Min(chunkBytes, totalBytes);
            size -= size % _blockAlign;
            if (size <= 0)
            {
                break;
            }

            var duration = TimeSpan.FromTicks(size * (long)TimeSpan.TicksPerSecond / _bytesPerSecond);
            var mixed = PcmMix.Add(_speaker.Take(size), _microphone.Take(size));
            chunks.Add(new MixedAudioChunk(mixed, _written, duration));
            _written += duration;
            totalBytes -= size;
        }

        return chunks;
    }

    private void Append(PcmBuffer buffer, byte[] pcm)
    {
        buffer.Append(pcm);
        buffer.TrimTo(_bytesPerSecond * KeepSeconds, _blockAlign);
    }
}
