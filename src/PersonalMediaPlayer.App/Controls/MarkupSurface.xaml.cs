using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Helpers;
using Windows.Foundation;
using Windows.UI;
using DrawingPoint = System.Drawing.Point;
using Point = Windows.Foundation.Point;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace PersonalMediaPlayer.App.Controls;

public sealed partial class MarkupSurface : UserControl, INotifyPropertyChanged
{
    private readonly List<ScreenshotMark> _marks = [];
    private readonly List<EditorSnapshot> _history = [];
    private int _historyIndex = -1;
    private ImagePlate? _plate;
    private ImagePlate? _loadedPlate;
    private int _minNextStep = 1;
    private AnnotationTool _tool;
    private Color _strokeColor = Color.FromArgb(255, 232, 17, 35);
    private int _pixelWidth;
    private int _pixelHeight;
    private IReadOnlyList<OcrWordBox> _searchHighlights = [];
    private byte[]? _sourceBytes;
    private System.Drawing.Bitmap? _sourceBitmap;
    private bool _canEdit = true;
    private double _previewScale = 1;
    private DrawingPoint _drawStart;
    private bool _drawing;
    private Polyline? _draftStroke;
    private Line? _draftLine;
    private Rectangle? _draftRect;
    private TextMark? _selectedText;
    private Canvas? _selectedHost;
    private TextBlock? _selectedBlock;
    private TextAction _textAction;
    private ResizeEdge _resizeEdge;
    private Point _textPointerStart;
    private DrawingPoint _textOriginStart;
    private float _textFontStart;
    private double _startLeft;
    private double _startTop;
    private double _startWidth;
    private double _startHeight;
    private bool _textChanged;
    private bool _eraseChanged;
    private bool _comparing;
    private bool _compareDrag;
    private int _beforeWidth;
    private int _beforeHeight;
    private double _comparePercent = 50;
    private int _penSize = 4;
    private int _highlightSize = 4;
    private int _eraserSize = 5;

    private enum TextAction { None, Drag, Resize }

    private enum ResizeEdge
    {
        None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight
    }

