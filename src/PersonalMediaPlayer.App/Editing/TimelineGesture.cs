namespace PersonalMediaPlayer.App.Editing;

internal static class TimelineGesture
{
    // A click jitters by a few pixels. A longer movement is a selection, so zoom decides how short a span can be.
    internal const double DragSlopPixels = 6;

    // Past this width the timeline element is too large for layout.
    internal const double MaxTimelineWidth = 160_000;

    // Trim handles stay at least this far apart. One percent of a long video is a long way on screen once zoomed.
    internal const long MinimumKeepMs = 400;

    internal const double EdgeMargin = 56;

    internal static readonly double[] ZoomSteps = [1, 2, 4, 8, 16, 32, 64, 128, 256];

    internal static bool IsDrag(double startFraction, double endFraction, double widthPixels)
    {
        if (widthPixels <= 1 || double.IsNaN(widthPixels) || double.IsInfinity(widthPixels))
        {
            return false;
        }

        var pixels = Math.Abs(endFraction - startFraction) * widthPixels;
        return !double.IsNaN(pixels) && pixels >= DragSlopPixels;
    }

    internal static int HighestZoomIndex(double viewportPixels)
    {
        if (viewportPixels <= 1)
        {
            return ZoomSteps.Length - 1;
        }

        var index = 0;
        for (var step = 0; step < ZoomSteps.Length; step++)
        {
            if (viewportPixels * ZoomSteps[step] <= MaxTimelineWidth)
            {
                index = step;
            }
        }

        return index;
    }

    internal static double TimelineWidth(double viewportPixels, int zoomIndex)
    {
        if (viewportPixels <= 1)
        {
            return Math.Max(1, viewportPixels);
        }

        var index = Math.Clamp(zoomIndex, 0, ZoomSteps.Length - 1);
        var zoomed = viewportPixels * ZoomSteps[index];
        return Math.Max(viewportPixels, Math.Min(MaxTimelineWidth, zoomed));
    }

    internal static double TrimGapFraction(long durationMs)
    {
        if (durationMs <= 0)
        {
            return 0.01;
        }

        var halfSecond = MinimumKeepMs / (double)durationMs;
        return Math.Min(0.01, Math.Min(1, halfSecond));
    }

    // One slider step is one pixel, so a zoomed playhead follows the pointer.
    internal static double SeekStep(double maximum, double widthPixels)
    {
        if (maximum <= 0 || widthPixels <= 1 || double.IsNaN(widthPixels) || double.IsInfinity(widthPixels))
        {
            return Math.Max(0, maximum);
        }

        return Math.Clamp(maximum / widthPixels, maximum / 1_000_000d, maximum);
    }

    internal static double WheelOffset(double viewportPixels, int wheelDelta)
    {
        if (viewportPixels <= 1 || wheelDelta == 0)
        {
            return 0;
        }

        var notches = wheelDelta / 120d;
        var page = Math.Clamp(viewportPixels * 0.18, 48, 240);
        return -notches * page;
    }

    internal static double PagePan(double viewportPixels)
        => viewportPixels <= 1 ? 0 : Math.Max(48, viewportPixels * 0.8);

    internal static double KeyPan(double viewportPixels)
        => viewportPixels <= 1 ? 0 : Math.Clamp(viewportPixels * 0.22, 64, 280);

    internal static double EdgeScroll(double pointerX, double viewportPixels)
    {
        if (viewportPixels <= 1 || double.IsNaN(pointerX) || double.IsInfinity(pointerX))
        {
            return 0;
        }

        if (pointerX < EdgeMargin)
        {
            var closeness = Math.Clamp((EdgeMargin - pointerX) / EdgeMargin, 0, 1);
            return -Math.Max(1, closeness * closeness * 28);
        }

        var right = viewportPixels - EdgeMargin;
        if (pointerX > right)
        {
            var closeness = Math.Clamp((pointerX - right) / EdgeMargin, 0, 1);
            return Math.Max(1, closeness * closeness * 28);
        }

        return 0;
    }
}
