using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace PersonalMediaPlayer.App.Helpers;

internal static class UiMotion
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(240);

    public static Task DoubleAsync(DependencyObject target, string property, double from, double to)
    {
        var tcs = new TaskCompletionSource();
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = Duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => tcs.TrySetResult();
        storyboard.Begin();
        return tcs.Task;
    }

    public static async Task ThicknessAsync(Action<Thickness> apply, Thickness from, Thickness to)
    {
        const int frames = 14;
        for (var i = 1; i <= frames; i++)
        {
            var t = i / (double)frames;
            t = t * t * (3 - 2 * t);
            apply(new Thickness(
                Lerp(from.Left, to.Left, t),
                Lerp(from.Top, to.Top, t),
                Lerp(from.Right, to.Right, t),
                Lerp(from.Bottom, to.Bottom, t)));
            await Task.Delay(16);
        }

        apply(to);
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);
}
