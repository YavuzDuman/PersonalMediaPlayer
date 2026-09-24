using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Capture;

namespace PersonalMediaPlayer.App.Helpers;

internal static class NavigationHelper
{
    public static Frame? ContentFrame { get; set; }

    public static Action<CaptureKind>? RequestCapture { get; set; }

    public static bool Navigate(Type pageType, object? parameter = null)
    {
        if (ContentFrame is null)
        {
            return false;
        }

        return ContentFrame.Navigate(pageType, parameter);
    }

    public static bool GoBack()
    {
        if (ContentFrame?.CanGoBack != true)
        {
            return false;
        }

        ContentFrame.GoBack();
        return true;
    }
}
