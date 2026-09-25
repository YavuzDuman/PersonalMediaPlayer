using System.Runtime.InteropServices;

namespace PersonalMediaPlayer.App.Editing;

internal static class VideoSpeedEncoder
{
    public static void Write(string sourcePath, string destinationPath, double rate, double volume)
    {
        if (rate < 0.25 || rate > 4)
        {
            throw new InvalidOperationException("Choose a speed from 0.25x to 4x.");
        }

        MediaFoundation.Startup();
        IMFSourceReader? reader = null;
        IMFSinkWriter? writer = null;
        try
        {
            MediaFoundation.CreateReader(sourcePath, out reader);
            var video = VideoStream.Open(reader);
            var audio = AudioStream.TryOpen(reader);
            MediaFoundation.CreateWriter(destinationPath, out writer);
            video.AddTo(writer, rate);
            audio?.AddTo(writer);
            writer.BeginWriting();
            CopyStreams(reader, writer, video, audio, rate, volume);
            writer.FinishWriting();
        }
        finally
        {
            if (writer is not null)
            {
                Marshal.ReleaseComObject(writer);
            }

            if (reader is not null)
            {
                Marshal.ReleaseComObject(reader);
            }
        }
    }

    private static void CopyStreams(IMFSourceReader reader, IMFSinkWriter writer, VideoStream video, AudioStream? audio, double rate, double volume)
    {
        var videoDone = false;
        var audioDone = audio is null;
        while (!videoDone || !audioDone)
        {
            reader.ReadSample(MediaFoundation.AnyStream, 0, out var actual, out var flags, out _, out var samplePointer);
            if ((flags & MediaFoundation.ReadError) != 0)
            {
                throw new InvalidOperationException("The video could not be read.");
            }

            if ((flags & MediaFoundation.EndOfStream) != 0)
            {
                if (actual == video.ReaderIndex)
                {
                    videoDone = true;
                }

                if (audio is not null && actual == audio.ReaderIndex)
                {
                    audioDone = true;
                }
            }

            if (samplePointer == IntPtr.Zero)
            {
                continue;
            }

            var sample = (IMFSample)Marshal.GetObjectForIUnknown(samplePointer);
            try
            {
                if (actual == video.ReaderIndex)
                {
                    ScaleTime(sample, rate);
                    writer.WriteSample(video.WriterIndex, sample);
                }
                else if (audio is not null && actual == audio.ReaderIndex)
                {
                    var sped = audio.Resample(sample, rate, volume);
                    try
                    {
                        writer.WriteSample(audio.WriterIndex, sped);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sped);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
                Marshal.Release(samplePointer);
            }
        }
    }

    private static int StreamIndex(IMFSourceReader reader, Guid major)
    {
        for (var index = 0; index < 16; index++)
        {
            IMFMediaType? type = null;
            try
            {
                type = reader.GetCurrentMediaType(index);
                type.GetGUID(MfGuids.MajorType, out var found);
                if (found == major)
                {
                    return index;
                }
            }
            catch (COMException)
            {
                break;
            }
            finally
            {
                if (type is not null)
                {
                    Marshal.ReleaseComObject(type);
                }
            }
        }

        throw new InvalidOperationException("The video streams could not be read.");
    }

    private static void ScaleTime(IMFSample sample, double rate)
    {
        sample.GetSampleTime(out var time);
        sample.SetSampleTime((long)(time / rate));
        try
        {
            sample.GetSampleDuration(out var duration);
            sample.SetSampleDuration(Math.Max(1, (long)(duration / rate)));
        }
        catch (COMException)
        {
            // Some decoded frames omit a duration. The next timestamp still places them.
        }
    }

    private sealed class VideoStream
    {
        public int ReaderIndex { get; private init; }

        public int WriterIndex { get; private set; }

        private IMFMediaType _input = null!;

        private uint _width;

        private uint _height;

        private long _frameRate = Pack(1, 30);

        private int _bitrate = 4_000_000;

        public static VideoStream Open(IMFSourceReader reader)
        {
            reader.SetStreamSelection(MediaFoundation.AllStreams, false);
            reader.SetStreamSelection(MediaFoundation.FirstVideo, true);
            reader.SetStreamSelection(MediaFoundation.FirstAudio, true);
            var native = reader.GetNativeMediaType(MediaFoundation.FirstVideo, 0);
            try
            {
                native.GetUINT64(MfGuids.FrameSize, out var size);
                var width = (uint)((ulong)size >> 32);
                var height = (uint)(size & 0xffffffff);
                var evenWidth = width - (width % 2);
                var evenHeight = height - (height % 2);
                if (evenWidth < 2 || evenHeight < 2)
                {
                    throw new InvalidOperationException("This video has no picture to save.");
                }

                var desired = MediaFoundation.CreateType(MfGuids.Video, MfGuids.Nv12);
                try
                {
                    native.CopyAllItems(desired);
                    desired.SetGUID(MfGuids.Subtype, MfGuids.Nv12);
                    if (evenWidth != width || evenHeight != height)
                    {
                        desired.SetUINT64(MfGuids.FrameSize, Pack(evenWidth, evenHeight));
                    }

                    reader.SetCurrentMediaType(MediaFoundation.FirstVideo, IntPtr.Zero, desired);
                }
                finally
                {
                    Marshal.ReleaseComObject(desired);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(native);
            }

            var current = reader.GetCurrentMediaType(MediaFoundation.FirstVideo);
            current.GetUINT64(MfGuids.FrameSize, out var decodedSize);
            var decodedWidth = (uint)((ulong)decodedSize >> 32);
            var decodedHeight = (uint)(decodedSize & 0xffffffff);

            long frameRate = Pack(30, 1);
            try
            {
                current.GetUINT64(MfGuids.FrameRate, out frameRate);
            }
            catch (COMException)
            {
                // 30 fps is used when the file does not say.
            }

            var bitrate = (int)Math.Clamp((long)decodedWidth * decodedHeight * 4, 1_000_000, 20_000_000);
            try
            {
                var sourceType = reader.GetNativeMediaType(MediaFoundation.FirstVideo, 0);
                try
                {
                    sourceType.GetUINT32(MfGuids.Bitrate, out var nativeBitrate);
                    if (nativeBitrate > 0)
                    {
                        bitrate = (int)Math.Clamp(nativeBitrate, 1_000_000, 20_000_000);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(sourceType);
                }
            }
            catch (COMException)
            {
                // The estimated bitrate stays.
            }

            return new VideoStream
            {
                ReaderIndex = StreamIndex(reader, MfGuids.Video),
                _input = current,
                _width = decodedWidth,
                _height = decodedHeight,
                _frameRate = frameRate,
                _bitrate = bitrate
            };
        }

        public void AddTo(IMFSinkWriter writer, double rate)
        {
            var output = MediaFoundation.CreateType(MfGuids.Video, MfGuids.H264);
            try
            {
                var numerator = (uint)((ulong)_frameRate >> 32);
                var denominator = (uint)(_frameRate & 0xffffffff);
                if (numerator == 0)
                {
                    numerator = 30;
                }

                if (denominator == 0)
                {
                    denominator = 1;
                }

                numerator = (uint)Math.Max(1, Math.Round(numerator * rate));
                output.SetUINT64(MfGuids.FrameSize, Pack(_width, _height));
                output.SetUINT64(MfGuids.FrameRate, Pack(numerator, denominator));
                output.SetUINT64(MfGuids.PixelAspect, Pack(1, 1));
                output.SetUINT32(MfGuids.Interlace, 2);
                output.SetUINT32(MfGuids.Bitrate, _bitrate);
                writer.AddStream(output, out var index);
                WriterIndex = index;
            }
            finally
            {
                Marshal.ReleaseComObject(output);
            }

            writer.SetInputMediaType(WriterIndex, _input, null);
        }
    }

    private sealed class AudioStream
    {
        public int ReaderIndex { get; private init; }

        public int WriterIndex { get; private set; }

        private int _channels;

        private int _sampleRate;

        private int _outputRate;

        public static AudioStream? TryOpen(IMFSourceReader reader)
        {
            try
            {
                var native = reader.GetNativeMediaType(MediaFoundation.FirstAudio, 0);
                var desired = MediaFoundation.CreateType(MfGuids.Audio, MfGuids.Pcm);
                try
                {
                    native.CopyAllItems(desired);
                    desired.SetGUID(MfGuids.Subtype, MfGuids.Pcm);
                    desired.SetUINT32(MfGuids.BitsPerSample, 16);
                    reader.SetCurrentMediaType(MediaFoundation.FirstAudio, IntPtr.Zero, desired);
                }
                finally
                {
                    Marshal.ReleaseComObject(desired);
                    Marshal.ReleaseComObject(native);
                }

                var current = reader.GetCurrentMediaType(MediaFoundation.FirstAudio);
                current.GetUINT32(MfGuids.Channels, out var channels);
                current.GetUINT32(MfGuids.SampleRate, out var sampleRate);
                Marshal.ReleaseComObject(current);
                if (channels is not (1 or 2 or 6) || sampleRate < 8000)
                {
                    reader.SetStreamSelection(MediaFoundation.FirstAudio, false);
                    return null;
                }

                return new AudioStream
                {
                    ReaderIndex = StreamIndex(reader, MfGuids.Audio),
                    _channels = channels,
                    _sampleRate = sampleRate,
                    _outputRate = AacRate(sampleRate)
                };
            }
            catch (COMException)
            {
                try
                {
                    reader.SetStreamSelection(MediaFoundation.FirstAudio, false);
                }
                catch (COMException)
                {
                    // The file has no audio stream to turn off.
                }

                return null;
            }
        }

        public void AddTo(IMFSinkWriter writer)
        {
            var output = MediaFoundation.CreateType(MfGuids.Audio, MfGuids.Aac);
            var input = MediaFoundation.CreateType(MfGuids.Audio, MfGuids.Pcm);
            try
            {
                const int aacBytesPerSecond = 20_000;
                output.SetUINT32(MfGuids.Channels, _channels);
                output.SetUINT32(MfGuids.SampleRate, _outputRate);
                output.SetUINT32(MfGuids.BitsPerSample, 16);
                output.SetUINT32(MfGuids.BytesPerSecond, aacBytesPerSecond);
                output.SetUINT32(MfGuids.AacPayload, 0);
                output.SetUINT32(MfGuids.AacProfile, 0x29);
                input.SetUINT32(MfGuids.Channels, _channels);
                input.SetUINT32(MfGuids.SampleRate, _outputRate);
                input.SetUINT32(MfGuids.BitsPerSample, 16);
                input.SetUINT32(MfGuids.BlockAlign, _channels * 2);
                input.SetUINT32(MfGuids.BytesPerSecond, _outputRate * _channels * 2);
                writer.AddStream(output, out var index);
                WriterIndex = index;
                writer.SetInputMediaType(index, input, null);
            }
            finally
            {
                Marshal.ReleaseComObject(output);
                Marshal.ReleaseComObject(input);
            }
        }

        public IMFSample Resample(IMFSample source, double rate, double volume)
        {
            source.ConvertToContiguousBuffer(out var buffer);
            try
            {
                buffer.Lock(out var pointer, out _, out var length);
                byte[] bytes;
                try
                {
                    bytes = new byte[length];
                    Marshal.Copy(pointer, bytes, 0, length);
                }
                finally
                {
                    buffer.Unlock();
                }

                var sped = ResamplePcm16(bytes, _channels, _sampleRate, _outputRate, rate, volume);
                source.GetSampleTime(out var time);
                var sample = MediaFoundation.CreateSample(sped, (long)(time / rate), sped.Length / (_channels * 2) * 10_000_000L / _outputRate);
                return sample;
            }
            finally
            {
                Marshal.ReleaseComObject(buffer);
            }
        }

        private static byte[] ResamplePcm16(byte[] input, int channels, int sourceRate, int outputRate, double speed, double volume)
        {
            var frame = channels * 2;
            var frames = input.Length / frame;
            if (frames < 1)
            {
                return input;
            }

            var step = sourceRate / (double)outputRate * speed;
            var outputFrames = Math.Max(1, (int)Math.Round(frames / Math.Max(step, 0.001)));
            var output = new byte[outputFrames * frame];
            for (var i = 0; i < outputFrames; i++)
            {
                var position = Math.Min(frames - 1, i * step);
                var left = (int)position;
                var right = Math.Min(left + 1, frames - 1);
                var mix = position - left;
                for (var channel = 0; channel < channels; channel++)
                {
                    var a = BitConverter.ToInt16(input, (left * frame) + (channel * 2));
                    var b = BitConverter.ToInt16(input, (right * frame) + (channel * 2));
                    var mixed = a + ((b - a) * mix);
                    var scaled = (int)Math.Round(mixed * volume);
                    BitConverter.TryWriteBytes(output.AsSpan((i * frame) + (channel * 2)), (short)Math.Clamp(scaled, short.MinValue, short.MaxValue));
                }
            }

            return output;
        }

        private static int AacRate(int sampleRate)
        {
            ReadOnlySpan<int> supported = [8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000];
            var best = supported[0];
            var distance = int.MaxValue;
            foreach (var rate in supported)
            {
                var gap = Math.Abs(rate - sampleRate);
                if (gap < distance)
                {
                    best = rate;
                    distance = gap;
                }
            }

            return best;
        }
    }

    private static long Pack(uint high, uint low) => ((long)high << 32) | low;
}

internal static class MfGuids
{
    public static readonly Guid Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid Audio = new("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid H264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid Nv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    public static readonly Guid Aac = new("00001610-0000-0010-8000-00AA00389B71");
    public static readonly Guid Pcm = new("00000001-0000-0010-8000-00AA00389B71");
    public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid Bitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid Interlace = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid PixelAspect = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid Channels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    public static readonly Guid SampleRate = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid BitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    public static readonly Guid BlockAlign = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static readonly Guid BytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    public static readonly Guid AacPayload = new("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
    public static readonly Guid AacProfile = new("7632f0e6-9538-4d61-acda-ea29c8c14456");
    public static readonly Guid EnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
}

internal static class MediaFoundation
{
    public const int FirstVideo = unchecked((int)0xFFFFFFFC);
    public const int FirstAudio = unchecked((int)0xFFFFFFFD);
    public const int AnyStream = unchecked((int)0xFFFFFFFE);
    public const int AllStreams = unchecked((int)0xFFFFFFFE);
    public const int EndOfStream = 0x00000002;
    public const int ReadError = 0x00000001;

    public static void Startup() => MfImports.MFStartup(0x00020070, 0);

    public static void CreateReader(string path, out IMFSourceReader reader)
    {
        var attributes = CreateAttributes();
        try
        {
            attributes.SetUINT32(MfGuids.EnableVideoProcessing, 1);
            MfImports.MFCreateSourceReaderFromURL(path, attributes, out reader);
        }
        finally
        {
            Marshal.ReleaseComObject(attributes);
        }
    }

    public static void CreateWriter(string path, out IMFSinkWriter writer)
        => MfImports.MFCreateSinkWriterFromURL(path, null, null, out writer);

    public static IMFMediaType CreateType(Guid major, Guid subtype)
    {
        MfImports.MFCreateMediaType(out var type);
        type.SetGUID(MfGuids.MajorType, major);
        type.SetGUID(MfGuids.Subtype, subtype);
        return type;
    }

    public static IMFAttributes CreateAttributes()
    {
        MfImports.MFCreateAttributes(out var attributes, 4);
        return attributes;
    }

    public static IMFSample CreateSample(byte[] data, long time, long duration)
    {
        MfImports.MFCreateSample(out var sample);
        MfImports.MFCreateMemoryBuffer(data.Length, out var buffer);
        try
        {
            buffer.Lock(out var pointer, out _, out _);
            try
            {
                Marshal.Copy(data, 0, pointer, data.Length);
            }
            finally
            {
                buffer.Unlock();
            }

            buffer.SetCurrentLength(data.Length);
            sample.AddBuffer(buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(Math.Max(1, duration));
            return sample;
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }
    }
}

internal static class MfImports
{
    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateMediaType(out IMFMediaType type);

    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateAttributes(out IMFAttributes attributes, int initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateSample(out IMFSample sample);

    [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IMFAttributes attributes, out IMFSourceReader reader);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.IUnknown)] object? stream, IMFAttributes? attributes, out IMFSinkWriter writer);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
internal interface IMFAttributes
{
    void GetItem([MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
    void GetItemType([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int type);
    void CompareItem([MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
    void Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
    void GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);
    void GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, out long value);
    void GetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid key, out double value);
    void GetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, out Guid value);
    void GetStringLength([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int length);
    void GetString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int bufferSize, out int length);
    void GetAllocatedString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out int length);
    void GetBlobSize([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int size);
    void GetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPArray)] byte[] buffer, int bufferSize, out int size);
    void GetAllocatedBlob([MarshalAs(UnmanagedType.LPStruct)] Guid key, out IntPtr buffer, out int size);
    void GetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    void SetItem([MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
    void DeleteItem([MarshalAs(UnmanagedType.LPStruct)] Guid key);
    void DeleteAllItems();
    void SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
    void SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
    void SetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid key, double value);
    void SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid value);
    void SetString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    void SetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] buffer, int size);
    void SetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
    void LockStore();
    void UnlockStore();
    void GetCount(out int count);
    void GetItemByIndex(int index, out Guid key, IntPtr value);
    void CopyAllItems(IMFAttributes destination);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
internal interface IMFMediaType : IMFAttributes
{
    new void GetItem([MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
    new void GetItemType([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int type);
    new void CompareItem([MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
    new void Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
    new void GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);
    new void GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, out long value);
    new void GetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid key, out double value);
    new void GetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, out Guid value);
    new void GetStringLength([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int length);
    new void GetString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int bufferSize, out int length);
    new void GetAllocatedString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out int length);
    new void GetBlobSize([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int size);
    new void GetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPArray)] byte[] buffer, int bufferSize, out int size);
    new void GetAllocatedBlob([MarshalAs(UnmanagedType.LPStruct)] Guid key, out IntPtr buffer, out int size);
    new void GetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    new void SetItem([MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
    new void DeleteItem([MarshalAs(UnmanagedType.LPStruct)] Guid key);
    new void DeleteAllItems();
    new void SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
    new void SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
    new void SetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid key, double value);
    new void SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid value);
    new void SetString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    new void SetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] buffer, int size);
    new void SetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
    new void LockStore();
    new void UnlockStore();
    new void GetCount(out int count);
    new void GetItemByIndex(int index, out Guid key, IntPtr value);
    new void CopyAllItems(IMFAttributes destination);
    void GetMajorType(out Guid major);
    void IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool compressed);
    void IsEqual(IMFMediaType other, out int flags);
    void GetRepresentation(Guid representation, out IntPtr value);
    void FreeRepresentation(Guid representation, IntPtr value);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
