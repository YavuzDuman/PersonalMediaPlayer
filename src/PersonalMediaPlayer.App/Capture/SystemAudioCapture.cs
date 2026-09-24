using System.Runtime.InteropServices;

namespace PersonalMediaPlayer.App.Capture;

internal sealed class SystemAudioCapture : IDisposable
{
    private const int RenderDevice = 0;
    private const int ConsoleRole = 0;
    private const int SharedMode = 0;
    private const uint LoopbackFlag = 0x00020000;
    private const uint SilentFlag = 0x00000002;
    private const uint ClassContextAll = 23;
    private const ushort WaveFormatPcm = 1;
    private const ushort WaveFormatFloat = 3;
    private const ushort WaveFormatExtensible = 0xFFFE;

    private static readonly Guid CaptureClientId = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly ManualResetEventSlim _ready = new(false);
    private Action<byte[]>? _onPacket;
    private Thread? _thread;
    private volatile bool _stop;
    private Exception? _startError;
    private bool _disposed;

    public int SampleRate { get; private set; }

    public int ChannelCount { get; private set; }

    public int BytesPerSecond => SampleRate * ChannelCount * 2;

    public void Start(Action<byte[]> onPacket)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _onPacket = onPacket;
        _thread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "System audio"
        };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            _stop = true;
            throw new InvalidOperationException("System audio did not start. Check that a speaker is available.");
        }

        if (_startError is InvalidOperationException invalid)
        {
            throw invalid;
        }

        if (_startError is not null)
        {
            throw new InvalidOperationException("System audio did not start. Check that a speaker is available.", _startError);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop = true;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void CaptureLoop()
    {
        var initialized = CoInitializeEx(IntPtr.Zero, 0) >= 0;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        var format = IntPtr.Zero;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var hr = enumerator.GetDefaultAudioEndpoint(RenderDevice, ConsoleRole, out device);
            if (hr < 0 || device is null)
            {
                throw new InvalidOperationException("No speaker is available for system audio.");
            }

            var clientId = typeof(IAudioClient).GUID;
            hr = device.Activate(ref clientId, ClassContextAll, IntPtr.Zero, out client);
            if (hr < 0 || client is null)
            {
                throw new InvalidOperationException("System audio could not open the speaker.");
            }

            hr = client.GetMixFormat(out format);
            if (hr < 0 || format == IntPtr.Zero)
            {
                throw new InvalidOperationException("System audio could not read the speaker format.");
            }

            DescribeFormat(format);
            hr = client.Initialize(SharedMode, LoopbackFlag, 1_000_0000, 0, format, IntPtr.Zero);
            if (hr < 0)
            {
                throw new InvalidOperationException("System audio could not listen to the speaker.");
            }

            var captureId = CaptureClientId;
            hr = client.GetService(ref captureId, out capture);
            if (hr < 0 || capture is null)
            {
                throw new InvalidOperationException("System audio could not listen to the speaker.");
            }

            hr = client.Start();
            if (hr < 0)
            {
                throw new InvalidOperationException("System audio could not listen to the speaker.");
            }

            _ready.Set();
            while (!_stop)
            {
                Pump(capture);
                Thread.Sleep(5);
            }

            client.Stop();
        }
        catch (Exception ex)
        {
            _startError = ex;
            _ready.Set();
        }
        finally
        {
            if (format != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(format);
            }

            if (capture is not null)
            {
                Marshal.ReleaseComObject(capture);
            }

            if (client is not null)
            {
                Marshal.ReleaseComObject(client);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }

            if (initialized)
            {
                CoUninitialize();
            }
        }
    }

    private void DescribeFormat(IntPtr format)
    {
        var tag = (ushort)Marshal.ReadInt16(format, 0);
        var channels = (ushort)Marshal.ReadInt16(format, 2);
        var sampleRate = (uint)Marshal.ReadInt32(format, 4);
        var bits = (ushort)Marshal.ReadInt16(format, 14);
        var extra = (ushort)Marshal.ReadInt16(format, 16);
        Guid subFormat = Guid.Empty;
        if (tag == WaveFormatExtensible && extra >= 22)
        {
            subFormat = Marshal.PtrToStructure<Guid>(format + 24);
        }

        var isFloat = tag == WaveFormatFloat || subFormat == FloatSubFormat;
        var isPcm = tag == WaveFormatPcm || subFormat == PcmSubFormat;
        if (channels == 0 || sampleRate is < 8000 or > 192000 || (!isFloat && !isPcm) || bits is not (16 or 32))
        {
            throw new InvalidOperationException("This speaker format cannot be recorded.");
        }

        SampleRate = (int)sampleRate;
        ChannelCount = Math.Min(channels, (ushort)2);
        _sourceBits = bits;
        _sourceBlockAlign = channels * (bits / 8);
        _float = isFloat;
    }

    private int _sourceBits;
    private int _sourceBlockAlign;
    private bool _float;

    private void Pump(IAudioCaptureClient capture)
    {
        while (!_stop)
        {
            var hr = capture.GetNextPacketSize(out var packetFrames);
            if (hr < 0 || packetFrames == 0)
            {
                return;
            }

            hr = capture.GetBuffer(out var data, out var frames, out var flags, out _, out _);
            if (hr < 0)
            {
                return;
            }

            try
            {
                if (frames == 0)
                {
                    continue;
                }

                var pcm = (flags & SilentFlag) != 0 || data == IntPtr.Zero
                    ? new byte[frames * ChannelCount * 2]
                    : Convert(data, (int)frames);
                _onPacket?.Invoke(pcm);
            }
            finally
            {
                capture.ReleaseBuffer(frames);
            }
        }
    }

    private byte[] Convert(IntPtr source, int frames)
    {
        var pcm = new byte[frames * ChannelCount * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            var frameOffset = frame * _sourceBlockAlign;
            for (var channel = 0; channel < ChannelCount; channel++)
            {
                var sample = ReadSample(source, frameOffset + (channel * (_sourceBits / 8)));
                var dest = (frame * ChannelCount + channel) * 2;
                pcm[dest] = (byte)sample;
                pcm[dest + 1] = (byte)(sample >> 8);
            }
        }

        return pcm;
    }

    private short ReadSample(IntPtr source, int offset)
    {
        if (_float)
        {
            var value = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(source, offset));
            var scaled = Math.Clamp(value, -1f, 1f) * 32767f;
            return (short)Math.Clamp((int)Math.Round(scaled), short.MinValue, short.MaxValue);
        }

        if (_sourceBits == 32)
        {
            return (short)(Marshal.ReadInt32(source, offset) >> 16);
        }

        return (short)Marshal.ReadInt16(source, offset);
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint classContext, IntPtr activationParams, out IAudioClient client);

        [PreserveSig]
        int OpenPropertyStore(uint access, out IntPtr store);

        [PreserveSig]
        int GetId(out IntPtr id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]
        int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);

        [PreserveSig]
        int GetBufferSize(out uint bufferFrames);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out uint padding);

        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);

        [PreserveSig]
        int GetMixFormat(out IntPtr deviceFormat);

        [PreserveSig]
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(IntPtr handle);

        [PreserveSig]
        int GetService(ref Guid iid, out IAudioCaptureClient capture);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out IntPtr data, out uint numFrames, out uint flags, out ulong devicePosition, out ulong qpcPosition);

        [PreserveSig]
        int ReleaseBuffer(uint numFrames);

        [PreserveSig]
        int GetNextPacketSize(out uint numFrames);
    }
}
