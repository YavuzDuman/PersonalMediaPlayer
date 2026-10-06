using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace PersonalMediaPlayer.App.Controls;

public sealed class WrapBar : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(WrapBar),
        new PropertyMetadata(8.0, (sender, _) => (sender as WrapBar)?.InvalidateMeasure()));

    public WrapBar()
    {
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var limit = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width);
        var gap = Math.Max(0, Spacing);
        double x = 0, y = 0, rowHeight = 0, width = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var desired = MeasureChild(child, limit);
            if (x > 0 && !double.IsInfinity(limit) && x + gap + desired > limit + 0.5)
            {
                y += rowHeight + gap;
                x = 0;
                rowHeight = 0;
            }

            if (x > 0)
            {
                x += gap;
            }

            x += desired;
            width = Math.Max(width, x);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(limit) ? width : limit, y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var limit = Math.Max(0, finalSize.Width);
        var gap = Math.Max(0, Spacing);
        var rows = new List<List<UIElement>>();
        var current = new List<UIElement>();
        double used = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var desired = Math.Min(child.DesiredSize.Width, limit);
            if (current.Count > 0 && used + gap + desired > limit + 0.5)
            {
                rows.Add(current);
                current = [];
                used = 0;
            }

            if (current.Count > 0)
            {
                used += gap;
            }

            current.Add(child);
            used += desired;
        }

        if (current.Count > 0)
        {
            rows.Add(current);
        }

        double y = 0;
        foreach (var row in rows)
        {
            var rowHeight = 0.0;
            foreach (var child in row)
            {
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            double left = 0;
            foreach (var child in row)
            {
                var width = Math.Min(child.DesiredSize.Width, Math.Max(0, limit - left));
                var top = y + Math.Max(0, rowHeight - child.DesiredSize.Height);
                child.Arrange(new Rect(left, top, width, child.DesiredSize.Height));
                left += width + gap;
            }

            y += rowHeight + gap;
        }

        return finalSize;
    }

    private static double MeasureChild(UIElement child, double limit)
    {
        child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = child.DesiredSize.Width;
        if (!double.IsInfinity(limit) && desired > limit)
        {
            child.Measure(new Size(limit, double.PositiveInfinity));
            desired = Math.Min(child.DesiredSize.Width, limit);
        }

        return desired;
    }
}
