using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PersonalMediaPlayer.App.Helpers;

internal static class ClipboardHelper
{
    public static void CopyText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public static async Task CopyPngAsync(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(pngBytes.AsBuffer());
        stream.Seek(0);
        SetBitmap(stream);
    }

    public static async Task CopyImageFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var fileStream = await file.OpenReadAsync();
        var memory = new InMemoryRandomAccessStream();
        await RandomAccessStream.CopyAsync(fileStream, memory);
        memory.Seek(0);
        SetBitmap(memory);
    }

    private static void SetBitmap(IRandomAccessStream stream)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
