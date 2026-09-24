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
}
