using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Views;

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

    internal static bool OpenPlayer(object? parameter)
    {
        if (parameter is null || App.MainAppWindow is not MainWindow window)
        {
            return false;
        }

        return window.ShowPlayer(parameter);
    }

    internal static bool Follow(Frame frame, Type pageType, object? parameter, bool clearBackStack)
    {
        if (pageType == typeof(VideoPlayerPage))
        {
            return OpenPlayer(parameter);
        }

        var moved = frame.Navigate(pageType, parameter);
        if (moved && clearBackStack)
        {
            frame.BackStack.Clear();
        }

        return moved;
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
