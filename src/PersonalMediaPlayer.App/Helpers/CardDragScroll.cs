using System.Runtime.CompilerServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace PersonalMediaPlayer.App.Helpers;

internal static class CardDragScroll
{
    private const double Slop = 8;
    private static readonly ConditionalWeakTable<ScrollViewer, Drag> Hooks = new();

    internal static void Attach(ScrollViewer viewer)
    {
        if (Hooks.TryGetValue(viewer, out _))
        {
            return;
        }

        var drag = new Drag(viewer);
        Hooks.Add(viewer, drag);
        viewer.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(drag.OnPressed), true);
        viewer.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(drag.OnMoved), true);
        viewer.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(drag.OnReleased), true);
        viewer.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(drag.OnReleased), true);
        viewer.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(drag.OnCaptureLost), true);
    }

    private sealed class Drag
    {
        private readonly ScrollViewer _viewer;
        private readonly PointerEventHandler _moved;
        private readonly PointerEventHandler _released;
        private readonly List<UIElement> _watched = [];
        private uint _pointerId;
        private double _startX;
        private double _startY;
        private double _startOffset;
        private bool _tracking;
        private bool _dragging;

        internal Drag(ScrollViewer viewer)
        {
            _viewer = viewer;
            _moved = OnMoved;
            _released = OnReleased;
        }

        internal void OnPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_tracking || e.Pointer.PointerDeviceType == PointerDeviceType.Touch || IsOnScrollBar(e.OriginalSource as DependencyObject))
            {
                return;
            }

            var point = e.GetCurrentPoint(_viewer);
            if (!point.Properties.IsLeftButtonPressed || _viewer.ScrollableWidth <= 0)
            {
                return;
            }

            _pointerId = e.Pointer.PointerId;
            _startX = point.Position.X;
            _startY = point.Position.Y;
            _startOffset = _viewer.HorizontalOffset;
            _tracking = true;
            _dragging = false;
            Watch(e.OriginalSource as DependencyObject);
        }

        internal void OnMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_tracking || e.Pointer.PointerId != _pointerId)
            {
                return;
            }

            var point = e.GetCurrentPoint(_viewer);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            var dx = point.Position.X - _startX;
            var dy = point.Position.Y - _startY;
            if (!_dragging)
            {
                if (Math.Abs(dx) < Slop && Math.Abs(dy) < Slop)
                {
                    return;
                }

                if (Math.Abs(dx) < Slop || Math.Abs(dx) <= Math.Abs(dy))
                {
                    if (Math.Abs(dy) >= Slop)
                    {
                        Stop();
                    }

                    return;
                }

                if (!_viewer.CapturePointer(e.Pointer))
                {
                    Stop();
                    return;
                }

                _dragging = true;
            }

            _viewer.ChangeView(_startOffset - dx, null, null, true);
            e.Handled = true;
        }

        internal void OnReleased(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerId != _pointerId)
            {
                return;
            }

            var dragged = _dragging;
            Stop();
            if (!dragged)
            {
                return;
            }

            e.Handled = true;
            if (_viewer.PointerCaptures is null)
            {
                return;
            }

            foreach (var captured in _viewer.PointerCaptures)
            {
                if (captured.PointerId == e.Pointer.PointerId)
                {
                    _viewer.ReleasePointerCapture(e.Pointer);
                    break;
                }
            }
        }

        internal void OnCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerId == _pointerId)
            {
                Stop();
            }
        }

        private void Stop()
        {
            _tracking = false;
            _dragging = false;
            ClearWatch();
        }

        private void Watch(DependencyObject? source)
        {
            ClearWatch();
            for (var node = source as UIElement; node is not null && !ReferenceEquals(node, _viewer); node = VisualTreeHelper.GetParent(node) as UIElement)
            {
                node.AddHandler(UIElement.PointerMovedEvent, _moved, true);
                node.AddHandler(UIElement.PointerReleasedEvent, _released, true);
                node.AddHandler(UIElement.PointerCanceledEvent, _released, true);
                _watched.Add(node);
            }
        }

        private void ClearWatch()
        {
            foreach (var node in _watched)
            {
                node.RemoveHandler(UIElement.PointerMovedEvent, _moved);
                node.RemoveHandler(UIElement.PointerReleasedEvent, _released);
                node.RemoveHandler(UIElement.PointerCanceledEvent, _released);
            }

            _watched.Clear();
        }

        private bool IsOnScrollBar(DependencyObject? source)
        {
            for (var node = source; node is not null && !ReferenceEquals(node, _viewer); node = VisualTreeHelper.GetParent(node))
            {
                if (node is ScrollBar)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
