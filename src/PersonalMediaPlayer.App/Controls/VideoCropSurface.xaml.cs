using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class VideoCropSurface : UserControl
{
    private const double MinPicture = 32;

    private Rect _picture;
    private double _left;
    private double _top;
    private double _right = 1;
    private double _bottom = 1;
    private DragKind _drag;
    private Point _origin;
    private double _originLeft;
    private double _originTop;
    private double _originRight;
    private double _originBottom;

    public VideoCropSurface()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ArrangeCrop();
    }

    public event EventHandler? CropChanged;

    public double Left => _left;

    public double Top => _top;

    public double Right => _right;

    public double Bottom => _bottom;

    public bool HasCrop => _right - _left < 0.995 || _bottom - _top < 0.995 || _left > 0.005 || _top > 0.005;

    public void SetPicture(double x, double y, double width, double height)
    {
        _picture = new Rect(x, y, width, height);
        ArrangeCrop();
    }

    public void SetFractions(double left, double top, double right, double bottom)
    {
        _left = Math.Clamp(left, 0, 1);
        _top = Math.Clamp(top, 0, 1);
        _right = Math.Clamp(Math.Max(right, _left), 0, 1);
        _bottom = Math.Clamp(Math.Max(bottom, _top), 0, 1);
        ArrangeCrop();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        _left = 0;
        _top = 0;
        _right = 1;
        _bottom = 1;
        ArrangeCrop();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Frame_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_picture.Width < 1 || _picture.Height < 1)
        {
            return;
        }

        var point = e.GetCurrentPoint(Host).Position;
        _drag = Hit(point);
        _origin = point;
        _originLeft = _left;
        _originTop = _top;
        _originRight = _right;
        _originBottom = _bottom;
        Host.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Host_Moved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Host).Position;
        if (_drag == DragKind.None)
        {
            ProtectedCursor = InputSystemCursor.Create(CursorFor(Hit(point)));
            return;
        }

        var dx = (point.X - _origin.X) / _picture.Width;
        var dy = (point.Y - _origin.Y) / _picture.Height;
        var minX = MinPicture / _picture.Width;
        var minY = MinPicture / _picture.Height;
        var left = _originLeft;
        var top = _originTop;
        var right = _originRight;
        var bottom = _originBottom;
        if (_drag is DragKind.Move)
        {
            var width = right - left;
            var height = bottom - top;
            left = Math.Clamp(left + dx, 0, 1 - width);
            top = Math.Clamp(top + dy, 0, 1 - height);
            right = left + width;
            bottom = top + height;
        }
        else
        {
            if (_drag is DragKind.West or DragKind.NorthWest or DragKind.SouthWest)
            {
                left = Math.Clamp(left + dx, 0, right - minX);
            }

            if (_drag is DragKind.East or DragKind.NorthEast or DragKind.SouthEast)
            {
                right = Math.Clamp(right + dx, left + minX, 1);
            }

            if (_drag is DragKind.North or DragKind.NorthWest or DragKind.NorthEast)
            {
                top = Math.Clamp(top + dy, 0, bottom - minY);
            }

            if (_drag is DragKind.South or DragKind.SouthWest or DragKind.SouthEast)
            {
                bottom = Math.Clamp(bottom + dy, top + minY, 1);
            }
        }

        _left = left;
        _top = top;
        _right = right;
        _bottom = bottom;
        ArrangeCrop();
        CropChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void Host_Released(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragKind.None)
        {
            return;
        }

        _drag = DragKind.None;
        Host.ReleasePointerCaptures();
        e.Handled = true;
    }

    private DragKind Hit(Point point)
    {
        var rect = CropRect();
        const double edge = 14;
        var west = Math.Abs(point.X - rect.Left) <= edge;
        var east = Math.Abs(point.X - rect.Right) <= edge;
        var north = Math.Abs(point.Y - rect.Top) <= edge;
        var south = Math.Abs(point.Y - rect.Bottom) <= edge;
        if (north && west)
        {
            return DragKind.NorthWest;
        }

        if (north && east)
        {
            return DragKind.NorthEast;
        }

        if (south && west)
        {
            return DragKind.SouthWest;
        }

        if (south && east)
        {
            return DragKind.SouthEast;
        }

        if (north && point.X >= rect.Left && point.X <= rect.Right)
        {
            return DragKind.North;
        }

        if (south && point.X >= rect.Left && point.X <= rect.Right)
        {
            return DragKind.South;
        }

        if (west && point.Y >= rect.Top && point.Y <= rect.Bottom)
        {
            return DragKind.West;
        }

        if (east && point.Y >= rect.Top && point.Y <= rect.Bottom)
        {
            return DragKind.East;
        }

        return rect.Contains(point) ? DragKind.Move : DragKind.None;
    }

    private static InputSystemCursorShape CursorFor(DragKind drag) => drag switch
    {
        DragKind.North or DragKind.South => InputSystemCursorShape.SizeNorthSouth,
        DragKind.East or DragKind.West => InputSystemCursorShape.SizeWestEast,
        DragKind.NorthWest or DragKind.SouthEast => InputSystemCursorShape.SizeNorthwestSoutheast,
        DragKind.NorthEast or DragKind.SouthWest => InputSystemCursorShape.SizeNortheastSouthwest,
        DragKind.Move => InputSystemCursorShape.SizeAll,
        _ => InputSystemCursorShape.Arrow
    };

    private Rect CropRect()
    {
        return new Rect(
            _picture.X + _left * _picture.Width,
            _picture.Y + _top * _picture.Height,
            Math.Max(1, (_right - _left) * _picture.Width),
            Math.Max(1, (_bottom - _top) * _picture.Height));
    }

    private void ArrangeCrop()
    {
        if (_picture.Width < 1 || _picture.Height < 1)
        {
            Frame.Visibility = Visibility.Collapsed;
            return;
        }

        Frame.Visibility = Visibility.Visible;
        var crop = CropRect();
        Place(Frame, crop.X, crop.Y, crop.Width, crop.Height);
        Place(ShadeTop, _picture.X, _picture.Y, _picture.Width, Math.Max(0, crop.Y - _picture.Y));
        Place(ShadeBottom, _picture.X, crop.Bottom, _picture.Width, Math.Max(0, _picture.Bottom - crop.Bottom));
        Place(ShadeLeft, _picture.X, crop.Y, Math.Max(0, crop.X - _picture.X), crop.Height);
        Place(ShadeRight, crop.Right, crop.Y, Math.Max(0, _picture.Right - crop.Right), crop.Height);
    }

    private static void Place(FrameworkElement element, double x, double y, double width, double height)
    {
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
    }

    private enum DragKind
    {
        None,
        Move,
        North,
        South,
        East,
        West,
        NorthWest,
        NorthEast,
        SouthWest,
        SouthEast
    }
}
