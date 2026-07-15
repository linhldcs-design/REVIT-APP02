using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevitAPP.Core.Models.BeamDrawing;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfFormattedText = System.Windows.Media.FormattedText;
using WpfPoint = System.Windows.Point;

namespace RevitAPP.Views.Controls;

/// <summary>
/// WPF-only preview for a beam cross section. Drawing stays vector based and avoids native render dependencies.
/// </summary>
public sealed class CrossSectionDiagramCanvas : Canvas
{
    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
        nameof(Preview), typeof(CrossSectionPreview), typeof(CrossSectionDiagramCanvas),
        new FrameworkPropertyMetadata(CrossSectionPreview.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(CrossSectionDiagramCanvas),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LongitudinalLabelProperty = DependencyProperty.Register(
        nameof(LongitudinalLabel), typeof(string), typeof(CrossSectionDiagramCanvas),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ReinforceLabelProperty = DependencyProperty.Register(
        nameof(ReinforceLabel), typeof(string), typeof(CrossSectionDiagramCanvas),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StirrupLabelProperty = DependencyProperty.Register(
        nameof(StirrupLabel), typeof(string), typeof(CrossSectionDiagramCanvas),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LeadersToLeftProperty = DependencyProperty.Register(
        nameof(LeadersToLeft), typeof(bool), typeof(CrossSectionDiagramCanvas),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ConcreteBrushProperty = DependencyProperty.Register(
        nameof(ConcreteBrush), typeof(Brush), typeof(CrossSectionDiagramCanvas),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(CrossSectionDiagramCanvas),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AnnotationBrushProperty = DependencyProperty.Register(
        nameof(AnnotationBrush), typeof(Brush), typeof(CrossSectionDiagramCanvas),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public CrossSectionPreview Preview
    {
        get => (CrossSectionPreview)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string LongitudinalLabel
    {
        get => (string)GetValue(LongitudinalLabelProperty);
        set => SetValue(LongitudinalLabelProperty, value);
    }

    public string ReinforceLabel
    {
        get => (string)GetValue(ReinforceLabelProperty);
        set => SetValue(ReinforceLabelProperty, value);
    }

    public string StirrupLabel
    {
        get => (string)GetValue(StirrupLabelProperty);
        set => SetValue(StirrupLabelProperty, value);
    }

    public bool LeadersToLeft
    {
        get => (bool)GetValue(LeadersToLeftProperty);
        set => SetValue(LeadersToLeftProperty, value);
    }

    public Brush ConcreteBrush
    {
        get => (Brush)GetValue(ConcreteBrushProperty);
        set => SetValue(ConcreteBrushProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush AnnotationBrush
    {
        get => (Brush)GetValue(AnnotationBrushProperty);
        set => SetValue(AnnotationBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var preview = Preview ?? CrossSectionPreview.Empty;
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 80 || height < 100) return;

        DrawText(drawingContext, Heading, 13, FontWeights.SemiBold,
            new WpfPoint(width / 2, 6), TextAlignment.Center, ConcreteBrush);

        if (!preview.HasData)
        {
            DrawText(drawingContext, "Chưa đọc được tiết diện", 11, FontWeights.Normal,
                new WpfPoint(width / 2, height / 2), TextAlignment.Center, AnnotationBrush);
            return;
        }

        const double labelSpace = 82;
        const double top = 34;
        const double bottomSpace = 48;
        var areaLeft = LeadersToLeft ? labelSpace : 14;
        var areaRight = LeadersToLeft ? width - 14 : width - labelSpace;
        var maxFrameWidth = Math.Max(32, areaRight - areaLeft);
        var maxFrameHeight = Math.Max(50, height - top - bottomSpace);
        var ratio = preview.WidthMm / preview.HeightMm;
        var frameHeight = maxFrameHeight;
        var frameWidth = frameHeight * ratio;
        if (frameWidth > maxFrameWidth)
        {
            frameWidth = maxFrameWidth;
            frameHeight = frameWidth / ratio;
        }

        var centerX = (areaLeft + areaRight) / 2;
        var left = centerX - frameWidth / 2;
        var frameTop = top + (maxFrameHeight - frameHeight) / 2;
        var frame = new Rect(left, frameTop, frameWidth, frameHeight);
        var concretePen = new Pen(ConcreteBrush, 1.4);
        var accentPen = new Pen(AccentBrush, 1.4);
        var annotationPen = new Pen(AnnotationBrush, 1);
        drawingContext.DrawRectangle(null, concretePen, frame);

        var inset = Math.Clamp(Math.Min(frameWidth, frameHeight) * 0.11, 5, 13);
        var cage = new Rect(frame.Left + inset, frame.Top + inset,
            Math.Max(4, frame.Width - 2 * inset), Math.Max(4, frame.Height - 2 * inset));
        if (preview.HasStirrup) drawingContext.DrawRoundedRectangle(null, accentPen, cage, 4, 4);

        DrawBars(drawingContext, preview, cage);

        var yTop = cage.Top + 5;
        var yMiddle = cage.Top + cage.Height / 2;
        var yBottom = cage.Bottom - 5;
        DrawLeader(drawingContext, frame, yTop, LongitudinalLabel);
        DrawLeader(drawingContext, frame, yMiddle, ReinforceLabel);
        DrawLeader(drawingContext, frame, yBottom, StirrupLabel);

        var dimY = frame.Bottom + 18;
        drawingContext.DrawLine(annotationPen, new WpfPoint(frame.Left, dimY), new WpfPoint(frame.Right, dimY));
        drawingContext.DrawLine(annotationPen, new WpfPoint(frame.Left, dimY - 4), new WpfPoint(frame.Left, dimY + 4));
        drawingContext.DrawLine(annotationPen, new WpfPoint(frame.Right, dimY - 4), new WpfPoint(frame.Right, dimY + 4));
        DrawText(drawingContext, $"{preview.WidthMm:0} × {preview.HeightMm:0}", 10, FontWeights.Normal,
            new WpfPoint(centerX, dimY + 5), TextAlignment.Center, AnnotationBrush);
    }

    private void DrawBars(DrawingContext drawingContext, CrossSectionPreview preview, Rect cage)
    {
        if (preview.Bars.Count > 0)
        {
            foreach (var bar in preview.Bars)
            {
                var x = cage.Left + Math.Clamp(bar.X, 0, 1) * cage.Width;
                var y = cage.Bottom - Math.Clamp(bar.Y, 0, 1) * cage.Height;
                drawingContext.DrawEllipse(AccentBrush, null, new WpfPoint(x, y), 3.2, 3.2);
            }
            return;
        }

        var xs = new[] { cage.Left + 5, cage.Left + cage.Width / 2, cage.Right - 5 };
        foreach (var x in xs)
        {
            drawingContext.DrawEllipse(AccentBrush, null, new WpfPoint(x, cage.Top + 5), 3.2, 3.2);
            drawingContext.DrawEllipse(AccentBrush, null, new WpfPoint(x, cage.Bottom - 5), 3.2, 3.2);
        }
        drawingContext.DrawEllipse(AccentBrush, null, new WpfPoint(cage.Left + 5, cage.Top + cage.Height / 2), 3.2, 3.2);
        drawingContext.DrawEllipse(AccentBrush, null, new WpfPoint(cage.Right - 5, cage.Top + cage.Height / 2), 3.2, 3.2);
    }

    private void DrawLeader(DrawingContext drawingContext, Rect frame, double y, string? label)
    {
        var startX = LeadersToLeft ? frame.Left : frame.Right;
        var endX = LeadersToLeft ? 76 : ActualWidth - 76;
        drawingContext.DrawLine(new Pen(AccentBrush, 1), new WpfPoint(startX, y), new WpfPoint(endX, y));
        if (string.IsNullOrWhiteSpace(label)) return;

        var textX = LeadersToLeft ? endX - 4 : endX + 4;
        DrawText(drawingContext, Shorten(label), 9.5, FontWeights.Normal,
            new WpfPoint(textX, y - 7), LeadersToLeft ? TextAlignment.Right : TextAlignment.Left, AnnotationBrush);
    }

    private void DrawText(DrawingContext drawingContext, string? text, double size, FontWeight weight,
        WpfPoint origin, TextAlignment alignment, Brush brush)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var formatted = new WpfFormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new WpfFontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = alignment,
            MaxTextWidth = 76,
            Trimming = TextTrimming.CharacterEllipsis
        };
        var drawX = alignment switch
        {
            TextAlignment.Center => origin.X - formatted.MaxTextWidth / 2,
            TextAlignment.Right => origin.X - formatted.MaxTextWidth,
            _ => origin.X
        };
        drawingContext.DrawText(formatted, new WpfPoint(drawX, origin.Y));
    }

    private static string Shorten(string name)
    {
        var value = name;
        var colon = value.LastIndexOf(':');
        if (colon >= 0 && colon < value.Length - 1) value = value[(colon + 1)..].Trim();
        return value.Length > 18 ? value[..18] + "…" : value;
    }
}
