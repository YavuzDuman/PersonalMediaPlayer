using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace PersonalMediaPlayer.App.Helpers;

internal static class ClickZoom
{
    public const float Step = 1.4f;
    public const float Min = 1f;
    public const float Max = 8f;

    public static float ZoomIn(ScrollViewer scroll, Point viewportPoint)
        => ZoomBy(scroll, viewportPoint, Step);

    public static float ZoomOut(ScrollViewer scroll, Point viewportPoint)
        => ZoomBy(scroll, viewportPoint, 1f / Step);

    public static float ZoomBy(ScrollViewer scroll, Point viewportPoint, float factor)
    {
        var current = Math.Max(scroll.ZoomFactor, 0.01f);
        var next = Math.Clamp(current * factor, Min, Math.Min(Max, scroll.MaxZoomFactor));
        if (Math.Abs(next - current) < 0.001f)
        {
            return current;
        }

        var offsetX = (scroll.HorizontalOffset + viewportPoint.X) * (next / current) - viewportPoint.X;
        var offsetY = (scroll.VerticalOffset + viewportPoint.Y) * (next / current) - viewportPoint.Y;
        scroll.ChangeView(offsetX, offsetY, next, disableAnimation: true);
        return next;
    }

    public static float HandleClick(ScrollViewer scroll, Point viewportPoint)
        => ControlIsDown() ? ZoomOut(scroll, viewportPoint) : ZoomIn(scroll, viewportPoint);

    public static float HandleWheel(ScrollViewer scroll, Point viewportPoint, int delta)
        => delta > 0 ? ZoomIn(scroll, viewportPoint) : ZoomOut(scroll, viewportPoint);

    public static bool ControlIsDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
}