internal interface IMFSample : IMFAttributes
{
    new void GetItem([MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
    new void GetItemType([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int type);
    new void CompareItem([MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
    new void Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
    new void GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);
    new void GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, out long value);
    new void GetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid key, out double value);
    new void GetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, out Guid value);
    new void GetStringLength([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int length);
    new void GetString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int bufferSize, out int length);
    new void GetAllocatedString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out int length);
    new void GetBlobSize([MarshalAs(UnmanagedType.LPStruct)] Guid key, out int size);
    new void GetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPArray)] byte[] buffer, int bufferSize, out int size);
    new void GetAllocatedBlob([MarshalAs(UnmanagedType.LPStruct)] Guid key, out IntPtr buffer, out int size);
    new void GetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    new void SetItem([MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
    new void DeleteItem([MarshalAs(UnmanagedType.LPStruct)] Guid key);
    new void DeleteAllItems();
    new void SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
    new void SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
    new void SetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid key, double value);
    new void SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid value);
    new void SetString([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    new void SetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] buffer, int size);
    new void SetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
    new void LockStore();
    new void UnlockStore();
    new void GetCount(out int count);
    new void GetItemByIndex(int index, out Guid key, IntPtr value);
    new void CopyAllItems(IMFAttributes destination);
    void GetSampleFlags(out int flags);
    void SetSampleFlags(int flags);
    void GetSampleTime(out long time);
    void SetSampleTime(long time);
    void GetSampleDuration(out long duration);
    void SetSampleDuration(long duration);
    void GetBufferCount(out int count);
    void GetBufferByIndex(int index, out IMFMediaBuffer buffer);
    void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    void AddBuffer(IMFMediaBuffer buffer);
    void RemoveBufferByIndex(int index);
    void RemoveAllBuffers();
    void GetTotalLength(out int length);
    void CopyToBuffer(IMFMediaBuffer buffer);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("045FA593-8799-42b8-BC8D-8968C6453507")]
