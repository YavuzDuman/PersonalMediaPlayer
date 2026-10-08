using PersonalMediaPlayer.App.Editing;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class TimelineGestureTests
{
    [Fact]
    public void A_few_pixels_stay_a_click()
    {
        Assert.False(TimelineGesture.IsDrag(0, 5.0 / 1000, 1000));
        Assert.True(TimelineGesture.IsDrag(0, 6.0 / 1000, 1000));
        Assert.False(TimelineGesture.IsDrag(0.2, 0.5, 0));
    }

    [Fact]
    public void Two_seconds_on_a_long_video_is_a_drag_after_zoom()
    {
        const double hourMs = 60 * 60 * 1000;
        const double twoSeconds = 2_000;
        const double viewport = 800;
        var fraction = twoSeconds / hourMs;

        Assert.False(TimelineGesture.IsDrag(0, fraction, TimelineGesture.TimelineWidth(viewport, 0)));

        var zoom = TimelineGesture.HighestZoomIndex(viewport);
        Assert.Equal(128, TimelineGesture.ZoomSteps[zoom]);
        Assert.True(TimelineGesture.IsDrag(0, fraction, TimelineGesture.TimelineWidth(viewport, zoom)));
    }

    [Fact]
    public void A_short_fraction_of_a_wide_timeline_is_kept()
    {
        Assert.True(TimelineGesture.IsDrag(0.500, 0.501, TimelineGesture.MaxTimelineWidth));
    }

    [Fact]
    public void Zoom_stops_before_the_timeline_is_too_wide()
    {
        Assert.Equal(256, TimelineGesture.ZoomSteps[TimelineGesture.HighestZoomIndex(500)]);
        Assert.Equal(64, TimelineGesture.ZoomSteps[TimelineGesture.HighestZoomIndex(2_000)]);
        Assert.Equal(TimelineGesture.MaxTimelineWidth, TimelineGesture.TimelineWidth(800, TimelineGesture.ZoomSteps.Length - 1));
        Assert.Equal(800, TimelineGesture.TimelineWidth(800, 0));
    }

    [Fact]
    public void A_zoomed_playhead_moves_one_pixel_at_a_time()
    {
        var wide = TimelineGesture.SeekStep(1000, 102_400);
        var normal = TimelineGesture.SeekStep(1000, 800);

        Assert.InRange(wide / 1000d * 102_400, 0.9, 1.1);
        Assert.InRange(normal / 1000d * 800, 0.9, 1.1);
        Assert.True(wide < 1);
    }

    [Fact]
    public void Trim_handles_can_sit_half_a_second_apart_on_a_long_video()
    {
        var hour = TimelineGesture.TrimGapFraction(60 * 60 * 1000);
        Assert.Equal(400d / (60 * 60 * 1000), hour);
        Assert.True(hour < 0.01);
        Assert.Equal(0.01, TimelineGesture.TrimGapFraction(10_000));
        Assert.Equal(0.01, TimelineGesture.TrimGapFraction(0));
    }

    [Fact]
    public void Wheel_and_edge_scroll_move_a_zoomed_timeline()
    {
        Assert.Equal(-144, TimelineGesture.WheelOffset(800, 120));
        Assert.Equal(288, TimelineGesture.WheelOffset(800, -240));
        Assert.Equal(0, TimelineGesture.EdgeScroll(400, 800));
        Assert.True(TimelineGesture.EdgeScroll(0, 800) < TimelineGesture.EdgeScroll(40, 800));
        Assert.True(TimelineGesture.EdgeScroll(40, 800) < 0);
        Assert.True(TimelineGesture.EdgeScroll(800, 800) > TimelineGesture.EdgeScroll(760, 800));
        Assert.True(TimelineGesture.PagePan(800) > TimelineGesture.KeyPan(800));
    }
}
