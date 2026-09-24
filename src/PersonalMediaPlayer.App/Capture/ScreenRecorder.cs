using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PersonalMediaPlayer.App.Capture;

internal sealed class ScreenRecorder : IAsyncDisposable
{
    private const int TargetFps = 15;
    private static readonly TimeSpan AudioIdleGap = TimeSpan.FromMilliseconds(100);
    private readonly object _gate = new();
    private readonly Queue<byte[]> _frames = new();
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private MediaStreamSource? _source;
    private IRandomAccessStream? _output;
    private Task? _transcode;
    private MediaStreamSourceSampleRequest? _videoRequest;
    private MediaStreamSourceSampleRequestDeferral? _videoDeferral;
    private MediaStreamSourceSampleRequest? _audioRequest;
    private MediaStreamSourceSampleRequestDeferral? _audioDeferral;
    private readonly Queue<AudioPacket> _audioPackets = new();
    private SystemAudioCapture? _audio;
    private AudioStreamDescriptor? _audioDescriptor;
    private TimeSpan _audioWritten = TimeSpan.Zero;
    private int _audioBytesPerSecond;
    private int _audioBlockAlign;
    private bool _acceptAudio;
    private TimeSpan _written = TimeSpan.Zero;
    private DateTime _segmentStart;
    private TimeSpan _accumulated = TimeSpan.Zero;
    private bool _paused;
    private bool _stopping;
    private bool _started;
    private int _width;
    private int _height;
    private int _poolWidth;
    private int _poolHeight;
    private int _cropX;
    private int _cropY;
    private bool _hasCrop;

    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return ElapsedCore();
            }
        }
    }

    private TimeSpan ElapsedCore()
    {
        if (!_started)
        {
            return TimeSpan.Zero;
        }

        if (_paused)
        {
            return _accumulated;
        }

        return _accumulated + (DateTime.UtcNow - _segmentStart);
    }

    public bool IsPaused => _paused;

    public static async Task<ScreenRecorder> StartAsync(
        GraphicsCaptureItem item,
        string path,
        bool includeCursor,
        FrameCrop? crop = null,
        bool includeSystemAudio = false)
    {
        var recorder = new ScreenRecorder();
        await recorder.StartCoreAsync(item, path, includeCursor, crop, includeSystemAudio);
        return recorder;
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_started || _paused || _stopping)
            {
                return;
            }

            _accumulated += DateTime.UtcNow - _segmentStart;
            _paused = true;
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            _segmentStart = DateTime.UtcNow;
        }
    }

    public void StopCapture()
    {
        if (!_started)
        {
            return;
        }

        lock (_gate)
        {
            var end = ElapsedCore();
            _stopping = true;
            _paused = false;
            _acceptAudio = false;
            EnqueueSilence(end - _audioWritten);
        }

        TryDeliverVideo();
        TryDeliverAudio();
        DisposeCapture();
        _audio?.Dispose();
        _audio = null;
        TryDeliverVideo();
        TryDeliverAudio();
    }

    public async Task StopAsync(bool keep)
    {
        if (!_started)
        {
            return;
        }

        StopCapture();
        if (_transcode is not null)
        {
            try
            {
                await _transcode;
            }
            catch (Exception) when (!keep)
            {
            }
        }

        if (_output is not null)
        {
            _output.Dispose();
            _output = null;
        }

        _started = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await StopAsync(keep: false);
        }
    }

    private async Task StartCoreAsync(GraphicsCaptureItem item, string path, bool includeCursor, FrameCrop? crop, bool includeSystemAudio)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new InvalidOperationException("Windows.Graphics.Capture is not supported on this PC.");
        }

        var itemWidth = Math.Max(1, (int)item.Size.Width);
        var itemHeight = Math.Max(1, (int)item.Size.Height);
        if (crop is { } region)
        {
            _cropX = Math.Clamp(region.X, 0, Math.Max(0, itemWidth - 2));
            _cropY = Math.Clamp(region.Y, 0, Math.Max(0, itemHeight - 2));
            _width = Math.Max(2, Math.Min(region.Width, itemWidth - _cropX) & ~1);
            _height = Math.Max(2, Math.Min(region.Height, itemHeight - _cropY) & ~1);
            _hasCrop = true;
        }
        else
        {
            _width = Math.Max(2, itemWidth & ~1);
            _height = Math.Max(2, itemHeight & ~1);
        }
        _poolWidth = item.Size.Width;
        _poolHeight = item.Size.Height;
        _item = item;
        var device = CanvasDevice.GetSharedDevice();
        _pool = Direct3D11CaptureFramePool.Create(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _session = _pool.CreateCaptureSession(item);
#pragma warning disable CA1416
        _session.IsCursorCaptureEnabled = includeCursor;
        if (ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName!, "IsBorderRequired"))
        {
            _session.IsBorderRequired = false;
        }
