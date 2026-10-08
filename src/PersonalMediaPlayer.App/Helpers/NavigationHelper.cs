using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
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

        var ease = clearBackStack && frame.Content is not null;
        var moved = frame.Navigate(pageType, parameter);
        if (moved && clearBackStack)
        {
            frame.BackStack.Clear();
            if (ease)
            {
                SectionArrival.Play(frame.Content as UIElement);
            }
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

internal static class SectionArrival
{
    private const int DurationMs = 180;
    private const double Rise = 8;

    private static readonly UISettings SystemUi = new();

    // Menu arrival only. GoBack does not call this, so the back stack stays immediate.
    public static void Play(UIElement? page)
    {
        if (page is null || !SystemUi.AnimationsEnabled)
        {
            return;
        }

        page.Opacity = 0;
        var lift = new TranslateTransform { Y = Rise };
        page.RenderTransform = lift;
        var board = new Storyboard();
        Add(board, page, "Opacity", 0, 1);
        Add(board, lift, "Y", Rise, 0);
        var closed = false;
        void Finish()
        {
            if (closed)
            {
                return;
            }

            closed = true;
            board.Stop();
            page.Opacity = 1;
            if (ReferenceEquals(page.RenderTransform, lift))
            {
                page.ClearValue(UIElement.RenderTransformProperty);
            }
        }

        board.Completed += (_, _) => page.DispatcherQueue.TryEnqueue(Finish);
        var watchdog = page.DispatcherQueue.CreateTimer();
        watchdog.Interval = TimeSpan.FromMilliseconds(DurationMs + 80);
        watchdog.IsRepeating = false;
        watchdog.Tick += (_, _) => Finish();
        watchdog.Start();
        board.Begin();
    }

    private static void Add(Storyboard board, DependencyObject target, string property, double from, double to)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(DurationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
    }
}
