using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RevitAPP.Core.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace BeamRebarPro.Views;

/// <summary>
/// Mặt cắt dọc toàn tuyến dầm kèm mặt cắt ngang thu nhỏ ở góc. Hình học đọc thẳng từ bản mô tả thép,
/// không suy diễn lại, nên những gì thấy ở đây là những gì sẽ được tạo trong mô hình.
/// </summary>
public sealed class BeamRebarPreview2D : FrameworkElement
{
    public static readonly DependencyProperty PlanProperty = DependencyProperty.Register(
        nameof(Plan), typeof(BeamRebarGeometryPlan), typeof(BeamRebarPreview2D),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) =>
            {
                var control = (BeamRebarPreview2D)d;
                Serilog.Log.Debug("[VE2D] Nhan plan moi: id={Id}, paths={Paths}, hienThi={Visible}",
                    control.GetHashCode(),
                    (e.NewValue as BeamRebarGeometryPlan)?.Paths.Count ?? -1,
                    control.IsVisible);
                control.InvalidateVisual();
            }));

    public static readonly DependencyProperty SectionStationMmProperty = DependencyProperty.Register(
        nameof(SectionStationMm), typeof(double), typeof(BeamRebarPreview2D),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Color BackgroundColor = Color.FromRgb(35, 38, 42);
    private static readonly Color LevelColor = Color.FromRgb(142, 205, 238);

    private double _zoom = 1;
    private Vector _pan;
    private Point _dragOrigin;
    private Vector _panOrigin;

    public BeamRebarGeometryPlan? Plan
    {
        get => (BeamRebarGeometryPlan?)GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    /// <summary>Vị trí lấy mặt cắt ngang. NaN = giữa tuyến dầm.</summary>
    public double SectionStationMm
    {
        get => (double)GetValue(SectionStationMmProperty);
        set => SetValue(SectionStationMmProperty, value);
    }

    public BeamRebarPreview2D()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Cross;
        // Thẻ bị ẩn không được vẽ lại dù dữ liệu đã đổi; vẽ lại ngay khi hiện ra để không hiện bản cũ.
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) InvalidateVisual(); };
        MouseWheel += (_, e) =>
        {
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), .2, 12);
            InvalidateVisual();
            e.Handled = true;
        };
        MouseLeftButtonDown += (_, e) =>
        {
            _dragOrigin = e.GetPosition(this);
            _panOrigin = _pan;
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || !IsMouseCaptured) return;
            _pan = _panOrigin + (e.GetPosition(this) - _dragOrigin);
            InvalidateVisual();
        };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
    }

    public void Fit()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(BackgroundColor), null, new Rect(RenderSize));

        var plan = Plan;
        if (plan is null || plan.IsEmpty || ActualWidth < 40 || ActualHeight < 40)
        {
            Serilog.Log.Debug("[VE2D] Bo qua: plan={HasPlan}, rong={Empty}, w={Width}, h={Height}",
                plan is not null, plan?.IsEmpty, ActualWidth, ActualHeight);
            return;
        }

        Serilog.Log.Debug("[VE2D] Dang ve {Paths} thanh (id={Id})", plan.Paths.Count, GetHashCode());

        var origin = plan.Context.Count > 0 ? plan.Context[0].StartCenterMm : default;
        var axis = RunAxis(plan);

        var insetSize = Math.Min(170, ActualWidth * .32);
        var plot = new Rect(16, 14,
            Math.Max(20, ActualWidth - insetSize - 34),
            Math.Max(20, ActualHeight - 30));

        var stations = plan.Paths
            .SelectMany(p => p.Points)
            .Select(p => Station(p, origin, axis))
            .ToArray();
        if (stations.Length == 0) return;

        var elevations = plan.Paths.SelectMany(p => p.Points).Select(p => p.Zmm).ToArray();
        var minStation = Math.Min(0, stations.Min());
        var maxStation = Math.Max(plan.TotalLengthMm, stations.Max());
        var minZ = elevations.Min();
        var maxZ = elevations.Max();

        foreach (var volume in plan.Context)
        {
            minZ = Math.Min(minZ, volume.StartCenterMm.Zmm - volume.HeightMm / 2);
            maxZ = Math.Max(maxZ, volume.StartCenterMm.Zmm + volume.HeightMm / 2);
        }

        var scale = Math.Min(
            plot.Width / Math.Max(1, maxStation - minStation),
            plot.Height / Math.Max(1, maxZ - minZ)) * .9 * _zoom;

        Point Map(double station, double z) => new(
            plot.Left + plot.Width / 2 + (station - (minStation + maxStation) / 2) * scale + _pan.X,
            plot.Top + plot.Height / 2 - (z - (minZ + maxZ) / 2) * scale + _pan.Y);

        DrawContext(dc, plan, origin, axis, Map);
        DrawSupports(dc, plan, plot, Map, minZ, maxZ);
        DrawBars(dc, plan, origin, axis, Map, scale);
        DrawSectionInset(dc, plan, origin, axis,
            new Rect(ActualWidth - insetSize + 4, 26, insetSize - 14, insetSize - 14));
    }

    /// <summary>Hướng tuyến dầm trong mặt bằng, để quy mọi điểm về khoảng cách dọc trục.</summary>
    private static (double X, double Y) RunAxis(BeamRebarGeometryPlan plan)
    {
        var beam = plan.Context.FirstOrDefault(v => v.Kind == BeamRebarContextKind.Beam);
        if (beam is null) return (1, 0);

        var dx = beam.EndCenterMm.Xmm - beam.StartCenterMm.Xmm;
        var dy = beam.EndCenterMm.Ymm - beam.StartCenterMm.Ymm;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length < 1e-6 ? (1, 0) : (dx / length, dy / length);
    }

    /// <summary>Khoảng cách của một điểm dọc theo tuyến dầm, tính từ đầu tuyến.</summary>
    private static double Station(GeometryPoint3D point, GeometryPoint3D origin, (double X, double Y) axis) =>
        (point.Xmm - origin.Xmm) * axis.X + (point.Ymm - origin.Ymm) * axis.Y;

    private static void DrawContext(
        DrawingContext dc, BeamRebarGeometryPlan plan, GeometryPoint3D origin,
        (double X, double Y) axis, Func<double, double, Point> map)
    {
        var beamFill = new SolidColorBrush(Color.FromArgb(54, 190, 195, 200));
        var columnFill = new SolidColorBrush(Color.FromArgb(70, 145, 150, 158));
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(100, 185, 190, 198)), 1);

        foreach (var volume in plan.Context)
        {
            var startStation = Station(volume.StartCenterMm, origin, axis);
            var endStation = Station(volume.EndCenterMm, origin, axis);
            var centreZ = (volume.StartCenterMm.Zmm + volume.EndCenterMm.Zmm) / 2;

            // Cột và dầm giao gần như không trải dài theo tuyến — cho chúng bề rộng thấy được.
            if (Math.Abs(endStation - startStation) < volume.WidthMm)
            {
                var mid = (startStation + endStation) / 2;
                startStation = mid - volume.WidthMm / 2;
                endStation = mid + volume.WidthMm / 2;
            }

            var top = map(startStation, centreZ + volume.HeightMm / 2);
            var bottom = map(endStation, centreZ - volume.HeightMm / 2);
            dc.DrawRectangle(
                volume.Kind == BeamRebarContextKind.Beam ? beamFill : columnFill,
                outline, new Rect(top, bottom));
        }
    }

    private static void DrawSupports(
        DrawingContext dc, BeamRebarGeometryPlan plan, Rect plot,
        Func<double, double, Point> map, double minZ, double maxZ)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(145, 105, 178, 224)), 1)
        {
            DashStyle = DashStyles.Dash
        };

        for (var i = 0; i < plan.SupportStationsMm.Count; i++)
        {
            var station = plan.SupportStationsMm[i];
            var top = map(station, maxZ);
            var bottom = map(station, minZ);
            dc.DrawLine(pen, new Point(top.X, plot.Top), new Point(bottom.X, plot.Bottom));
            DrawText(dc, $"{station / 1000:0.###} m", new Point(top.X + 3, plot.Top), 10, LevelColor);
        }
    }

    private void DrawBars(
        DrawingContext dc, BeamRebarGeometryPlan plan, GeometryPoint3D origin,
        (double X, double Y) axis, Func<double, double, Point> map, double scale)
    {
        foreach (var path in plan.Paths)
        {
            if (path.Points.Count < 2) continue;

            var brush = BarBrush(path.Kind);
            var thickness = Math.Clamp(path.DiameterMm * scale, 1.1, 7);
            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            for (var i = 1; i < path.Points.Count; i++)
            {
                var a = path.Points[i - 1];
                var b = path.Points[i];
                dc.DrawLine(pen,
                    map(Station(a, origin, axis), a.Zmm),
                    map(Station(b, origin, axis), b.Zmm));
            }

            if (path.IsClosedLoop)
            {
                var last = path.Points[^1];
                var first = path.Points[0];
                dc.DrawLine(pen,
                    map(Station(last, origin, axis), last.Zmm),
                    map(Station(first, origin, axis), first.Zmm));
            }
        }
    }

    /// <summary>Mặt cắt ngang tại một vị trí dọc: thấy rõ số thanh và cách xếp trong tiết diện.</summary>
    private void DrawSectionInset(
        DrawingContext dc, BeamRebarGeometryPlan plan, GeometryPoint3D origin,
        (double X, double Y) axis, Rect box)
    {
        if (box.Width < 30 || box.Height < 30) return;

        var beam = plan.Context.FirstOrDefault(v => v.Kind == BeamRebarContextKind.Beam);
        if (beam is null) return;

        var station = double.IsNaN(SectionStationMm) ? plan.TotalLengthMm / 2 : SectionStationMm;
        DrawText(dc, $"MẶT CẮT · {station / 1000:0.###} m", new Point(box.Left, 6), 11, Colors.White);

        var scale = Math.Min(box.Width / Math.Max(1, beam.WidthMm), box.Height / Math.Max(1, beam.HeightMm)) * .82;
        var centre = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);
        var rect = new Rect(
            centre.X - beam.WidthMm * scale / 2, centre.Y - beam.HeightMm * scale / 2,
            beam.WidthMm * scale, beam.HeightMm * scale);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(52, 190, 195, 200)), new Pen(Brushes.LightGray, 1), rect);

        var beamCentreZ = beam.StartCenterMm.Zmm;
        var across = (-axis.Y, axis.X);

        foreach (var path in plan.Paths)
        {
            var point = NearestPoint(path, station, origin, axis);
            if (point is null) continue;

            var lateral = (point.Value.Xmm - origin.Xmm) * across.Item1 + (point.Value.Ymm - origin.Ymm) * across.Item2;
            var vertical = point.Value.Zmm - beamCentreZ;
            var radius = Math.Clamp(path.DiameterMm * scale / 2, 1.5, 9);

            dc.DrawEllipse(BarBrush(path.Kind), null,
                new Point(centre.X + lateral * scale, centre.Y - vertical * scale), radius, radius);
        }
    }

    /// <summary>Điểm của thanh gần mặt cắt nhất, bỏ qua thanh không chạy qua vị trí đó.</summary>
    private static GeometryPoint3D? NearestPoint(
        BeamRebarPath path, double station, GeometryPoint3D origin, (double X, double Y) axis)
    {
        GeometryPoint3D? best = null;
        var bestDistance = double.MaxValue;

        foreach (var point in path.Points)
        {
            var distance = Math.Abs(Station(point, origin, axis) - station);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = point;
        }

        // Thanh dọc luôn cắt qua mặt cắt; đai chỉ hiện khi nằm sát vị trí đang xem.
        var tolerance = path.IsClosedLoop ? 60 : double.MaxValue;
        return bestDistance <= tolerance ? best : null;
    }

    private Brush BarBrush(BeamRebarPathKind kind) => kind switch
    {
        BeamRebarPathKind.MainTop or BeamRebarPathKind.MainBottom =>
            Resource("Brush.RebarPreview", Color.FromRgb(240, 55, 55)),
        BeamRebarPathKind.AdditionalTop or BeamRebarPathKind.AdditionalBottom =>
            Resource("Brush.RebarPreviewAdditional", Color.FromRgb(255, 170, 60)),
        BeamRebarPathKind.StirrupSecondary =>
            Resource("Brush.RebarPreviewSecondary", Color.FromRgb(120, 230, 160)),
        _ => Resource("Brush.RebarPreviewStirrup", Color.FromRgb(90, 190, 255))
    };

    private Brush Resource(string key, Color fallback) =>
        TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static void DrawText(DrawingContext dc, string text, Point origin, double size, Color color) =>
        dc.DrawText(new System.Windows.Media.FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, new SolidColorBrush(color), 1.0), origin);
}