#pragma warning restore CA1416
        _pool.FrameArrived += (_, _) => OnFrame(device);
        var started = false;
        try
        {
            if (includeSystemAudio)
            {
                var capture = new SystemAudioCapture();
                try
                {
                    capture.Start(OnAudioPacket);
                }
                catch
                {
                    capture.Dispose();
                    throw;
                }

                _audio = capture;
                _audioBytesPerSecond = capture.BytesPerSecond;
                _audioBlockAlign = Math.Max(2, capture.ChannelCount * 2);
            }

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            profile.Video.Width = (uint)_width;
            profile.Video.Height = (uint)_height;
            profile.Video.FrameRate.Numerator = TargetFps;
            profile.Video.FrameRate.Denominator = 1;
            profile.Video.Bitrate = (uint)Math.Clamp(_width * _height * 4L, 2_000_000, 12_000_000);
            var videoDescriptor = new VideoStreamDescriptor(VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_width, (uint)_height));
            if (_audio is null)
            {
                profile.Audio = null;
                _source = new MediaStreamSource(videoDescriptor);
            }
            else
            {
                var sampleRate = (uint)_audio.SampleRate;
                var channels = (uint)_audio.ChannelCount;
                profile.Audio = AudioEncodingProperties.CreateAac(sampleRate, channels, 128_000);
                var pcm = AudioEncodingProperties.CreatePcm(sampleRate, channels, 16);
                _audioDescriptor = new AudioStreamDescriptor(pcm);
                _source = new MediaStreamSource(videoDescriptor, _audioDescriptor);
            }

            _source.BufferTime = TimeSpan.Zero;
            _source.CanSeek = false;
            _source.Starting += (_, args) => args.Request.SetActualStartPosition(TimeSpan.Zero);
            _source.SampleRequested += (_, args) => OnSampleRequested(args);
            _output = await OpenOutputAsync(path);
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(_source, _output, profile);
            if (!prepared.CanTranscode)
            {
                throw new InvalidOperationException(prepared.FailureReason.ToString());
            }

            _segmentStart = DateTime.UtcNow;
            _started = true;
            _acceptAudio = _audio is not null;
            _transcode = prepared.TranscodeAsync().AsTask();
            _session.StartCapture();
            started = true;
        }
        finally
        {
            if (!started)
            {
                _acceptAudio = false;
                _audio?.Dispose();
                _audio = null;
                _output?.Dispose();
                _output = null;
                DisposeCapture();
            }
        }
    }

    private static async Task<IRandomAccessStream> OpenOutputAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var created = File.Create(path))
        {
        }

        var file = await StorageFile.GetFileFromPathAsync(path);
        return await file.OpenAsync(FileAccessMode.ReadWrite);
    }

    private void OnFrame(CanvasDevice device)
    {
        if (_pool is null)
        {
            return;
        }

        using var frame = _pool.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        if (TryResizePool(device, frame) || _paused || _stopping)
        {
            return;
        }

        var elapsed = Elapsed;
        if (_written != TimeSpan.Zero && elapsed - _written < TimeSpan.FromSeconds(1.0 / TargetFps))
        {
            return;
        }

        try
        {
            using var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface);
            var pixels = bitmap.GetPixelBytes();
            var surfaceWidth = (int)bitmap.SizeInPixels.Width;
            var surfaceHeight = (int)bitmap.SizeInPixels.Height;
            var contentWidth = frame.ContentSize.Width > 0 ? frame.ContentSize.Width : surfaceWidth;
            var contentHeight = frame.ContentSize.Height > 0 ? frame.ContentSize.Height : surfaceHeight;
            var packed = _hasCrop
                ? CropBottomUp(pixels, surfaceWidth, surfaceHeight, _cropX, _cropY, _width, _height)
                : Compose(_width, _height, pixels, surfaceWidth, surfaceHeight, contentWidth, contentHeight);
            lock (_gate)
            {
                if (_frames.Count >= 3)
                {
                    _frames.Dequeue();
                }

                _frames.Enqueue(packed);
            }

            TryDeliverVideo();
        }
        catch (Exception)
        {
            _stopping = true;
            TryDeliverVideo();
            TryDeliverAudio();
        }
    }

    private bool TryResizePool(CanvasDevice device, Direct3D11CaptureFrame frame)
    {
        if (_pool is null)
        {
            return false;
        }

        var content = frame.ContentSize;
        if (content.Width <= 0 || content.Height <= 0 || (content.Width == _poolWidth && content.Height == _poolHeight))
        {
            return false;
        }

        var previousWidth = _poolWidth;
        var previousHeight = _poolHeight;
        _poolWidth = content.Width;
        _poolHeight = content.Height;
        try
        {
            _pool.Recreate(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, content);
            return true;
        }
        catch (Exception)
        {
            _poolWidth = previousWidth;
            _poolHeight = previousHeight;
            return false;
        }
    }

    private static byte[] CropBottomUp(
        byte[] pixels,
        int surfaceWidth,
        int surfaceHeight,
        int cropX,
        int cropY,
        int cropWidth,
        int cropHeight)
    {
        var packed = new byte[cropWidth * cropHeight * 4];
        if (surfaceWidth <= 0 || surfaceHeight <= 0 || cropWidth <= 0 || cropHeight <= 0)
        {
            return packed;
        }

        var copyWidth = Math.Max(0, Math.Min(cropWidth, surfaceWidth - cropX));
        var copyHeight = Math.Max(0, Math.Min(cropHeight, surfaceHeight - cropY));
        var copyBytes = copyWidth * 4;
        if (copyBytes <= 0)
        {
            return packed;
        }

        for (var y = 0; y < copyHeight; y++)
        {
            var sourceIndex = ((cropY + y) * surfaceWidth + cropX) * 4;
            if (sourceIndex < 0 || sourceIndex + copyBytes > pixels.Length)
            {
                continue;
            }

            System.Buffer.BlockCopy(pixels, sourceIndex, packed, ((cropHeight - 1 - y) * cropWidth) * 4, copyBytes);
        }

        return packed;
    }

    private static byte[] Compose(
        int destWidth,
        int destHeight,
        byte[] pixels,
        int surfaceWidth,
        int surfaceHeight,
        int contentWidth,
        int contentHeight)
    {
        // Bgra8 samples are bottom-up: the first stored row is the bottom of the picture.
        // Capture bytes are top-down, so each frame is flipped while it is packed.
        contentWidth = Math.Clamp(contentWidth, 1, Math.Max(1, surfaceWidth));
        contentHeight = Math.Clamp(contentHeight, 1, Math.Max(1, surfaceHeight));
        var packed = new byte[destWidth * destHeight * 4];
        if (surfaceWidth <= 0 || pixels.Length < surfaceWidth * 4)
        {
            return packed;
        }

        var availableHeight = pixels.Length / (surfaceWidth * 4);
        contentHeight = Math.Min(contentHeight, availableHeight);
        if (contentWidth == destWidth && contentHeight == destHeight && surfaceWidth == destWidth)
        {
            CopyBottomUp(pixels, surfaceWidth, contentWidth, contentHeight, packed, destWidth, destHeight, 0, 0);
            return packed;
        }

        if (contentWidth <= destWidth && contentHeight <= destHeight)
        {
            CopyBottomUp(
                pixels,
                surfaceWidth,
                contentWidth,
                contentHeight,
                packed,
                destWidth,
                destHeight,
                (destWidth - contentWidth) / 2,
                (destHeight - contentHeight) / 2);
            return packed;
        }

        var scale = Math.Min(destWidth / (double)contentWidth, destHeight / (double)contentHeight);
        var targetWidth = Math.Max(1, Math.Min(destWidth, (int)Math.Round(contentWidth * scale)));
        var targetHeight = Math.Max(1, Math.Min(destHeight, (int)Math.Round(contentHeight * scale)));
        var offsetX = (destWidth - targetWidth) / 2;
        var offsetY = (destHeight - targetHeight) / 2;
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = y * contentHeight / targetHeight;
            var visualY = offsetY + y;
            var destIndex = ((destHeight - 1 - visualY) * destWidth + offsetX) * 4;
            var sourceRow = sourceY * surfaceWidth * 4;
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceIndex = sourceRow + (x * contentWidth / targetWidth) * 4;
                packed[destIndex++] = pixels[sourceIndex];
                packed[destIndex++] = pixels[sourceIndex + 1];
                packed[destIndex++] = pixels[sourceIndex + 2];
                packed[destIndex++] = pixels[sourceIndex + 3];
            }
        }

        return packed;
    }

    private static void CopyBottomUp(
        byte[] source,
        int surfaceWidth,
        int contentWidth,
        int contentHeight,
        byte[] destination,
        int destWidth,
        int destHeight,
        int offsetX,
        int offsetY)
    {
        var copyBytes = contentWidth * 4;
        for (var y = 0; y < contentHeight; y++)
        {
            var visualY = offsetY + y;
            if (visualY < 0 || visualY >= destHeight)
            {
                continue;
            }

            System.Buffer.BlockCopy(
                source,
                y * surfaceWidth * 4,
                destination,
                ((destHeight - 1 - visualY) * destWidth + offsetX) * 4,
                copyBytes);
        }
    }

    private void OnAudioPacket(byte[] pcm)
    {
        if (!_acceptAudio || pcm.Length == 0 || _audioBytesPerSecond <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_acceptAudio || _paused || _stopping)
            {
                return;
            }

            var ticks = pcm.Length * (long)TimeSpan.TicksPerSecond / _audioBytesPerSecond;
            if (ticks <= 0)
            {
                ticks = 1;
            }

            var duration = TimeSpan.FromTicks(ticks);
            var packetStart = ElapsedCore() - duration;
            if (packetStart < TimeSpan.Zero)
            {
                packetStart = TimeSpan.Zero;
            }

            var gap = packetStart - _audioWritten;
            if (gap >= AudioIdleGap)
            {
                EnqueueSilence(gap);
            }

            _audioPackets.Enqueue(new AudioPacket(pcm, _audioWritten, duration));
            _audioWritten += duration;
        }

        TryDeliverAudio();
    }

    private void EnqueueSilence(TimeSpan gap)
    {
        if (_audioDescriptor is null || _audioBytesPerSecond <= 0 || _audioBlockAlign <= 0 || gap < AudioIdleGap)
        {
            return;
        }

        var totalBytes = gap.Ticks * _audioBytesPerSecond / TimeSpan.TicksPerSecond;
        totalBytes -= totalBytes % _audioBlockAlign;
        if (totalBytes <= 0)
        {
            return;
        }

        var chunk = _audioBytesPerSecond / 10;
        chunk -= chunk % _audioBlockAlign;
        if (chunk <= 0)
        {
            chunk = _audioBlockAlign;
        }

        while (totalBytes > 0)
        {
            var size = (int)Math.Min(chunk, totalBytes);
            size -= size % _audioBlockAlign;
            if (size <= 0)
            {
                break;
            }

            var duration = TimeSpan.FromTicks(size * (long)TimeSpan.TicksPerSecond / _audioBytesPerSecond);
            _audioPackets.Enqueue(new AudioPacket(new byte[size], _audioWritten, duration));
            _audioWritten += duration;
            totalBytes -= size;
        }
    }

    private void OnSampleRequested(MediaStreamSourceSampleRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        var audio = args.Request.StreamDescriptor is AudioStreamDescriptor;
        lock (_gate)
        {
            if (audio)
            {
                _audioRequest = args.Request;
                _audioDeferral = deferral;
            }
            else
            {
                _videoRequest = args.Request;
                _videoDeferral = deferral;
            }
        }

        if (audio)
        {
            TryDeliverAudio();
        }
        else
        {
            TryDeliverVideo();
        }
    }

    private void TryDeliverVideo()
    {
        lock (_gate)
        {
            if (_videoRequest is null || _videoDeferral is null || _paused)
            {
                return;
            }

            if (_frames.Count == 0)
            {
                if (!_stopping)
                {
                    return;
                }

                _videoRequest.Sample = null;
                _videoDeferral.Complete();
                _videoRequest = null;
                _videoDeferral = null;
                return;
            }

            var pixels = _frames.Dequeue();
            var stamp = _written;
            _written += TimeSpan.FromSeconds(1.0 / TargetFps);
            var sample = MediaStreamSample.CreateFromBuffer(pixels.AsBuffer(), stamp);
            sample.Duration = TimeSpan.FromSeconds(1.0 / TargetFps);
            _videoRequest.Sample = sample;
            _videoDeferral.Complete();
            _videoRequest = null;
            _videoDeferral = null;
        }
    }

    private void TryDeliverAudio()
    {
        if (_audioDescriptor is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_audioRequest is null || _audioDeferral is null || _paused)
            {
                return;
            }

            if (_audioPackets.Count == 0)
            {
                if (!_stopping)
                {
                    return;
                }

                _audioRequest.Sample = null;
                _audioDeferral.Complete();
                _audioRequest = null;
                _audioDeferral = null;
                return;
            }

            var packet = _audioPackets.Dequeue();
            var sample = MediaStreamSample.CreateFromBuffer(packet.Pcm.AsBuffer(), packet.Stamp);
            sample.Duration = packet.Duration;
            _audioRequest.Sample = sample;
            _audioDeferral.Complete();
            _audioRequest = null;
            _audioDeferral = null;
        }
    }

    private void DisposeCapture()
    {
        _session?.Dispose();
        _pool?.Dispose();
        _session = null;
        _pool = null;
        _item = null;
    }
}

internal readonly record struct FrameCrop(int X, int Y, int Width, int Height);

internal readonly record struct AudioPacket(byte[] Pcm, TimeSpan Stamp, TimeSpan Duration);
