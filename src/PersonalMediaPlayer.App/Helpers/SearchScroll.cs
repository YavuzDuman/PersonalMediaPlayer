using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PersonalMediaPlayer.App.Helpers;

internal static class SearchScroll
{
    internal static void Restore(ScrollViewer? viewer, double offset)
    {
        if (viewer is null || offset <= 0)
        {
            return;
        }

        var tries = 0;
        void Apply(object? sender, object e)
        {
            tries++;
            var limit = viewer.ScrollableHeight;
            if (tries < 8 && (limit <= 0 || limit + 0.5 < offset))
            {
                return;
            }

            Stop();
            if (limit <= 0)
            {
                return;
            }

            viewer.ChangeView(null, Math.Min(offset, limit), null, true);
        }

        void OnUnload(object sender, RoutedEventArgs e) => Stop();

        void Stop()
        {
            viewer.LayoutUpdated -= Apply;
            viewer.Unloaded -= OnUnload;
        }

        viewer.LayoutUpdated += Apply;
        viewer.Unloaded += OnUnload;
        viewer.UpdateLayout();
    }

    internal static void RestoreInside(FrameworkElement? root, double offset)
    {
        if (root is null || offset <= 0)
        {
            return;
        }

        var viewer = Find(root);
        if (viewer is not null)
        {
            Restore(viewer, offset);
            return;
        }

        var tries = 0;
        void Apply(object? sender, object e)
        {
            tries++;
            var found = Find(root);
            if (found is null && tries < 8)
            {
                return;
            }

            Stop();
            Restore(found, offset);
        }

        void OnUnload(object sender, RoutedEventArgs e) => Stop();

        void Stop()
        {
            root.LayoutUpdated -= Apply;
            root.Unloaded -= OnUnload;
        }

        root.LayoutUpdated += Apply;
        root.Unloaded += OnUnload;
        root.UpdateLayout();
    }

    internal static ScrollViewer? Find(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = Find(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
