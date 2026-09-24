using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using WinRT;

namespace PersonalMediaPlayer.App.Capture;

internal static class GraphicsCaptureService
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static async Task<Bitmap> CaptureAllMonitorsAsync(
        CancellationToken cancellationToken = default,
        bool includeCursor = false)
    {
        EnsureSupported();
        ScreenGeometry.Refresh();
        var virtualDesktop = ScreenGeometry.VirtualDesktop;
        var composed = new Bitmap(Math.Max(1, virtualDesktop.Width), Math.Max(1, virtualDesktop.Height), PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(composed);
            graphics.Clear(Color.Black);

            var device = CanvasDevice.GetSharedDevice();
            var captured = 0;
            foreach (var monitor in ScreenGeometry.Monitors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (monitor.Handle == 0)
                {
                    continue;
                }

                var item = CreateItemForMonitor(monitor.Handle);
                using var slice = await CaptureOneFrameAsync(device, item, cancellationToken, includeCursor);
                graphics.DrawImage(
                    slice,
                    new Rectangle(monitor.X - virtualDesktop.X, monitor.Y - virtualDesktop.Y, monitor.Width, monitor.Height));
                captured++;
            }

            if (captured == 0)
            {
                throw new InvalidOperationException("Windows.Graphics.Capture could not capture any monitor.");
            }
        }
        catch
        {
            composed.Dispose();
            throw;
        }

        return composed;
    }

    public static async Task<Bitmap> CaptureMonitorAsync(
        MonitorRect monitor,
        CancellationToken cancellationToken = default,
        bool includeCursor = false)
    {
        EnsureSupported();
        if (monitor.Handle == 0)
        {
            throw new InvalidOperationException("That monitor cannot be captured.");
        }

        var device = CanvasDevice.GetSharedDevice();
        var item = CreateItemForMonitor(monitor.Handle);
        return await CaptureOneFrameAsync(device, item, cancellationToken, includeCursor);
    }

    public static async Task<Bitmap> CaptureWindowAsync(
        nint window,
        CancellationToken cancellationToken = default,
        bool includeCursor = false)
    {
        EnsureSupported();
        if (window == 0)
        {
            throw new InvalidOperationException("Click a visible window to capture.");
        }

        var device = CanvasDevice.GetSharedDevice();
        var item = CreateItemForWindow(window);
        return await CaptureOneFrameAsync(device, item, cancellationToken, includeCursor);
    }

    private static void EnsureSupported()
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new InvalidOperationException("Windows.Graphics.Capture is not supported on this PC.");
        }
    }

    private static async Task<Bitmap> CaptureOneFrameAsync(
        CanvasDevice device,
        GraphicsCaptureItem item,
        CancellationToken cancellationToken,
        bool includeCursor)
    {
        var completion = new TaskCompletionSource<Bitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var framePool = Direct3D11CaptureFramePool.Create(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        using var session = framePool.CreateCaptureSession(item);
#pragma warning disable CA1416
        session.IsCursorCaptureEnabled = includeCursor;
        if (ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName!, "IsBorderRequired"))
        {
            session.IsBorderRequired = false;
        }
#pragma warning restore CA1416

        framePool.FrameArrived += (_, _) =>
        {
            if (completion.Task.IsCompleted)
            {
                return;
            }

            try
            {
                using var frame = framePool.TryGetNextFrame();
                if (frame is null)
                {
                    return;
                }

                using var canvasBitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface);
                completion.TrySetResult(ToGdiBitmap(canvasBitmap));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        };

        session.StartCapture();
        try
        {
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Screen capture timed out.");
        }
    }

    internal static GraphicsCaptureItem CreateItemForMonitor(nint monitor)
        => CreateItem(interop =>
        {
            var iid = GraphicsCaptureItemIid;
            return interop.CreateForMonitor(monitor, ref iid);
        }, "Could not create a graphics capture item for a monitor.");

    internal static GraphicsCaptureItem CreateItemForWindow(nint window)
        => CreateItem(interop =>
        {
            var iid = GraphicsCaptureItemIid;
            return interop.CreateForWindow(window, ref iid);
        }, "Could not capture that window.");

    private static GraphicsCaptureItem CreateItem(Func<IGraphicsCaptureItemInterop, nint> create, string error)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var pointer = create(interop);
        if (pointer == 0)
        {
            throw new InvalidOperationException(error);
        }

        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static Bitmap ToGdiBitmap(CanvasBitmap canvas)
    {
        var width = (int)canvas.SizeInPixels.Width;
        var height = (int)canvas.SizeInPixels.Height;
        var pixels = canvas.GetPixelBytes();
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    [ComImport]
    [System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, ref Guid iid);

        nint CreateForMonitor(nint monitor, ref Guid iid);
    }
}
