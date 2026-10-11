using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Playback;

namespace PersonalMediaPlayer.App.Controls;

// Sleep for a preview that has its own player. The watching player keeps its own menu.
internal sealed class PreviewSleep
{
    private readonly ListeningSession _session = new();
    private readonly MenuFlyout _menu = new();

    public PreviewSleep()
    {
        Add("off", "Off");
        Add("1", "1 minute");
        Add("5", "5 minutes");
        Add("15", "15 minutes");
        Add("30", "30 minutes");
        Add("60", "60 minutes");
        Add("end", "End of this video");
    }

    public event Action? Changed;

    public void Show(FrameworkElement anchor) => _menu.ShowAt(anchor);

    public void Paint(Button button, TextBlock label)
    {
        var text = _session.Label(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        label.Text = text;
        var tip = _session.Sleep switch
        {
            SleepSpan.EndOfVideo => "Pause when this video ends",
            SleepSpan.Off => "Sleep timer",
            _ => "Pause in " + text
        };
        ToolTipService.SetToolTip(button, tip);
    }

    public bool Tick(Action? pause)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!_session.Due(now))
        {
            if (_session.DeadlineUtcMs is not null)
            {
                Changed?.Invoke();
            }

            return false;
        }

        _session.ClearSleep();
        try
        {
            pause?.Invoke();
        }
        catch (Exception)
        {
            // The preview can already be stopped when the timer arrives.
        }

        Changed?.Invoke();
        return true;
    }

    public bool HoldAtEnd()
    {
        if (!_session.StopsAtEnd)
        {
            return false;
        }

        _session.ClearSleep();
        Changed?.Invoke();
        return true;
    }

    private void Add(string tag, string text)
    {
        var item = new MenuFlyoutItem { Text = text, Tag = tag };
        item.Click += (_, _) => Choose(tag);
        _menu.Items.Add(item);
    }

    private void Choose(string tag)
    {
        var span = tag switch
        {
            "1" => SleepSpan.OneMinute,
            "5" => SleepSpan.FiveMinutes,
            "15" => SleepSpan.QuarterHour,
            "30" => SleepSpan.HalfHour,
            "60" => SleepSpan.Hour,
            "end" => SleepSpan.EndOfVideo,
            _ => SleepSpan.Off
        };
        _session.SetSleep(span, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Changed?.Invoke();
    }
}
