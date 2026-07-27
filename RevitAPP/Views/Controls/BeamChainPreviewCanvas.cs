using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;
using WpfFormattedText = System.Windows.Media.FormattedText;
using WpfPoint = System.Windows.Point;

namespace RevitAPP.Views.Controls;

public sealed class BeamChainPreviewCanvas : FrameworkElement
{
    private const double MinimumZoom = 0.35;
    private const double MaximumZoom = 8.0;
    private double _zoom = 1.0;
    private Vector _pan;
    private WpfPoint? _dragStart;
    private Vector _panAtDragStart;

    public static readonly RoutedUICommand FitCommand = new("Fit preview", nameof(FitCommand), typeof(BeamChainPreviewCanvas));
    public static readonly RoutedUICommand ZoomInCommand = new("Zoom in", nameof(ZoomInCommand), typeof(BeamChainPreviewCanvas));
    public static readonly RoutedUICommand ZoomOutCommand = new("Zoom out", nameof(ZoomOutCommand), typeof(BeamChainPreviewCanvas));

    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
        nameof(Preview), typeof(BeamChainPreviewModel), typeof(BeamChainPreviewCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPreviewChanged));

    public BeamChainPreviewCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        CommandBindings.Add(new CommandBinding(FitCommand, (_, _) => ResetView()));
        CommandBindings.Add(new CommandBinding(ZoomInCommand, (_, _) => ZoomAt(new WpfPoint(ActualWidth / 2, ActualHeight / 2), 1.2)));
        CommandBindings.Add(new CommandBinding(ZoomOutCommand, (_, _) => ZoomAt(new WpfPoint(ActualWidth / 2, ActualHeight / 2), 1 / 1.2)));
    }

    public BeamChainPreviewModel? Preview
    {
        get => (BeamChainPreviewModel?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            _dragStart = e.GetPosition(this);
            _panAtDragStart = _pan;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart == null || e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed) return;
        _pan = _panAtDragStart + (e.GetPosition(this) - _dragStart.Value);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragStart == null) return;
        _dragStart = null;
        ReleaseMouseCapture();
        Cursor = Cursors.Hand;
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var foreground = FindBrush("Brush.Text", Brushes.White);
        var secondary = FindBrush("Brush.TextSecondary", Brushes.LightGray);
        var accent = FindBrush("Brush.Accent", Brushes.DodgerBlue);
        var border = FindBrush("Brush.Border", Brushes.Gray);
        dc.DrawRectangle(FindBrush("Brush.Background", Brushes.Transparent), new Pen(border, 1), new Rect(0, 0, ActualWidth, ActualHeight));

        var preview = Preview;
        if (preview == null || preview.Spans.Count == 0)
        {
            DrawText(dc, "Không có nhịp dầm hợp lệ", new WpfPoint(16, 16), secondary);
            return;
        }

        const double padding = 45;
        var axisY = ActualHeight * 0.48;
        WpfPoint Map(double x, double y)
        {
            var center = new WpfPoint(ActualWidth / 2, ActualHeight / 2);
            return new WpfPoint(center.X + (x - center.X) * _zoom + _pan.X, center.Y + (y - center.Y) * _zoom + _pan.Y);
        }

        var axisStartX = BeamChainPreviewFactory.ProjectX(0, preview.TotalLengthFeet, ActualWidth, padding);
        var axisEndX = BeamChainPreviewFactory.ProjectX(preview.TotalLengthFeet, preview.TotalLengthFeet, ActualWidth, padding);
        dc.DrawLine(new Pen(foreground, 2), Map(axisStartX, axisY), Map(axisEndX, axisY));

        foreach (var span in preview.Spans)
        {
            var x1 = BeamChainPreviewFactory.ProjectX(Math.Min(span.StartFeet, span.EndFeet), preview.TotalLengthFeet, ActualWidth, padding);
            var x2 = BeamChainPreviewFactory.ProjectX(Math.Max(span.StartFeet, span.EndFeet), preview.TotalLengthFeet, ActualWidth, padding);
            dc.DrawRectangle(null, new Pen(border, 2), new Rect(Map(x1, axisY - 22), Map(x2, axisY + 22)));
            DrawText(dc, $"NHỊP {span.DisplayIndex}", Map((x1 + x2) * 0.5 - 25, axisY - 58), foreground);
            DrawText(dc, span.Label, Map((x1 + x2) * 0.5 - 45, axisY + 30), secondary);
        }

        foreach (var station in preview.Stations)
        {
            var x = BeamChainPreviewFactory.ProjectX(station.ChainDistanceFeet, preview.TotalLengthFeet, ActualWidth, padding);
            dc.DrawLine(new Pen(accent, 2), Map(x, axisY - 34), Map(x, axisY + 34));
            DrawText(dc, station.Label, Map(x - 24, axisY + 54), accent);
        }

        DrawText(dc, preview.IsReversed ? "Hướng đọc: PHẢI → TRÁI" : "Hướng đọc: TRÁI → PHẢI", new WpfPoint(12, 12), accent);
        DrawText(dc, $"Zoom {Math.Round(_zoom * 100)}%  ·  Lăn chuột: zoom  ·  Kéo chuột: di chuyển  ·  Double-click: fit",
            new WpfPoint(12, Math.Max(12, ActualHeight - 24)), secondary);
    }

    private void ZoomAt(WpfPoint anchor, double factor)
    {
        var oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * factor, MinimumZoom, MaximumZoom);
        if (Math.Abs(_zoom - oldZoom) < 0.0001) return;
        var center = new WpfPoint(ActualWidth / 2, ActualHeight / 2);
        var renderedAnchor = anchor - center - _pan;
        _pan += renderedAnchor * (1 - _zoom / oldZoom);
        InvalidateVisual();
    }

    private void ResetView()
    {
        _zoom = 1.0;
        _pan = default;
        InvalidateVisual();
    }

    private static void OnPreviewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BeamChainPreviewCanvas)d).ResetView();
    private Brush FindBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static void DrawText(DrawingContext dc, string value, WpfPoint point, Brush brush)
    {
        var text = new WpfFormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, brush, 1.0);
        dc.DrawText(text, point);
    }
}
