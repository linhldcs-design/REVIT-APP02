using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RevitAPP.Core.Services;
using RevitAPP.ViewModels;
// Autodesk.Revit.DB defines its own Point and Line; this file is pure WPF drawing.
using Line = System.Windows.Shapes.Line;
using Point = System.Windows.Point;

namespace RevitAPP.Views;

public partial class CadGridReviewWindow : Window
{
    /// <summary>Drawing area at zoom 1, in device-independent pixels.</summary>
    private const double BaseCanvasSize = 900.0;

    private const double CanvasPadding = 40.0;

    private readonly CadGridReviewViewModel _viewModel;
    private Point _dragOrigin;
    private bool _isDragging;

    public CadGridReviewWindow(CadGridReviewViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.CloseRequested += (_, confirmed) =>
        {
            DialogResult = confirmed;
            Close();
        };
        viewModel.RenderRequested += (_, _) => Render();

        Loaded += (_, _) => Render();
        PreviewScroll.PreviewMouseWheel += OnPreviewMouseWheel;
        // Capture on the canvas: the ScrollViewer forwards clicks to its content, so
        // handlers attached to the viewer itself never see a drag over the drawing.
        PreviewCanvas.MouseLeftButtonDown += OnDragStart;
        PreviewCanvas.MouseMove += OnDragMove;
        PreviewCanvas.MouseLeftButtonUp += OnDragEnd;
    }

    /// <summary>
    /// Redraws the axes. The CAD extents are fitted into a fixed square and then scaled by
    /// the zoom factor, so aspect ratio survives and zooming stays independent of the
    /// drawing's real-world size.
    /// </summary>
    private void Render()
    {
        PreviewCanvas.Children.Clear();

        var preview = _viewModel.Preview;
        var extent = Math.Max(Math.Max(preview.WidthMm, preview.HeightMm), 1.0);
        var canvasSize = BaseCanvasSize * _viewModel.Zoom;
        var scale = (canvasSize - 2 * CanvasPadding) / extent;

        PreviewCanvas.Width = canvasSize;
        PreviewCanvas.Height = canvasSize;

        foreach (var axis in _viewModel.Axes)
        {
            var isSkew = axis.IsSkew;
            var selected = axis.IsSelected;

            var line = new Line
            {
                X1 = CanvasPadding + axis.Axis.Start.Xmm * scale,
                X2 = CanvasPadding + axis.Axis.End.Xmm * scale,
                // Canvas Y grows downward; CAD Y grows upward.
                Y1 = canvasSize - CanvasPadding - axis.Axis.Start.Ymm * scale,
                Y2 = canvasSize - CanvasPadding - axis.Axis.End.Ymm * scale,
                StrokeThickness = selected ? 1.6 : 1.0,
                Stroke = BrushFor(selected, isSkew),
                StrokeDashArray = selected ? null : new DoubleCollection { 4, 3 },
                Opacity = selected ? 1.0 : 0.45
            };

            PreviewCanvas.Children.Add(line);
            AddLabel(axis, line, selected, isSkew);
        }
    }

    private void AddLabel(CadGridAxisViewModel axis, Line line, bool selected, bool isSkew)
    {
        var label = new TextBlock
        {
            Text = axis.Name,
            FontSize = 11,
            Foreground = BrushFor(selected, isSkew),
            Opacity = selected ? 1.0 : 0.5
        };

        // Place the tag just beyond the end that sits highest on screen, matching how
        // Revit draws grid heads.
        var atStart = line.Y1 <= line.Y2;
        Canvas.SetLeft(label, (atStart ? line.X1 : line.X2) - 6);
        Canvas.SetTop(label, (atStart ? line.Y1 : line.Y2) - 18);
        PreviewCanvas.Children.Add(label);
    }

    private Brush BrushFor(bool selected, bool isSkew)
    {
        if (!selected) return (Brush)FindResource("Brush.TextSecondary");
        return isSkew
            ? (Brush)FindResource("Brush.Danger")
            : (Brush)FindResource("Brush.Accent");
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Keep whatever sits under the cursor under the cursor after zooming, rather than
        // letting the drawing slide away toward the canvas origin.
        var beforeZoom = _viewModel.Zoom;
        var cursor = e.GetPosition(PreviewCanvas);

        if (e.Delta > 0) _viewModel.ZoomInCommand.Execute(null);
        else _viewModel.ZoomOutCommand.Execute(null);

        var ratio = _viewModel.Zoom / beforeZoom;
        if (Math.Abs(ratio - 1.0) > 1e-9)
        {
            var viewer = e.GetPosition(PreviewScroll);
            PreviewScroll.ScrollToHorizontalOffset(cursor.X * ratio - viewer.X);
            PreviewScroll.ScrollToVerticalOffset(cursor.Y * ratio - viewer.Y);
        }

        e.Handled = true;
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        // Measure against the viewer, not the canvas: the canvas moves as we scroll, so
        // canvas-relative deltas would feed back on themselves and jitter.
        _dragOrigin = e.GetPosition(PreviewScroll);
        _isDragging = true;
        PreviewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var current = e.GetPosition(PreviewScroll);
        PreviewScroll.ScrollToHorizontalOffset(
            PreviewScroll.HorizontalOffset - (current.X - _dragOrigin.X));
        PreviewScroll.ScrollToVerticalOffset(
            PreviewScroll.VerticalOffset - (current.Y - _dragOrigin.Y));
        _dragOrigin = current;
        e.Handled = true;
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        PreviewCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }
}
