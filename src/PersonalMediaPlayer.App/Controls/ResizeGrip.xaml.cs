using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class ResizeGrip : UserControl
{
    private bool _dragging;

    public ResizeGrip()
    {
        InitializeComponent();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }

    public bool Dragging
    {
        get => _dragging;
        set
        {
            _dragging = value;
            Line.Opacity = value ? 1 : 0.45;
        }
    }

    private void Grip_Entered(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            Line.Opacity = 1;
        }
    }

    private void Grip_Exited(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            Line.Opacity = 0.45;
        }
    }
}