    public MarkupSurface()
    {
        InitializeComponent();
        ResetHistory();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? MarksChanged;

    public event EventHandler<MarkupImageEventArgs>? ImageChanged;

    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            if (_canEdit == value)
            {
                return;
            }

            _canEdit = value;
            if (!value)
            {
                _tool = AnnotationTool.None;
                _selectedText = null;
            }

            Notify(nameof(CanEdit));
            Notify(nameof(CanUndo));
            Notify(nameof(CanRedo));
            Notify(nameof(CanClearMarks));
            UpdateCanvasHitTest();
            RebuildMarks();
        }
    }

    public bool HasMarks => _marks.Count > 0;

    public bool HasEdits
        => HasMarks || (_plate is not null && _loadedPlate is not null && !ReferenceEquals(_plate, _loadedPlate));

    public bool ShowStrokeSize
        => _tool is AnnotationTool.Pen or AnnotationTool.Highlight or AnnotationTool.Eraser;

    public Visibility StrokeSizeVisibility
        => ShowStrokeSize && !_comparing ? Visibility.Visible : Visibility.Collapsed;

    public bool IsComparing => _comparing;

    public Visibility ToolBarVisibility => _comparing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CompareBarVisibility => _comparing ? Visibility.Visible : Visibility.Collapsed;

    public double ComparePercent
    {
        get => _comparePercent;
        set
        {
            var next = Math.Clamp(value, 0, 100);
            if (Math.Abs(_comparePercent - next) < 0.01)
            {
                return;
            }

            _comparePercent = next;
            Notify(nameof(ComparePercent));
            LayoutCompare();
        }
    }

    public string StrokeSizeLabel => _tool switch
    {
        AnnotationTool.Highlight => "Highlighter",
        AnnotationTool.Eraser => "Eraser",
        _ => "Pen"
    };

    public double StrokeSize
    {
        get => _tool switch
        {
            AnnotationTool.Highlight => _highlightSize,
            AnnotationTool.Eraser => _eraserSize,
            _ => _penSize
        };
        set
        {
            var size = (int)Math.Clamp(Math.Round(value), 1, 12);
            switch (_tool)
            {
                case AnnotationTool.Highlight:
                    _highlightSize = size;
                    break;
                case AnnotationTool.Eraser:
                    _eraserSize = size;
                    break;
                default:
                    _penSize = size;
                    break;
            }

            Notify(nameof(StrokeSize));
        }
    }

    public bool CanUndo => CanEdit && _historyIndex > 0;

    public bool CanRedo => CanEdit && _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public bool CanClearMarks => CanEdit && _marks.Count > 0;

    public void ShowSearchHighlights(IReadOnlyList<OcrWordBox> boxes)
    {
        _searchHighlights = boxes;
        DrawSearchHighlights();
    }

    public void Load(BitmapImage? image, int pixelWidth, int pixelHeight, byte[]? sourceBytes = null)
    {
        ResetCompareVisual();
        PreviewImage.Source = image;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _sourceBytes = sourceBytes;
        _plate = new ImagePlate
        {
            Png = sourceBytes ?? [],
            Width = pixelWidth,
            Height = pixelHeight
        };
        _loadedPlate = _plate;
        _sourceBitmap?.Dispose();
        _sourceBitmap = null;
        if (sourceBytes is { Length: > 0 })
        {
            using var stream = new MemoryStream(sourceBytes, writable: false);
            using var loaded = new System.Drawing.Bitmap(stream);
            _sourceBitmap = new System.Drawing.Bitmap(
                Math.Max(1, loaded.Width),
                Math.Max(1, loaded.Height),
                PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(_sourceBitmap);
            graphics.DrawImage(loaded, 0, 0, _sourceBitmap.Width, _sourceBitmap.Height);
        }
        _marks.Clear();
        _minNextStep = 1;
        _selectedText = null;
        _tool = AnnotationTool.None;
        ResetHistory();
        _searchHighlights = [];
        SizePreviewHost();
        RebuildMarks();
        Notify(nameof(HasMarks));
        Notify(nameof(HasEdits));
        Notify(nameof(CanUndo));
        Notify(nameof(CanRedo));
        Notify(nameof(CanClearMarks));
        MarksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear() => Load(null, 0, 0);

    public void BeginCompare(BitmapImage before, int beforeWidth, int beforeHeight)
    {
        if (beforeWidth < 1 || beforeHeight < 1 || _pixelWidth < 1 || _pixelHeight < 1)
        {
            return;
        }

        _beforeWidth = beforeWidth;
        _beforeHeight = beforeHeight;
        CompareBeforeImage.Source = before;
        CompareAfterImage.Source = PreviewImage.Source;
        _tool = AnnotationTool.None;
        _comparing = true;
        _comparePercent = 50;
        CompareLayer.Visibility = Visibility.Visible;
        PreviewImage.Opacity = 0;
        Notify(nameof(ComparePercent));
        Notify(nameof(IsComparing));
        Notify(nameof(ToolBarVisibility));
        Notify(nameof(CompareBarVisibility));
        Notify(nameof(ShowStrokeSize));
        Notify(nameof(StrokeSizeVisibility));
        UpdateCanvasHitTest();
        SizePreviewHost();
    }

    public void EndCompare()
    {
        if (!_comparing)
        {
            return;
        }

        ResetCompareVisual();
        UpdateCanvasHitTest();
        SizePreviewHost();
        RebuildMarks();
    }

    private void ResetCompareVisual()
    {
        if (!_comparing && CompareLayer.Visibility == Visibility.Collapsed)
        {
            return;
        }

        _comparing = false;
        _compareDrag = false;
        CompareBeforeImage.Source = null;
        CompareAfterImage.Source = null;
        CompareLayer.Visibility = Visibility.Collapsed;
        PreviewImage.Opacity = 1;
        Notify(nameof(IsComparing));
        Notify(nameof(ToolBarVisibility));
        Notify(nameof(CompareBarVisibility));
        Notify(nameof(StrokeSizeVisibility));
    }

    public byte[] Flatten(byte[] original)
    {
        var source = _sourceBytes is { Length: > 0 } ? _sourceBytes : original;
        return _marks.Count == 0 ? source : ScreenshotAnnotator.Flatten(source, _marks);
    }

    private float StrokeThickness => Math.Max(3f, _pixelWidth / 280f);

    private float TextSize => Math.Max(22f, _pixelWidth / 28f);

    private float HighlightThickness => Math.Max(8f, _pixelWidth / 140f) * _highlightSize;

    private float PenThickness => Math.Max(1.5f, _pixelWidth / 520f) * _penSize;

    private float EraserRadius => Math.Max(6f, _pixelWidth / 180f) * _eraserSize;

    private float StepDiameter => Math.Max(36f, _pixelWidth / 24f);

    private void ToolNone_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.None);

    private void ToolRead_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.ReadArea);

    private void ToolPen_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Pen);

    private void ToolHighlight_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Highlight);

    private void ToolEraser_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Eraser);

    private void ToolArrow_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Arrow);

    private void ToolRect_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Rectangle);

    private void ToolText_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Text);

    private void ToolStep_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Step);

    private void ToolBlur_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Blur);

    private void ToolRedact_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Redact);

    private void ToolCrop_Click(object sender, RoutedEventArgs e) => SetTool(AnnotationTool.Crop);

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!CanUndo)
        {
            return;
        }

        _historyIndex--;
        await RestoreSnapshotAsync(_history[_historyIndex]);
        NotifyEdit();
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRedo)
        {
            return;
        }

        _historyIndex++;
        await RestoreSnapshotAsync(_history[_historyIndex]);
        NotifyEdit();
    }

    private void ClearMarks_Click(object sender, RoutedEventArgs e)
    {
        if (_marks.Count == 0)
        {
            return;
        }

        _marks.Clear();
        _selectedText = null;
        PushHistory();
        RebuildMarks();
    }

    private void Color_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string hex } || hex.Length != 6)
        {
            return;
        }

        var value = Convert.ToInt32(hex, 16);
        _strokeColor = Color.FromArgb(
            255,
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    private void SetTool(AnnotationTool tool)
    {
        _tool = _tool == tool && tool != AnnotationTool.None ? AnnotationTool.None : tool;
        if (_tool != AnnotationTool.Eraser)
        {
            EraserCursor.Visibility = Visibility.Collapsed;
        }

        UpdateCanvasHitTest();
        CancelDraft();
        EndTextAction();
        Notify(nameof(ShowStrokeSize));
        Notify(nameof(StrokeSizeVisibility));
        Notify(nameof(StrokeSize));
        Notify(nameof(StrokeSizeLabel));
        if (_selectedText is not null)
        {
            _selectedText = null;
            RebuildMarks();
        }
    }

    private void PreviewScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SizePreviewHost();
        RebuildMarks();
    }

    private void PreviewScroll_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_comparing || _tool != AnnotationTool.None)
        {
            return;
        }

        if (_selectedText is not null)
        {
            _selectedText = null;
            RebuildMarks();
            return;
        }

        ClickZoom.HandleClick(PreviewScroll, e.GetPosition(PreviewScroll));
    }

    private void PreviewScroll_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!ClickZoom.ControlIsDown())
        {
            return;
        }

        e.Handled = true;
        ClickZoom.HandleWheel(PreviewScroll, e.GetCurrentPoint(PreviewScroll).Position, e.GetCurrentPoint(PreviewScroll).Properties.MouseWheelDelta);
    }

    private async void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!CanEdit || _textAction != TextAction.None)
        {
            return;
        }

        if (IsTextChrome(e.OriginalSource))
        {
            return;
        }

        if (_selectedText is not null)
        {
            _selectedText = null;
            RebuildMarks();
            if (_tool == AnnotationTool.Text)
            {
                return;
            }
        }

        var canvasPoint = e.GetCurrentPoint(AnnotationCanvas).Position;
        var imagePoint = ToImagePoint(canvasPoint);
        if (_tool == AnnotationTool.Text)
        {
            await AddTextAsync(imagePoint);
            return;
        }

        if (_tool == AnnotationTool.Step)
        {
            AddStep(imagePoint);
            return;
        }

        if (_tool == AnnotationTool.Eraser)
        {
            AnnotationCanvas.CapturePointer(e.Pointer);
            _drawing = true;
            _eraseChanged = false;
            UpdateEraserCursor(canvasPoint);
            if (EraseAt(imagePoint))
            {
                _eraseChanged = true;
                RebuildMarks();
                UpdateEraserCursor(canvasPoint);
            }

            return;
        }

        if (_tool == AnnotationTool.None)
        {
            return;
        }

        AnnotationCanvas.CapturePointer(e.Pointer);
        _drawing = true;
        _drawStart = imagePoint;
        CancelDraft();
        var brush = new SolidColorBrush(_strokeColor);
        var thickness = Math.Max(2, StrokeThickness * _previewScale);
        switch (_tool)
        {
            case AnnotationTool.Pen:
            case AnnotationTool.Highlight:
                _draftStroke = new Polyline
                {
                    Stroke = _tool == AnnotationTool.Highlight
                        ? new SolidColorBrush(Color.FromArgb(110, _strokeColor.R, _strokeColor.G, _strokeColor.B))
                        : brush,
                    StrokeThickness = _tool == AnnotationTool.Highlight ? HighlightThickness * _previewScale : PenThickness * _previewScale,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Points = { canvasPoint }
                };
                AnnotationCanvas.Children.Add(_draftStroke);
                break;
            case AnnotationTool.Arrow:
                _draftLine = new Line
                {
                    X1 = canvasPoint.X,
                    Y1 = canvasPoint.Y,
                    X2 = canvasPoint.X,
                    Y2 = canvasPoint.Y,
                    Stroke = brush,
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Triangle
                };
                AnnotationCanvas.Children.Add(_draftLine);
                break;
            case AnnotationTool.Rectangle:
            case AnnotationTool.Blur:
            case AnnotationTool.Redact:
            case AnnotationTool.Crop:
            case AnnotationTool.ReadArea:
                _draftRect = new Rectangle
                {
                    Stroke = brush,
                    StrokeThickness = _tool == AnnotationTool.Rectangle ? thickness : 2,
                    Fill = _tool switch
                    {
                        AnnotationTool.Blur => new SolidColorBrush(Color.FromArgb(90, 240, 240, 240)),
                        AnnotationTool.Redact => new SolidColorBrush(Color.FromArgb(255, _strokeColor.R, _strokeColor.G, _strokeColor.B)),
                        AnnotationTool.Crop => new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                        AnnotationTool.ReadArea => new SolidColorBrush(Color.FromArgb(50, 0, 120, 215)),
                        _ => new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
                    }
                };
                Canvas.SetLeft(_draftRect, canvasPoint.X);
                Canvas.SetTop(_draftRect, canvasPoint.Y);
                AnnotationCanvas.Children.Add(_draftRect);
                break;
        }
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_textAction != TextAction.None && _selectedText is not null)
        {
            MoveOrResizeText(e.GetCurrentPoint(AnnotationCanvas).Position);
            return;
        }

        var canvasPoint = e.GetCurrentPoint(AnnotationCanvas).Position;
        if (_tool == AnnotationTool.Eraser)
        {
            UpdateEraserCursor(canvasPoint);
            if (_drawing && EraseAt(ToImagePoint(canvasPoint)))
            {
                _eraseChanged = true;
                RebuildMarks();
                UpdateEraserCursor(canvasPoint);
            }

            return;
        }

        if (!_drawing)
        {
            return;
        }
        if (_draftStroke is not null)
        {
            _draftStroke.Points.Add(canvasPoint);
        }
        else if (_draftLine is not null)
        {
            _draftLine.X2 = canvasPoint.X;
            _draftLine.Y2 = canvasPoint.Y;
        }
        else if (_draftRect is not null)
        {
            var start = ToCanvasPoint(_drawStart);
            var x = Math.Min(start.X, canvasPoint.X);
            var y = Math.Min(start.Y, canvasPoint.Y);
            Canvas.SetLeft(_draftRect, x);
            Canvas.SetTop(_draftRect, y);
            _draftRect.Width = Math.Abs(canvasPoint.X - start.X);
            _draftRect.Height = Math.Abs(canvasPoint.Y - start.Y);
        }
    }

    private async void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_textAction != TextAction.None)
        {
            AnnotationCanvas.ReleasePointerCaptures();
            if (_textChanged)
            {
                PushHistory();
            }

            EndTextAction();
            RebuildMarks();
            return;
        }

        if (_tool == AnnotationTool.Eraser)
        {
            AnnotationCanvas.ReleasePointerCaptures();
            _drawing = false;
            if (_eraseChanged)
            {
                _eraseChanged = false;
                PushHistory();
            }

            return;
        }

        if (!_drawing)
        {
            return;
        }

        AnnotationCanvas.ReleasePointerCaptures();
        _drawing = false;
        var end = ToImagePoint(e.GetCurrentPoint(AnnotationCanvas).Position);
        ScreenshotMark? mark = null;
        if (_tool == AnnotationTool.Pen && _draftStroke is { Points.Count: >= 2 } stroke)
        {
            var pen = new PenMark { Color = _strokeColor, Thickness = PenThickness };
            foreach (var point in stroke.Points)
            {
                pen.Points.Add(ToImagePoint(point));
            }

            mark = pen;
        }
        else if (_tool == AnnotationTool.Highlight && _draftStroke is { Points.Count: >= 2 } marked)
        {
            var highlight = new HighlightMark { Color = _strokeColor, Thickness = HighlightThickness };
            foreach (var point in marked.Points)
            {
                highlight.Points.Add(ToImagePoint(point));
            }

            mark = highlight;
        }
        else if (_tool == AnnotationTool.Arrow)
        {
            mark = new ArrowMark { Color = _strokeColor, Thickness = StrokeThickness, Start = _drawStart, End = end };
        }
        else if (_tool == AnnotationTool.Rectangle)
        {
            mark = new RectMark { Color = _strokeColor, Thickness = StrokeThickness, Start = _drawStart, End = end };
        }
        else if (_tool == AnnotationTool.Blur)
        {
            var blurBounds = ScreenshotAnnotator.Bounds(_drawStart, end);
            if (blurBounds.Width >= 4 && blurBounds.Height >= 4)
            {
                mark = new BlurMark { Color = _strokeColor, Thickness = StrokeThickness, Start = _drawStart, End = end };
            }
        }
        else if (_tool == AnnotationTool.Redact)
        {
            var cover = ScreenshotAnnotator.Bounds(_drawStart, end);
            if (cover.Width >= 4 && cover.Height >= 4)
            {
                mark = new RedactMark
                {
                    Color = Color.FromArgb(255, _strokeColor.R, _strokeColor.G, _strokeColor.B),
                    Thickness = StrokeThickness,
                    Start = _drawStart,
                    End = end
                };
            }
        }
        else if (_tool == AnnotationTool.Crop)
        {
            CancelDraft();
            await ApplyCropAsync(ScreenshotAnnotator.Bounds(_drawStart, end));
            return;
        }
        else if (_tool == AnnotationTool.ReadArea)
        {
            CancelDraft();
            await ReadAreaAsync(ScreenshotAnnotator.Bounds(_drawStart, end));
            return;
        }

        CancelDraft();
        if (mark is null)
        {
            return;
        }

        _marks.Add(mark);
        PushHistory();
        RebuildMarks();
    }

    private async Task ReadAreaAsync(System.Drawing.Rectangle bounds)
    {
        if (_sourceBytes is not { Length: > 0 } || bounds.Width < 8 || bounds.Height < 8)
        {
            return;
        }

        try
        {
            var png = _marks.Count > 0 ? Flatten(_sourceBytes) : _sourceBytes;
            using var stream = new MemoryStream(png);
            using var image = new System.Drawing.Bitmap(stream);
            var rect = System.Drawing.Rectangle.Intersect(bounds, new System.Drawing.Rectangle(0, 0, image.Width, image.Height));
            if (rect.Width < 8 || rect.Height < 8)
            {
                return;
            }

            using var crop = image.Clone(rect, PixelFormat.Format32bppArgb);
            using var output = new MemoryStream();
            crop.Save(output, ImageFormat.Png);
            var text = await ScreenshotOcr.ReadAsync(output.ToArray());
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "No text was found in that area.";
            }

            var box = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 280
            };
            var dialog = new ContentDialog
            {
                Title = "Text in selection",
                Content = box,
                PrimaryButtonText = "Copy",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && text != "No text was found in that area.")
            {
                ClipboardHelper.CopyText(text);
            }
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Text in selection",
                Content = ex.Message,
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private async Task AddTextAsync(DrawingPoint position)
    {
        var box = new TextBox { PlaceholderText = "Text on image" };
        var dialog = new ContentDialog
        {
            Title = "Add text",
            Content = box,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        var mark = new TextMark
        {
            Color = _strokeColor,
            Thickness = StrokeThickness,
            Position = position,
            Text = box.Text.Trim(),
            FontSize = TextSize
        };
        _marks.Add(mark);
        PushHistory();
        _selectedText = mark;
        RebuildMarks();
    }

    private void SizePreviewHost()
    {
        if (PreviewScroll.ViewportWidth <= 0 || PreviewScroll.ViewportHeight <= 0 || _pixelWidth <= 0 || _pixelHeight <= 0)
        {
            return;
        }

        var fitWidth = _comparing ? Math.Max(_pixelWidth, _beforeWidth) : _pixelWidth;
        var fitHeight = _comparing ? Math.Max(_pixelHeight, _beforeHeight) : _pixelHeight;
        _previewScale = Math.Min(PreviewScroll.ViewportWidth / fitWidth, PreviewScroll.ViewportHeight / fitHeight);
        _previewScale = Math.Max(_previewScale, 0.05);
        PreviewHost.Width = fitWidth * _previewScale;
        PreviewHost.Height = fitHeight * _previewScale;
        AnnotationCanvas.Width = PreviewHost.Width;
        AnnotationCanvas.Height = PreviewHost.Height;
        CursorCanvas.Width = PreviewHost.Width;
        CursorCanvas.Height = PreviewHost.Height;
        HighlightCanvas.Width = PreviewHost.Width;
        HighlightCanvas.Height = PreviewHost.Height;
        UpdateCanvasHitTest();
        DrawSearchHighlights();
        LayoutCompare();
    }

    private void LayoutCompare()
    {
        if (!_comparing || PreviewHost.Width <= 0 || PreviewHost.Height <= 0)
        {
            return;
        }

        var hostW = PreviewHost.Width;
        var hostH = PreviewHost.Height;
        PlaceCompareImage(CompareAfterCanvas, CompareAfterImage, _pixelWidth, _pixelHeight, hostW, hostH);
        PlaceCompareImage(CompareBeforeCanvas, CompareBeforeImage, _beforeWidth, _beforeHeight, hostW, hostH);
        var split = hostW * (_comparePercent / 100.0);
        CompareBeforeHost.Clip = new RectangleGeometry { Rect = new Rect(0, 0, Math.Max(0, split), hostH) };
        CompareDivider.Height = hostH;
        Canvas.SetLeft(CompareDivider, Math.Clamp(split - 1, 0, Math.Max(0, hostW - 2)));
        Canvas.SetLeft(CompareHandle, Math.Clamp(split - 18, 0, Math.Max(0, hostW - 36)));
        Canvas.SetTop(CompareHandle, Math.Max(0, (hostH - 36) / 2));
    }

    private void PlaceCompareImage(Canvas canvas, Image image, int pixelWidth, int pixelHeight, double hostW, double hostH)
    {
        image.Width = Math.Max(1, pixelWidth * _previewScale);
        image.Height = Math.Max(1, pixelHeight * _previewScale);
        Canvas.SetLeft(image, (hostW - image.Width) / 2);
        Canvas.SetTop(image, (hostH - image.Height) / 2);
        canvas.Width = hostW;
        canvas.Height = hostH;
    }

    private void Compare_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        CompareLayer.CapturePointer(e.Pointer);
        _compareDrag = true;
        e.Handled = true;
        MoveCompare(e);
    }

    private void Compare_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_compareDrag)
        {
            return;
        }

        e.Handled = true;
        MoveCompare(e);
    }

    private void Compare_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _compareDrag = false;
        CompareLayer.ReleasePointerCaptures();
        e.Handled = true;
    }

    private void MoveCompare(PointerRoutedEventArgs e)
    {
        var width = Math.Max(1, CompareLayer.ActualWidth);
        if (CompareLayer.ActualWidth <= 1)
        {
            width = Math.Max(1, PreviewHost.Width);
        }

        ComparePercent = e.GetCurrentPoint(CompareLayer).Position.X / width * 100;
    }

    private void DrawSearchHighlights()
    {
        HighlightCanvas.Children.Clear();
        foreach (var box in _searchHighlights)
        {
            var mark = new Rectangle
            {
                Width = Math.Max(1, box.Width * _previewScale),
                Height = Math.Max(1, box.Height * _previewScale),
                Fill = new SolidColorBrush(Color.FromArgb(110, 255, 214, 0)),
                Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 180, 0)),
                StrokeThickness = 2,
                IsHitTestVisible = false,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(mark, box.X * _previewScale);
            Canvas.SetTop(mark, box.Y * _previewScale);
            HighlightCanvas.Children.Add(mark);
        }
    }

    private void UpdateCanvasHitTest()
    {
        AnnotationCanvas.IsHitTestVisible = CanEdit && !_comparing;
        AnnotationCanvas.Background = _tool == AnnotationTool.None
            ? null
            : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }

    private void RebuildMarks()
    {
        CancelDraft();
        EndTextAction();
        if (_selectedText is not null && !_marks.Contains(_selectedText))
        {
            _selectedText = null;
        }

        _selectedHost = null;
        _selectedBlock = null;
        AnnotationCanvas.Children.Clear();
        foreach (var mark in _marks)
        {
            AddVisual(mark);
        }
    }

    private void AddVisual(ScreenshotMark mark)
    {
        var brush = new SolidColorBrush(mark.Color);
        var thickness = Math.Max(2, mark.Thickness * _previewScale);
        switch (mark)
        {
            case PenMark stroke when stroke.Points.Count >= 2:
                AnnotationCanvas.Children.Add(StrokeVisual(stroke.Points, brush, thickness));
                break;
            case HighlightMark highlight when highlight.Points.Count >= 2:
                AnnotationCanvas.Children.Add(StrokeVisual(
                    highlight.Points,
                    new SolidColorBrush(Color.FromArgb(110, mark.Color.R, mark.Color.G, mark.Color.B)),
                    Math.Max(8, highlight.Thickness * _previewScale)));
                break;
            case StepMark step:
                AddStepVisual(step, brush);
                break;
            case ArrowMark arrow:
                var start = ToCanvasPoint(arrow.Start);
                var end = ToCanvasPoint(arrow.End);
                AnnotationCanvas.Children.Add(new Line
                {
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = end.X,
                    Y2 = end.Y,
                    Stroke = brush,
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Triangle
                });
                break;
            case RectMark rect:
                var bounds = ScreenshotAnnotator.Bounds(rect.Start, rect.End);
                var shape = new Rectangle
                {
                    Width = bounds.Width * _previewScale,
                    Height = bounds.Height * _previewScale,
                    Stroke = brush,
                    StrokeThickness = thickness
                };
                Canvas.SetLeft(shape, bounds.X * _previewScale);
                Canvas.SetTop(shape, bounds.Y * _previewScale);
                AnnotationCanvas.Children.Add(shape);
                break;
            case BlurMark blur:
                AddBlurVisual(blur);
                break;
            case RedactMark redact:
                var cover = ScreenshotAnnotator.Bounds(redact.Start, redact.End);
                var block = new Rectangle
                {
                    Width = cover.Width * _previewScale,
                    Height = cover.Height * _previewScale,
                    Fill = new SolidColorBrush(Color.FromArgb(255, mark.Color.R, mark.Color.G, mark.Color.B)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(block, cover.X * _previewScale);
                Canvas.SetTop(block, cover.Y * _previewScale);
                AnnotationCanvas.Children.Add(block);
                break;
            case TextMark text:
                AddTextVisual(text, brush);
                break;
        }
    }

    private void CancelDraft()
    {
        if (_draftStroke is not null)
        {
            AnnotationCanvas.Children.Remove(_draftStroke);
        }

        if (_draftLine is not null)
        {
            AnnotationCanvas.Children.Remove(_draftLine);
        }

        if (_draftRect is not null)
        {
            AnnotationCanvas.Children.Remove(_draftRect);
        }

        _draftStroke = null;
        _draftLine = null;
        _draftRect = null;
    }

    private async Task ApplyCropAsync(System.Drawing.Rectangle crop)
    {
        if (_sourceBytes is not { Length: > 0 } bytes)
        {
            return;
        }

        ScreenshotAnnotator.CroppedImage cropped;
        try
        {
            var shown = _marks.Count == 0 ? bytes : ScreenshotAnnotator.Flatten(bytes, _marks);
            cropped = ScreenshotAnnotator.Crop(shown, crop);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Trim image?",
            Content = "Keep only the selected area. Anything outside it is removed.",
            PrimaryButtonText = "Trim",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _plate = new ImagePlate { Png = cropped.Png, Width = cropped.Width, Height = cropped.Height };
        _sourceBytes = cropped.Png;
        _pixelWidth = cropped.Width;
        _pixelHeight = cropped.Height;
        ReplaceSourceBitmap(cropped.Png);
        _minNextStep = NextStepNumber();
        _marks.Clear();
        _selectedText = null;
        _tool = AnnotationTool.None;
        UpdateCanvasHitTest();
        PreviewImage.Source = await ImageLoader.LoadAsync(cropped.Png);
        PreviewScroll.ChangeView(0, 0, 1, disableAnimation: true);
        SizePreviewHost();
        RebuildMarks();
        ImageChanged?.Invoke(this, new MarkupImageEventArgs(cropped.Png, cropped.Width, cropped.Height));
        PushHistory();
    }

    private void ReplaceSourceBitmap(byte[] pngBytes)
    {
        _sourceBitmap?.Dispose();
        using var stream = new MemoryStream(pngBytes, writable: false);
        using var loaded = new System.Drawing.Bitmap(stream);
        _sourceBitmap = new System.Drawing.Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(_sourceBitmap);
        graphics.DrawImage(loaded, 0, 0, _sourceBitmap.Width, _sourceBitmap.Height);
    }

    private int NextStepNumber()
    {
        var number = Math.Max(1, _minNextStep);
        foreach (var existing in _marks)
        {
            if (existing is StepMark step)
            {
                number = Math.Max(number, step.Number + 1);
            }
        }

        return number;
    }

    private void Canvas_PointerExited(object sender, PointerRoutedEventArgs e)
        => EraserCursor.Visibility = Visibility.Collapsed;

    private void UpdateEraserCursor(Point canvasPoint)
    {
        if (_tool != AnnotationTool.Eraser)
        {
            EraserCursor.Visibility = Visibility.Collapsed;
            return;
        }

        var diameter = Math.Max(8, EraserRadius * 2 * _previewScale);
        EraserCursor.Width = diameter;
        EraserCursor.Height = diameter;
        Canvas.SetLeft(EraserCursor, canvasPoint.X - (diameter / 2));
        Canvas.SetTop(EraserCursor, canvasPoint.Y - (diameter / 2));
        EraserCursor.Visibility = Visibility.Visible;
    }

    private bool EraseAt(DrawingPoint center)
    {
        var radius = EraserRadius;
        var next = new List<ScreenshotMark>();
        var changed = false;
        foreach (var mark in _marks)
        {
            switch (mark)
            {
                case PenMark pen:
                    changed |= KeepStroke(pen.Points, pen.Color, pen.Thickness, center, radius, false, next);
                    break;
                case HighlightMark highlight:
                    changed |= KeepStroke(highlight.Points, highlight.Color, highlight.Thickness, center, radius, true, next);
                    break;
                case StepMark step when Distance(step.Center, center) <= radius + (step.Diameter / 2f):
                    changed = true;
                    break;
                case TextMark text when Distance(text.Position, center) <= radius + text.FontSize:
                    changed = true;
                    break;
                case ArrowMark arrow when DistanceToSegment(center, arrow.Start, arrow.End) <= radius + arrow.Thickness:
                    changed = true;
                    break;
                case RectMark rect when CircleHits(center, ScreenshotAnnotator.Bounds(rect.Start, rect.End), radius):
                    changed = true;
                    break;
                case BlurMark blur when CircleHits(center, ScreenshotAnnotator.Bounds(blur.Start, blur.End), radius):
                    changed = true;
                    break;
                case RedactMark redact when CircleHits(center, ScreenshotAnnotator.Bounds(redact.Start, redact.End), radius):
                    changed = true;
                    break;
                default:
                    next.Add(mark);
                    break;
            }
        }

        if (!changed)
        {
            return false;
        }

        _marks.Clear();
        foreach (var mark in next)
        {
            _marks.Add(mark);
        }

        return true;
    }

    private static bool KeepStroke(
        IReadOnlyList<DrawingPoint> points,
        Windows.UI.Color color,
        float thickness,
        DrawingPoint center,
        float radius,
        bool highlight,
        List<ScreenshotMark> destination)
    {
        var reach = radius + (thickness / 2f);
        var runs = SplitStroke(points, center, reach);
        var changed = runs.Count != 1 || runs.Sum(run => run.Count) != points.Count;

        foreach (var run in runs)
        {
            if (highlight)
            {
                var mark = new HighlightMark { Color = color, Thickness = thickness };
                mark.Points.AddRange(run);
                destination.Add(mark);
            }
            else
            {
                var mark = new PenMark { Color = color, Thickness = thickness };
                mark.Points.AddRange(run);
                destination.Add(mark);
            }
        }

        return changed || runs.Count == 0;
    }

    private static List<List<DrawingPoint>> SplitStroke(IReadOnlyList<DrawingPoint> points, DrawingPoint center, float radius)
    {
        var runs = new List<List<DrawingPoint>>();
        var run = new List<DrawingPoint>();
        foreach (var point in points)
        {
            if (Distance(point, center) <= radius || (run.Count > 0 && DistanceToSegment(center, run[^1], point) <= radius))
            {
                if (run.Count >= 2)
                {
                    runs.Add(run.ToList());
                }

                run.Clear();
                continue;
            }

            run.Add(point);
        }

        if (run.Count >= 2)
        {
            runs.Add(run);
        }

        return runs;
    }

    private static bool CircleHits(DrawingPoint point, System.Drawing.Rectangle rect, float radius)
    {
        var closestX = Math.Clamp(point.X, rect.Left, rect.Right);
        var closestY = Math.Clamp(point.Y, rect.Top, rect.Bottom);
        var dx = point.X - closestX;
        var dy = point.Y - closestY;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static float Distance(DrawingPoint a, DrawingPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static float DistanceToSegment(DrawingPoint point, DrawingPoint start, DrawingPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = (dx * dx) + (dy * dy);
        if (length <= 0.01f)
        {
            return Distance(point, start);
        }

        var t = Math.Clamp(((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / (double)length), 0, 1);
        var closest = new DrawingPoint((int)Math.Round(start.X + (t * dx)), (int)Math.Round(start.Y + (t * dy)));
        return Distance(point, closest);
    }

    private void AddStep(DrawingPoint center)
    {
        var number = NextStepNumber();

        _marks.Add(new StepMark
        {
            Color = _strokeColor,
            Thickness = StrokeThickness,
            Center = center,
            Number = number,
            Diameter = StepDiameter
        });
        PushHistory();
        RebuildMarks();
    }

    private Polyline StrokeVisual(IEnumerable<DrawingPoint> points, Brush stroke, double thickness)
    {
        var line = new Polyline
        {
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        foreach (var point in points)
        {
            line.Points.Add(ToCanvasPoint(point));
        }

        return line;
    }

    private void AddStepVisual(StepMark step, SolidColorBrush brush)
    {
        var diameter = Math.Max(22, step.Diameter * _previewScale);
        var at = ToCanvasPoint(step.Center);
        var circle = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = brush,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(circle, at.X - (diameter / 2));
        Canvas.SetTop(circle, at.Y - (diameter / 2));
        AnnotationCanvas.Children.Add(circle);

        var luminance = (0.299 * step.Color.R) + (0.587 * step.Color.G) + (0.114 * step.Color.B);
        var label = new TextBlock
        {
            Text = step.Number.ToString(),
            Width = diameter,
            FontSize = Math.Max(12, diameter * 0.46),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(luminance > 160 ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255)),
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, at.X - (diameter / 2));
        Canvas.SetTop(label, at.Y - (diameter * 0.34));
        AnnotationCanvas.Children.Add(label);
    }

    private void AddBlurVisual(BlurMark blur)
    {
        var bounds = ScreenshotAnnotator.Bounds(blur.Start, blur.End);
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        var image = new Image
        {
            Width = bounds.Width * _previewScale,
            Height = bounds.Height * _previewScale,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        if (_sourceBitmap is not null)
        {
            using var composite = new System.Drawing.Bitmap(
                _sourceBitmap.Width,
                _sourceBitmap.Height,
                PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(composite))
            {
                graphics.DrawImage(_sourceBitmap, 0, 0, composite.Width, composite.Height);
            }

            var prior = new List<ScreenshotMark>();
            foreach (var mark in _marks)
            {
                if (ReferenceEquals(mark, blur))
                {
                    break;
                }

                prior.Add(mark);
            }

            ScreenshotAnnotator.ApplyMarks(composite, prior);
            using var patch = ScreenshotAnnotator.CreateBlurredPatch(composite, bounds);
            image.Source = ToBitmapImage(patch);
        }
        else
        {
            image.Opacity = 0.85;
        }

        Canvas.SetLeft(image, bounds.X * _previewScale);
        Canvas.SetTop(image, bounds.Y * _previewScale);
        AnnotationCanvas.Children.Add(image);
    }

    private static BitmapImage ToBitmapImage(System.Drawing.Bitmap bitmap)
    {
        using var buffer = new MemoryStream();
        bitmap.Save(buffer, ImageFormat.Png);
        var bytes = buffer.ToArray();
        var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        }

        stream.Seek(0);
        var image = new BitmapImage();
        image.SetSource(stream);
        return image;
    }

    private void AddTextVisual(TextMark text, SolidColorBrush brush)
    {
        var at = ToCanvasPoint(text.Position);
        var block = new TextBlock
        {
            Text = text.Text,
            Foreground = brush,
            FontSize = Math.Max(12, text.FontSize * _previewScale),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Tag = text,
            IsHitTestVisible = CanEdit
        };
        block.PointerPressed += Text_Pressed;
        if (!ReferenceEquals(_selectedText, text) || !CanEdit)
        {
            Canvas.SetLeft(block, at.X);
            Canvas.SetTop(block, at.Y);
            AnnotationCanvas.Children.Add(block);
            return;
        }

        var host = new Canvas { Tag = text };
        Canvas.SetLeft(host, at.X);
        Canvas.SetTop(host, at.Y);
        host.Children.Add(block);
        AnnotationCanvas.Children.Add(host);
        _selectedHost = host;
        _selectedBlock = block;
        block.SizeChanged += (_, _) => LayoutSelectionChrome(host, block, text);
        block.Loaded += (_, _) => LayoutSelectionChrome(host, block, text);
    }

    private void LayoutSelectionChrome(Canvas host, TextBlock block, TextMark mark)
    {
        for (var index = host.Children.Count - 1; index >= 0; index--)
        {
            if (!ReferenceEquals(host.Children[index], block))
            {
                host.Children.RemoveAt(index);
            }
        }

        var width = Math.Max(8, block.ActualWidth);
        var height = Math.Max(8, block.ActualHeight);
        const double pad = 4;
        const double hit = 8;
        host.Width = width;
        host.Height = height;
        var border = new Rectangle
        {
            Width = width + (pad * 2),
            Height = height + (pad * 2),
            Stroke = new SolidColorBrush(Color.FromArgb(255, 76, 194, 255)),
            StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromArgb(20, 76, 194, 255)),
            Tag = mark,
            IsHitTestVisible = true
        };
        Canvas.SetLeft(border, -pad);
        Canvas.SetTop(border, -pad);
        border.PointerPressed += (_, e) =>
        {
            if (_selectedBlock is null)
            {
                return;
            }

            e.Handled = true;
            BeginTextAction(mark, _selectedBlock, TextAction.Drag, e);
        };
        host.Children.Insert(0, border);
        AddResizeEdge(host, mark, ResizeEdge.Left, -hit, 0, hit, height);
        AddResizeEdge(host, mark, ResizeEdge.Right, width, 0, hit, height);
        AddResizeEdge(host, mark, ResizeEdge.Top, 0, -hit, width, hit);
        AddResizeEdge(host, mark, ResizeEdge.Bottom, 0, height, width, hit);
        AddResizeEdge(host, mark, ResizeEdge.TopLeft, -hit, -hit, hit, hit);
        AddResizeEdge(host, mark, ResizeEdge.TopRight, width, -hit, hit, hit);
        AddResizeEdge(host, mark, ResizeEdge.BottomLeft, -hit, height, hit, hit);
        AddResizeEdge(host, mark, ResizeEdge.BottomRight, width, height, hit, hit);
    }

    private void AddResizeEdge(Canvas host, TextMark mark, ResizeEdge edge, double x, double y, double width, double height)
    {
        var hit = new Rectangle
        {
            Width = Math.Max(8, width),
            Height = Math.Max(8, height),
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Tag = edge,
            IsHitTestVisible = true
        };
        Canvas.SetLeft(hit, x);
        Canvas.SetTop(hit, y);
        hit.PointerPressed += (_, e) => ResizeEdgePressed(mark, edge, e);
        host.Children.Add(hit);
    }

    private void Text_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (!CanEdit || sender is not TextBlock { Tag: TextMark mark } block)
        {
            return;
        }

        e.Handled = true;
        if (!ReferenceEquals(_selectedText, mark))
        {
            _selectedText = mark;
            RebuildMarks();
            return;
        }

        BeginTextAction(mark, block, TextAction.Drag, e);
    }

    private void ResizeEdgePressed(TextMark mark, ResizeEdge edge, PointerRoutedEventArgs e)
    {
        if (!CanEdit || _selectedBlock is null)
        {
            return;
        }

        e.Handled = true;
        _resizeEdge = edge;
        BeginTextAction(mark, _selectedBlock, TextAction.Resize, e);
        _textFontStart = mark.FontSize;
        _startWidth = Math.Max(8, _selectedBlock.ActualWidth);
        _startHeight = Math.Max(8, _selectedBlock.ActualHeight);
        _startLeft = Canvas.GetLeft(_selectedHost!);
        _startTop = Canvas.GetTop(_selectedHost!);
    }

    private void BeginTextAction(TextMark mark, TextBlock block, TextAction action, PointerRoutedEventArgs e)
    {
        AnnotationCanvas.CapturePointer(e.Pointer);
        _selectedText = mark;
        _selectedBlock = block;
        _textAction = action;
        _textPointerStart = e.GetCurrentPoint(AnnotationCanvas).Position;
        _textOriginStart = mark.Position;
        _textChanged = false;
        _drawing = false;
        CancelDraft();
    }

    private void MoveOrResizeText(Point canvasPoint)
    {
        if (_selectedText is null || _selectedBlock is null || _selectedHost is null)
        {
            return;
        }

        if (_textAction == TextAction.Drag)
        {
            var dx = (canvasPoint.X - _textPointerStart.X) / Math.Max(_previewScale, 0.01);
            var dy = (canvasPoint.Y - _textPointerStart.Y) / Math.Max(_previewScale, 0.01);
            _selectedText.Position = ClampImagePoint(_textOriginStart.X + dx, _textOriginStart.Y + dy);
            var at = ToCanvasPoint(_selectedText.Position);
            Canvas.SetLeft(_selectedHost, at.X);
            Canvas.SetTop(_selectedHost, at.Y);
            _textChanged = true;
            return;
        }

        var deltaX = canvasPoint.X - _textPointerStart.X;
        var deltaY = canvasPoint.Y - _textPointerStart.Y;
        var scaleX = _startWidth;
        var scaleY = _startHeight;
        switch (_resizeEdge)
        {
            case ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight:
                scaleX = Math.Max(8, _startWidth + deltaX);
                break;
            case ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft:
                scaleX = Math.Max(8, _startWidth - deltaX);
                break;
        }

        switch (_resizeEdge)
        {
            case ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight:
                scaleY = Math.Max(8, _startHeight + deltaY);
                break;
            case ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight:
                scaleY = Math.Max(8, _startHeight - deltaY);
                break;
        }

        var scale = Math.Max(scaleX / Math.Max(8, _startWidth), scaleY / Math.Max(8, _startHeight));
        var nextFont = Math.Clamp(_textFontStart * (float)scale, 12f, Math.Max(24f, _pixelHeight * 0.8f));
        var left = HasLeft(_resizeEdge) ? _startLeft + _startWidth - (_startWidth * scale) : _startLeft;
        var top = HasTop(_resizeEdge) ? _startTop + _startHeight - (_startHeight * scale) : _startTop;
        _selectedText.FontSize = nextFont;
        _selectedText.Position = ClampImagePoint(left / Math.Max(_previewScale, 0.01), top / Math.Max(_previewScale, 0.01));
        _selectedBlock.FontSize = Math.Max(12, nextFont * _previewScale);
        Canvas.SetLeft(_selectedHost, left);
        Canvas.SetTop(_selectedHost, top);
        _textChanged = true;
    }

    private DrawingPoint ClampImagePoint(double x, double y)
        => new(
            (int)Math.Clamp(Math.Round(x), 0, Math.Max(0, _pixelWidth - 1)),
            (int)Math.Clamp(Math.Round(y), 0, Math.Max(0, _pixelHeight - 1)));

    private void EndTextAction()
    {
        _textAction = TextAction.None;
        _resizeEdge = ResizeEdge.None;
        _textChanged = false;
    }

    private static bool HasLeft(ResizeEdge edge)
        => edge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft;

    private static bool HasTop(ResizeEdge edge)
        => edge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight;

    private static bool IsTextChrome(object source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Tag: TextMark or ResizeEdge })
            {
                return true;
            }
        }

        return false;
    }

    private DrawingPoint ToImagePoint(Point canvas)
        => new(
            (int)Math.Clamp(Math.Round(canvas.X / Math.Max(_previewScale, 0.01)), 0, Math.Max(0, _pixelWidth - 1)),
            (int)Math.Clamp(Math.Round(canvas.Y / Math.Max(_previewScale, 0.01)), 0, Math.Max(0, _pixelHeight - 1)));

    private Point ToCanvasPoint(DrawingPoint image)
        => new(image.X * _previewScale, image.Y * _previewScale);

    private void ResetHistory()
    {
        _history.Clear();
        _plate ??= new ImagePlate { Png = [], Width = 0, Height = 0 };
        _history.Add(new EditorSnapshot { Image = _plate, Marks = CloneMarks(), MinNextStep = _minNextStep });
        _historyIndex = 0;
    }

    private void PushHistory()
    {
        if (_plate is null)
        {
            return;
        }

        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        _history.Add(new EditorSnapshot { Image = _plate, Marks = CloneMarks(), MinNextStep = _minNextStep });
        _historyIndex = _history.Count - 1;
        NotifyEdit();
    }

    private async Task RestoreSnapshotAsync(EditorSnapshot snapshot)
    {
        _marks.Clear();
        _minNextStep = Math.Max(1, snapshot.MinNextStep);
        foreach (var mark in snapshot.Marks)
        {
            _marks.Add(mark.Clone());
        }

        _selectedText = null;
        if (ReferenceEquals(_plate, snapshot.Image))
        {
            RebuildMarks();
            return;
        }

        _plate = snapshot.Image;
        _sourceBytes = snapshot.Image.Png.Length > 0 ? snapshot.Image.Png : null;
        _pixelWidth = snapshot.Image.Width;
        _pixelHeight = snapshot.Image.Height;
        if (_sourceBytes is null)
        {
            _sourceBitmap?.Dispose();
            _sourceBitmap = null;
            PreviewImage.Source = null;
        }
        else
        {
            ReplaceSourceBitmap(_sourceBytes);
            PreviewImage.Source = await ImageLoader.LoadAsync(_sourceBytes);
        }

        PreviewScroll.ChangeView(0, 0, 1, disableAnimation: true);
        SizePreviewHost();
        RebuildMarks();
        if (_sourceBytes is not null)
        {
            ImageChanged?.Invoke(this, new MarkupImageEventArgs(_sourceBytes, _pixelWidth, _pixelHeight));
        }
    }

    private sealed class ImagePlate
    {
        public required byte[] Png { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }
    }

    private sealed class EditorSnapshot
    {
        public required ImagePlate Image { get; init; }

        public required List<ScreenshotMark> Marks { get; init; }

        public int MinNextStep { get; init; } = 1;
    }

    private List<ScreenshotMark> CloneMarks()
        => _marks.Select(mark => mark.Clone()).ToList();

    private void NotifyEdit()
    {
        Notify(nameof(HasMarks));
        Notify(nameof(HasEdits));
        Notify(nameof(CanUndo));
        Notify(nameof(CanRedo));
        Notify(nameof(CanClearMarks));
        MarksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class MarkupImageEventArgs : EventArgs
{
    public MarkupImageEventArgs(byte[] pngBytes, int width, int height)
    {
        PngBytes = pngBytes;
        Width = width;
        Height = height;
    }

    public byte[] PngBytes { get; }

    public int Width { get; }

    public int Height { get; }
}
