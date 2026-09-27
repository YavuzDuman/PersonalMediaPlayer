using Microsoft.UI.Xaml;
using PersonalMediaPlayer.Core;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PersonalMediaPlayer.App.Helpers;

internal static class FilePickerHelper
{
    public static async Task<IReadOnlyList<StorageFile>> PickMediaAsync(Window window)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.VideosLibrary
        };

        foreach (var extension in MediaFileTypes.ImageExtensions.Concat(MediaFileTypes.VideoExtensions))
        {
            picker.FileTypeFilter.Add(extension);
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        return files is { Count: > 0 } ? files.ToArray() : [];
    }

    public static async Task<StorageFile?> PickTextFileAsync(Window window)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".txt");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSingleFileAsync();
    }

    public static async Task<StorageFile?> PickSavePngAsync(Window window, string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName)
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSaveFileAsync();
    }

    public static async Task<StorageFile?> PickSaveDownloadAsync(Window window, string suggestedName, bool audio)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = audio ? PickerLocationId.MusicLibrary : PickerLocationId.VideosLibrary,
            SuggestedFileName = suggestedName
        };
        picker.FileTypeChoices.Add(audio ? "Audio" : "Video", [audio ? ".m4a" : ".mp4"]);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSaveFileAsync();
    }

    public static async Task<IReadOnlyList<StorageFile>> PickVideosAsync(Window window)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.VideosLibrary
        };
        foreach (var extension in new[] { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v" })
        {
            picker.FileTypeFilter.Add(extension);
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var files = await picker.PickMultipleFilesAsync();
        return files is { Count: > 0 } ? files.ToArray() : [];
    }

    public static async Task<StorageFile?> PickSaveVideoAsync(Window window, string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            SuggestedFileName = suggestedName
        };
        picker.FileTypeChoices.Add("MP4 video", [".mp4"]);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSaveFileAsync();
    }

    public static async Task<StorageFile?> PickSaveAudioAsync(Window window, string suggestedName, string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            SuggestedFileName = suggestedName
        };
        picker.FileTypeChoices.Add(extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ? "WAV" : "MP3", [extension]);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSaveFileAsync();
    }
}