internal interface IMFMediaBuffer
{
    void Lock(out IntPtr buffer, out int maxLength, out int currentLength);
    void Unlock();
    void GetCurrentLength(out int length);
    void SetCurrentLength(int length);
    void GetMaxLength(out int length);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
internal interface IMFSourceReader
{
    void GetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] out bool selected);
    void SetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] bool selected);
    IMFMediaType GetNativeMediaType(int stream, int typeIndex);
    IMFMediaType GetCurrentMediaType(int stream);
    void SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
    void SetCurrentPosition([MarshalAs(UnmanagedType.LPStruct)] Guid format, IntPtr position);
    void ReadSample(int stream, int flags, out int actualStream, out int streamFlags, out long timestamp, out IntPtr sample);
    void Flush(int stream);
    void GetServiceForStream(int stream, [MarshalAs(UnmanagedType.LPStruct)] Guid service, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IntPtr instance);
    void GetPresentationAttribute(int stream, [MarshalAs(UnmanagedType.LPStruct)] Guid attribute, IntPtr value);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
internal interface IMFSinkWriter
{
    void AddStream(IMFMediaType targetType, out int streamIndex);
    void SetInputMediaType(int streamIndex, IMFMediaType inputType, IMFAttributes? parameters);
    void BeginWriting();
    void WriteSample(int streamIndex, IMFSample sample);
    void SendStreamTick(int streamIndex, long timestamp);
    void PlaceMarker(int streamIndex, IntPtr context);
    void NotifyEndOfSegment(int streamIndex);
    void Flush(int streamIndex);
    void FinishWriting();
    void GetServiceForStream(int streamIndex, [MarshalAs(UnmanagedType.LPStruct)] Guid service, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IntPtr instance);
    void GetStatistics(int streamIndex, IntPtr statistics);
}
