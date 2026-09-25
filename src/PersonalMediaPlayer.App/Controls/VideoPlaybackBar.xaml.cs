using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PersonalMediaPlayer.App.Editing;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class VideoPlaybackBar : UserControl
{
    private enum TrimHandle
    {
        None,
        Start,
        End,
        Seek
    }

    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly TimelineThumbnails _thumbs = new();
    private TrimHandle _trimDrag;
    private double _trimStart;
    private double _trimEnd = 1;
    private string? _hoverPath;
    private long _hoverDurationMs;
    private TimeSpan _wantedHover = TimeSpan.MinValue;
    private bool _hoverBusy;
    private bool _hoverDirty;
    private bool _hoverOpen;
    private bool _cutPicking;
    private bool _trimPassThrough;
    private double _trimGrab = 18;
    private bool _cutDragging;
    private double _cutAnchor;
    private List<(double Start, double End)> _removedFractions = [];
    private (double Start, double End)? _selection;

    public VideoPlaybackBar()
    {
        InitializeComponent();
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            _ = PumpHoverAsync();
        };
        Loaded += (_, _) =>
        {
            TimelineHost.AddHandler(PointerMovedEvent, new PointerEventHandler(Timeline_Moved), true);
            TimelineHost.AddHandler(PointerPressedEvent, new PointerEventHandler(Timeline_Pressed), true);
            TimelineHost.AddHandler(PointerReleasedEvent, new PointerEventHandler(Timeline_Released), true);
            TimelineHost.AddHandler(PointerExitedEvent, new PointerEventHandler(Timeline_Exited), true);
            TimelineHost.AddHandler(PointerCanceledEvent, new PointerEventHandler(Timeline_Released), true);
            TimelineHost.SizeChanged += (_, _) => DrawRemovedFractions();
        };
        Unloaded += (_, _) =>
        {
            _hoverTimer.Stop();
            _thumbs.Dispose();
        };
    }

    public void SetHoverSource(string? path)
    {
        if (string.Equals(_hoverPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _hoverPath = string.IsNullOrWhiteSpace(path) ? null : path;
        _thumbs.Cancel();
        HideHover();
    }

    public void ReleaseHoverFile()
    {
        _hoverTimer.Stop();
        _hoverOpen = false;
        _thumbs.ReleaseFile();
        HideHover();
    }

    public void SetHoverDuration(long durationMs)
    {
        _hoverDurationMs = durationMs > 0 ? durationMs : 0;
        if (_hoverDurationMs == 0)
        {
            HideHover();
        }
    }

    public double TrimStart => _trimStart;

    public double TrimEnd => _trimEnd;

    public event EventHandler? TrimRangeChanged;

    public event EventHandler<double>? TrimSeekRequested;

    public event EventHandler<CutSelection>? CutSelected;

    public event EventHandler<double>? TimelineClicked;

    public readonly record struct CutSelection(double Start, double End);

    public void SetCutPicking(bool enabled)
    {
        _cutPicking = enabled;
        _cutDragging = false;
        SeekSlider.IsHitTestVisible = !enabled && (_trimPassThrough || TrimCanvas.Visibility != Visibility.Visible);
    }

    public void SetSelectionFraction(double? start, double? end)
    {
        _selection = start is double from && end is double to
            ? (Math.Min(from, to), Math.Max(from, to))
            : null;
        DrawRemovedFractions();
    }

    public void SetRemovedFractions(IReadOnlyList<(double Start, double End)> fractions)
    {
        _removedFractions = fractions.ToList();
        DrawRemovedFractions();
    }

    public void BeginTrim(bool passThrough = false)
    {
        _trimPassThrough = passThrough;
        _trimStart = 0;
        _trimEnd = 1;
        TrimCanvas.Background = passThrough ? null : new SolidColorBrush(Colors.Transparent);
        TrimCanvas.Visibility = Visibility.Visible;
        TrimCanvas.IsHitTestVisible = true;
        SeekSlider.IsHitTestVisible = passThrough && !_cutPicking;
        ArrangeTrim();
    }

    public void SetTrimInteractive(bool enabled)
    {
        _trimGrab = enabled ? 28 : 18;
        TrimStartThumb.Width = enabled ? 18 : 12;
        TrimEndThumb.Width = enabled ? 18 : 12;
        TrimStartThumb.Height = enabled ? 32 : 22;
        TrimEndThumb.Height = enabled ? 32 : 22;
        TrimCanvas.IsHitTestVisible = enabled && TrimCanvas.Visibility == Visibility.Visible;
        if (!enabled)
        {
            _trimDrag = TrimHandle.None;
        }

        SeekSlider.IsHitTestVisible = !enabled && !_cutPicking;
        ArrangeTrim();
    }

    public void SetTrimFractions(double start, double end)
    {
        const double minGap = 0.01;
        _trimStart = Math.Clamp(Math.Min(start, end - minGap), 0, 1);
        _trimEnd = Math.Clamp(Math.Max(end, _trimStart + minGap), 0, 1);
        ArrangeTrim();
    }

    public void EndTrim()
    {
        _trimDrag = TrimHandle.None;
        _trimPassThrough = false;
        TrimCanvas.Background = new SolidColorBrush(Colors.Transparent);
        TrimCanvas.Visibility = Visibility.Collapsed;
        TrimCanvas.IsHitTestVisible = false;
        SeekSlider.IsHitTestVisible = !_cutPicking;
    }

    private void Timeline_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_hoverDurationMs <= 0 || string.IsNullOrWhiteSpace(_hoverPath) || TimelineHost.ActualWidth <= 1)
        {
            HideHover();
            return;
        }

        if (_cutDragging && TimelineHost.ActualWidth > 1)
        {
            var dragEnd = CutFraction(e);
            _selection = (Math.Min(_cutAnchor, dragEnd), Math.Max(_cutAnchor, dragEnd));
            DrawRemovedFractions();
        }

        var x = Math.Clamp(e.GetCurrentPoint(TimelineHost).Position.X, 0, TimelineHost.ActualWidth);
        var ms = (long)(x / TimelineHost.ActualWidth * _hoverDurationMs);
        HoverTime.Text = FormatHover(ms);
        _wantedHover = TimeSpan.FromMilliseconds(ms);
        _hoverOpen = true;
        HoverCard.Visibility = Visibility.Visible;
        var cardWidth = HoverCard.ActualWidth > 1 ? HoverCard.ActualWidth : 168;
        var left = e.GetCurrentPoint(this).Position.X - cardWidth / 2;
        HoverCard.Margin = new Thickness(Math.Clamp(left, 0, Math.Max(0, ActualWidth - cardWidth)), -128, 0, 0);
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void Timeline_Exited(object sender, PointerRoutedEventArgs e) => HideHover();

    private void Timeline_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_trimDrag != TrimHandle.None || !_cutPicking || TimelineHost.ActualWidth <= 1)
        {
            return;
        }

        _cutDragging = true;
        _cutAnchor = CutFraction(e);
        TimelineHost.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Timeline_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_cutDragging)
        {
            return;
        }

        _cutDragging = false;
        TimelineHost.ReleasePointerCaptures();
        var end = CutFraction(e);
        var start = Math.Min(_cutAnchor, end);
        end = Math.Max(_cutAnchor, end);
        if (end - start < 0.008)
        {
            TimelineClicked?.Invoke(this, end);
        }
        else
        {
            CutSelected?.Invoke(this, new CutSelection(start, end));
        }

        e.Handled = true;
    }

    private double CutFraction(PointerRoutedEventArgs e)
        => Math.Clamp(e.GetCurrentPoint(TimelineHost).Position.X / Math.Max(1, TimelineHost.ActualWidth), 0, 1);

    private void DrawRemovedFractions()
    {
        CutsCanvas.Children.Clear();
        var width = TimelineHost.ActualWidth;
        var height = Math.Max(8, TimelineHost.ActualHeight);
        if (width <= 1)
        {
            return;
        }

        CutsCanvas.Width = width;
        CutsCanvas.Height = height;
        foreach (var (start, end) in _removedFractions)
        {
            AddSpan(start, end, width, height, 8, ColorHelper.FromArgb(210, 220, 48, 48));
        }

        if (_selection is { } selected)
        {
            AddSpan(selected.Start, selected.End, width, height, 16, ColorHelper.FromArgb(230, 255, 186, 46));
        }
    }

    private void AddSpan(double start, double end, double width, double height, double band, Windows.UI.Color color)
    {
        var mark = new Border
        {
            Width = Math.Max(2, (end - start) * width),
            Height = band,
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(mark, start * width);
        Canvas.SetTop(mark, (height - band) / 2);
        CutsCanvas.Children.Add(mark);
    }

    private void HideHover()
    {
        _hoverOpen = false;
        _hoverTimer.Stop();
        _thumbs.Cancel();
        HoverCard.Visibility = Visibility.Collapsed;
        HoverImage.Source = null;
    }

    private async Task PumpHoverAsync()
    {
        if (_hoverBusy)
        {
            _hoverDirty = true;
            return;
        }

        _hoverBusy = true;
        try
        {
            do
            {
                _hoverDirty = false;
                var path = _hoverPath;
                var time = _wantedHover;
                if (!_hoverOpen || path is null || time < TimeSpan.Zero)
                {
                    return;
                }

                try
                {
                    var bitmap = await _thumbs.GrabAsync(path, time);
                    if (_hoverOpen && bitmap is not null && time == _wantedHover)
                    {
                        HoverImage.Source = bitmap;
                    }
                }
                catch (Exception)
                {
                    if (_hoverOpen)
                    {
                        HoverImage.Source = null;
                    }
                }
            }
            while (_hoverDirty && _hoverOpen);
        }
        finally
        {
            _hoverBusy = false;
        }
    }

    private static string FormatHover(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private void TrimCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => ArrangeTrim();

    private void Trim_Pressed(object sender, PointerRoutedEventArgs e)
    {
        var width = TrimCanvas.ActualWidth;
        if (width <= 1)
        {
            return;
        }

        var x = e.GetCurrentPoint(TrimCanvas).Position.X;
        var startX = _trimStart * width;
        var endX = _trimEnd * width;
        _trimDrag = Math.Abs(x - startX) <= _trimGrab
            ? TrimHandle.Start
            : Math.Abs(x - endX) <= _trimGrab
                ? TrimHandle.End
                : TrimHandle.Seek;
        TrimCanvas.CapturePointer(e.Pointer);
        ApplyTrimDrag(x);
        e.Handled = true;
    }

    private void Trim_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_trimDrag == TrimHandle.None)
        {
            return;
        }

        ApplyTrimDrag(e.GetCurrentPoint(TrimCanvas).Position.X);
        e.Handled = true;
    }

    private void Trim_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_trimDrag == TrimHandle.None)
        {
            return;
        }

        _trimDrag = TrimHandle.None;
        TrimCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void ApplyTrimDrag(double x)
    {
        var width = Math.Max(1, TrimCanvas.ActualWidth);
        var fraction = Math.Clamp(x / width, 0, 1);
        const double minGap = 0.01;
        if (_trimDrag == TrimHandle.Start)
        {
            _trimStart = Math.Min(fraction, _trimEnd - minGap);
        }
        else if (_trimDrag == TrimHandle.End)
        {
            _trimEnd = Math.Max(fraction, _trimStart + minGap);
        }
        else if (_trimDrag == TrimHandle.Seek)
        {
            var inside = Math.Clamp(fraction, _trimStart, _trimEnd);
            TrimSeekRequested?.Invoke(this, inside);
            return;
        }

        _trimStart = Math.Clamp(_trimStart, 0, 1);
        _trimEnd = Math.Clamp(_trimEnd, 0, 1);
        ArrangeTrim();
        TrimRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ArrangeTrim()
    {
        var width = TrimCanvas.ActualWidth;
        if (width <= 1)
        {
            return;
        }

        var startX = _trimStart * width;
        var endX = _trimEnd * width;
        Canvas.SetLeft(TrimRange, startX);
        Canvas.SetTop(TrimRange, 12);
        TrimRange.Width = Math.Max(0, endX - startX);
        Canvas.SetLeft(TrimStartThumb, Math.Clamp(startX - 6, 0, width - 12));
        Canvas.SetTop(TrimStartThumb, 3);
        Canvas.SetLeft(TrimEndThumb, Math.Clamp(endX - 6, 0, width - 12));
        Canvas.SetTop(TrimEndThumb, 3);
    }
}
