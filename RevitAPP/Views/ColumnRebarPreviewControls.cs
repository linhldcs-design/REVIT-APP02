using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RevitAPP.Core.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using WpfGrid = System.Windows.Controls.Grid;

namespace RevitAPP.Views;

internal static class ColumnRebarPreviewProjection
{
    /// <summary>Half extent of a rotated local B/H rectangle projected onto world X or Y.</summary>
    internal static double ProjectedHalfExtent(double widthMm, double heightMm, double rotationRad, bool sideView)
    {
        var cos = Math.Abs(Math.Cos(rotationRad));
        var sin = Math.Abs(Math.Sin(rotationRad));
        return sideView
            ? (sin * widthMm + cos * heightMm) / 2d
            : (cos * widthMm + sin * heightMm) / 2d;
    }

    /// <summary>Inverse of the geometry factory's local-to-world rotation/translation.</summary>
    internal static (double Xmm, double Ymm) WorldToLocal(
        double worldXmm, double worldYmm, double centerXmm, double centerYmm, double rotationRad)
    {
        var dx = worldXmm - centerXmm;
        var dy = worldYmm - centerYmm;
        var cos = Math.Cos(rotationRad);
        var sin = Math.Sin(rotationRad);
        return (dx * cos + dy * sin, -dx * sin + dy * cos);
    }
}

/// <summary>Lightweight faithful elevation/section renderer. Geometry is read, never inferred, from the shared plan.</summary>
public sealed class ColumnRebarPreview2D : FrameworkElement
{
    public static readonly DependencyProperty PlanProperty = DependencyProperty.Register(nameof(Plan),
        typeof(ColumnRebarGeometryPlan), typeof(ColumnRebarPreview2D), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SideViewProperty = DependencyProperty.Register(nameof(SideView),
        typeof(bool), typeof(ColumnRebarPreview2D), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedStoreyIndexProperty = DependencyProperty.Register(nameof(SelectedStoreyIndex),
        typeof(int), typeof(ColumnRebarPreview2D), new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    private double _zoom = 1;
    private Vector _pan;
    private Point _dragOrigin;
    private Vector _panOrigin;

    public ColumnRebarGeometryPlan? Plan { get => (ColumnRebarGeometryPlan?)GetValue(PlanProperty); set => SetValue(PlanProperty, value); }
    public bool SideView { get => (bool)GetValue(SideViewProperty); set => SetValue(SideViewProperty, value); }
    public int SelectedStoreyIndex { get => (int)GetValue(SelectedStoreyIndexProperty); set => SetValue(SelectedStoreyIndexProperty, value); }

    public ColumnRebarPreview2D()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Cross;
        MouseWheel += (_, e) => { _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), .2, 12); InvalidateVisual(); e.Handled = true; };
        MouseLeftButtonDown += (_, e) => { _dragOrigin = e.GetPosition(this); _panOrigin = _pan; CaptureMouse(); };
        MouseMove += (_, e) => { if (e.LeftButton != MouseButtonState.Pressed || !IsMouseCaptured) return; _pan = _panOrigin + (e.GetPosition(this) - _dragOrigin); InvalidateVisual(); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
    }

    public void Fit() { _zoom = 1; _pan = default; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(35, 38, 42)), null, new Rect(RenderSize));
        var plan = Plan;
        if (plan == null || plan.Stack.Count == 0 || ActualWidth < 40 || ActualHeight < 40) return;

        var points = plan.Paths.SelectMany(p => p.Points).ToArray();
        var volumes = plan.Context;
        var horizontal = (SideView ? points.Select(p => p.Ymm) : points.Select(p => p.Xmm)).Concat(
            volumes.SelectMany(v =>
            {
                var center = SideView ? v.CenterYmm : v.CenterXmm;
                var halfExtent = ColumnRebarPreviewProjection.ProjectedHalfExtent(
                    v.WidthMm, v.HeightMm, v.RotationRad, SideView);
                return new[] { center - halfExtent, center + halfExtent };
            }));
        var minX = horizontal.DefaultIfEmpty(0).Min();
        var maxX = horizontal.DefaultIfEmpty(1).Max();
        var minZ = Math.Min(points.Select(p => p.Zmm).DefaultIfEmpty(0).Min(), volumes.Select(v => v.BaseElevationMm).DefaultIfEmpty(0).Min());
        var maxZ = Math.Max(points.Select(p => p.Zmm).DefaultIfEmpty(1).Max(), volumes.Select(v => v.TopElevationMm).DefaultIfEmpty(1).Max());
        var insetWidth = Math.Min(150, ActualWidth * .3);
        const double labelWidth = 92;
        var plot = new Rect(18, 15, Math.Max(20, ActualWidth - labelWidth - insetWidth - 28), Math.Max(20, ActualHeight - 30));
        var scale = Math.Min(plot.Width / Math.Max(1, maxX - minX), plot.Height / Math.Max(1, maxZ - minZ)) * .9 * _zoom;
        Point Map(double x, double z) => new(plot.Left + plot.Width / 2 + (x - (minX + maxX) / 2) * scale + _pan.X,
            plot.Top + plot.Height / 2 - (z - (minZ + maxZ) / 2) * scale + _pan.Y);

        var concrete = new SolidColorBrush(Color.FromArgb(54, 190, 195, 200));
        var beam = new SolidColorBrush(Color.FromArgb(70, 145, 150, 158));
        foreach (var volume in volumes)
        {
            var cx = SideView ? volume.CenterYmm : volume.CenterXmm;
            var halfExtent = ColumnRebarPreviewProjection.ProjectedHalfExtent(
                volume.WidthMm, volume.HeightMm, volume.RotationRad, SideView);
            var a = Map(cx - halfExtent, volume.TopElevationMm);
            var b = Map(cx + halfExtent, volume.BaseElevationMm);
            dc.DrawRectangle(volume.Kind == ColumnRebarContextKind.Column ? concrete : beam,
                new Pen(new SolidColorBrush(Color.FromArgb(100, 185, 190, 198)), 1), new Rect(a, b));
        }

        var levelPen = new Pen(new SolidColorBrush(Color.FromArgb(145, 105, 178, 224)), 1) { DashStyle = DashStyles.Dash };
        var levels = plan.Stack.Select(s => (s.Storey.LevelName, s.Storey.BaseElevationMm))
            .Append((plan.Stack[^1].Storey.LevelName + " TOP", plan.Stack[^1].Storey.TopElevationMm)).Distinct().ToArray();
        foreach (var (name, z) in levels)
        {
            var y = Map(0, z).Y;
            dc.DrawLine(levelPen, new Point(plot.Left, y), new Point(ActualWidth - insetWidth - 5, y));
            DrawText(dc, $"{name}  {z / 1000:0.###} m", new Point(ActualWidth - insetWidth - labelWidth, y - 15), 11, Color.FromRgb(142, 205, 238));
        }

        var red = TryFindResource("Brush.RebarPreview") as Brush ?? new SolidColorBrush(Color.FromRgb(240, 55, 55));
        foreach (var path in plan.Paths)
        {
            var thickness = Math.Clamp(path.DiameterMm * scale, 1.15, 7);
            var pen = new Pen(red, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            for (var i = 1; i < path.Points.Count; i++)
            {
                var a = path.Points[i - 1]; var b = path.Points[i];
                dc.DrawLine(pen, Map(SideView ? a.Ymm : a.Xmm, a.Zmm), Map(SideView ? b.Ymm : b.Xmm, b.Zmm));
            }
        }

        DrawSectionInset(dc, plan, new Rect(ActualWidth - insetWidth + 6, 28, insetWidth - 12, insetWidth - 12));
    }

    private void DrawSectionInset(DrawingContext dc, ColumnRebarGeometryPlan plan, Rect box)
    {
        var selected = plan.Stack.FirstOrDefault(s => s.Storey.Index == SelectedStoreyIndex) ?? plan.Stack[0];
        var context = plan.Context.FirstOrDefault(v => v.StoreyIndex == selected.Storey.Index && v.Kind == ColumnRebarContextKind.Column);
        if (context == null) return;
        DrawText(dc, $"SECTION · {selected.Storey.LevelName}", new Point(box.Left, 7), 11, Colors.White);
        var size = Math.Max(context.WidthMm, context.HeightMm);
        var scale = Math.Min(box.Width, box.Height) * .82 / Math.Max(1, size);
        var c = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);
        var rect = new Rect(c.X - context.WidthMm * scale / 2, c.Y - context.HeightMm * scale / 2,
            context.WidthMm * scale, context.HeightMm * scale);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(52, 190, 195, 200)), new Pen(Brushes.LightGray, 1), rect);
        var sampleZ = (selected.Storey.BaseElevationMm + selected.Storey.TopElevationMm) / 2;
        var paths = plan.Paths.Where(p => p.StoreyIndex == selected.Storey.Index && p.Kind is ColumnRebarPathKind.MainBar or ColumnRebarPathKind.DistributionBar);
        foreach (var path in paths)
        {
            var p = path.Points.OrderBy(q => Math.Abs(q.Zmm - sampleZ)).First();
            var local = ColumnRebarPreviewProjection.WorldToLocal(
                p.Xmm, p.Ymm, selected.CenterXmm, selected.CenterYmm, selected.RotationRad);
            var d = Math.Clamp(path.DiameterMm * scale, 3, 11);
            var red = TryFindResource("Brush.RebarPreview") as Brush ?? Brushes.Red;
            dc.DrawEllipse(red, null, new Point(c.X + local.Xmm * scale, c.Y - local.Ymm * scale), d / 2, d / 2);
        }
    }

    private static void DrawText(DrawingContext dc, string text, Point origin, double size, Color color) =>
        dc.DrawText(new System.Windows.Media.FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, new SolidColorBrush(color), 1.0), origin);
}

/// <summary>WPF 3D review with diameter-accurate tube meshes and orbit camera.</summary>
public sealed class ColumnRebarPreview3D : WpfGrid
{
    public static readonly DependencyProperty PlanProperty = DependencyProperty.Register(nameof(Plan),
        typeof(ColumnRebarGeometryPlan), typeof(ColumnRebarPreview3D), new PropertyMetadata(null, (d, _) => ((ColumnRebarPreview3D)d).Rebuild()));
    private readonly Viewport3D _viewport = new();
    private readonly Canvas _labels = new() { IsHitTestVisible = false };
    private Point3D _target;
    private double _distance = 5000, _minimumDistance = 100;
    private double _yaw = -.75, _pitch = .45;
    private Point _origin;
    private Point3D _panTargetOrigin;
    private CameraDragMode _dragMode;
    private MouseButton? _dragButton;

    public ColumnRebarGeometryPlan? Plan { get => (ColumnRebarGeometryPlan?)GetValue(PlanProperty); set => SetValue(PlanProperty, value); }

    public ColumnRebarPreview3D()
    {
        ClipToBounds = true; Background = new SolidColorBrush(Color.FromRgb(35, 38, 42));
        Children.Add(_viewport); Children.Add(_labels);
        SizeChanged += (_, _) => BuildLevelLabels();
        MouseWheel += (_, e) => { _distance = Math.Clamp(_distance * (e.Delta > 0 ? .84 : 1.19), _minimumDistance, _minimumDistance * 200); UpdateCamera(); e.Handled = true; };
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2) { EndDrag(); Fit(); e.Handled = true; return; }
                BeginDrag(e.GetPosition(this), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? CameraDragMode.Pan : CameraDragMode.Orbit, MouseButton.Left);
            }
            else if (e.ChangedButton == MouseButton.Middle)
            {
                BeginDrag(e.GetPosition(this), CameraDragMode.Pan, MouseButton.Middle);
            }
            else return;
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (!IsMouseCaptured || _dragMode == CameraDragMode.None) return;
            var initiatingButtonPressed = _dragButton switch
            {
                MouseButton.Left => e.LeftButton == MouseButtonState.Pressed,
                MouseButton.Middle => e.MiddleButton == MouseButtonState.Pressed,
                _ => false
            };
            if (!initiatingButtonPressed) { EndDrag(); return; }
            var p = e.GetPosition(this);
            if (_dragMode == CameraDragMode.Orbit)
            {
                _yaw += (p.X - _origin.X) * .008;
                _pitch = Math.Clamp(_pitch - (p.Y - _origin.Y) * .008, -1.4, 1.4);
                _origin = p;
            }
            else
            {
                PanFromOrigin(p);
            }
            UpdateCamera();
            e.Handled = true;
        };
        MouseUp += (_, e) =>
        {
            if (_dragMode == CameraDragMode.None || e.ChangedButton != _dragButton) return;
            EndDrag();
            e.Handled = true;
        };
        LostMouseCapture += (_, _) => { _dragMode = CameraDragMode.None; _dragButton = null; Cursor = Cursors.Arrow; };
    }

    private void BeginDrag(Point point, CameraDragMode mode, MouseButton button)
    {
        _origin = point;
        _panTargetOrigin = _target;
        _dragMode = mode;
        _dragButton = button;
        CaptureMouse();
        Cursor = mode == CameraDragMode.Pan ? Cursors.Hand : Cursors.SizeAll;
    }

    private void EndDrag()
    {
        _dragMode = CameraDragMode.None;
        _dragButton = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void PanFromOrigin(Point current)
    {
        var horizontal = _distance * Math.Cos(_pitch);
        var cameraPosition = new Point3D(_target.X + horizontal * Math.Cos(_yaw),
            _target.Y + horizontal * Math.Sin(_yaw), _target.Z + _distance * Math.Sin(_pitch));
        var forward = _target - cameraPosition;
        forward.Normalize();
        var right = Vector3D.CrossProduct(forward, new Vector3D(0, 0, 1));
        if (right.LengthSquared < 1e-9) right = new Vector3D(1, 0, 0);
        else right.Normalize();
        var up = Vector3D.CrossProduct(right, forward);
        up.Normalize();

        var worldPerPixel = 2 * _distance * Math.Tan(42 * Math.PI / 360) /
                            Math.Max(1, ActualHeight);
        var delta = current - _origin;
        _target = _panTargetOrigin - right * (delta.X * worldPerPixel) + up * (delta.Y * worldPerPixel);
    }

    private enum CameraDragMode { None, Orbit, Pan }

    public void Fit()
    {
        var p = Plan; if (p == null) return;
        var all = p.Paths.SelectMany(x => x.Points).ToArray(); if (all.Length == 0) return;
        _target = new Point3D((all.Min(q => q.Xmm) + all.Max(q => q.Xmm)) / 2, (all.Min(q => q.Ymm) + all.Max(q => q.Ymm)) / 2, (all.Min(q => q.Zmm) + all.Max(q => q.Zmm)) / 2);
        var span = Math.Max(all.Max(q => q.Zmm) - all.Min(q => q.Zmm), Math.Max(all.Max(q => q.Xmm) - all.Min(q => q.Xmm), all.Max(q => q.Ymm) - all.Min(q => q.Ymm)));
        _minimumDistance = Math.Max(100, span * .12); _distance = Math.Max(span * 1.65, 800); UpdateCamera();
    }

    private void Rebuild()
    {
        _viewport.Children.Clear(); _labels.Children.Clear();
        var plan = Plan; if (plan == null) return;
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(190, 190, 190)));
        group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2)));
        foreach (var v in plan.Context) group.Children.Add(Box(v));
        var mesh = new MeshGeometry3D();
        var sides = plan.Paths.Count > 2500 ? 4 : 7;
        foreach (var path in plan.Paths)
            for (var i = 1; i < path.Points.Count; i++) AddTube(mesh, path.Points[i - 1], path.Points[i], path.DiameterMm / 2, sides);
        var redColor = TryFindResource("Color.RebarPreview3D") is Color themed ? themed : Color.FromRgb(235, 42, 42);
        // Emissive red keeps reinforcement visible inside the neutral context regardless of camera/light direction.
        var red = new MaterialGroup();
        red.Children.Add(new DiffuseMaterial(new SolidColorBrush(redColor)));
        red.Children.Add(new EmissiveMaterial(new SolidColorBrush(redColor)));
        group.Children.Add(new GeometryModel3D(mesh, red) { BackMaterial = red });
        AddLevelPlanes(group, plan);
        _viewport.Children.Add(new ModelVisual3D { Content = group });
        Fit(); BuildLevelLabels();
    }

    private static GeometryModel3D Box(ColumnRebarContextVolume v)
    {
        // Solid translucent boxes still write to WPF's depth buffer and can completely hide tubes inside them.
        // Render the concrete/beam/foundation context as an x-ray wire box instead.
        var mesh = new MeshGeometry3D();
        var cos = Math.Cos(v.RotationRad); var sin = Math.Sin(v.RotationRad);
        GeometryPoint3D P(double x, double y, double z) => new(v.CenterXmm + x * cos - y * sin, v.CenterYmm + x * sin + y * cos, z);
        var hw = v.WidthMm / 2; var hh = v.HeightMm / 2;
        var bottom = new[] { P(-hw, -hh, v.BaseElevationMm), P(hw, -hh, v.BaseElevationMm), P(hw, hh, v.BaseElevationMm), P(-hw, hh, v.BaseElevationMm) };
        var top = new[] { P(-hw, -hh, v.TopElevationMm), P(hw, -hh, v.TopElevationMm), P(hw, hh, v.TopElevationMm), P(-hw, hh, v.TopElevationMm) };
        var edgeRadius = Math.Clamp(Math.Min(v.WidthMm, v.HeightMm) / 140d, 1.5d, 5d);
        for (var i = 0; i < 4; i++)
        {
            var next = (i + 1) % 4;
            AddTube(mesh, bottom[i], bottom[next], edgeRadius, 4);
            AddTube(mesh, top[i], top[next], edgeRadius, 4);
            AddTube(mesh, bottom[i], top[i], edgeRadius, 4);
        }
        var color = v.Kind == ColumnRebarContextKind.Column ? Color.FromArgb(150, 185, 190, 194) : Color.FromArgb(170, 135, 140, 148);
        var material = new DiffuseMaterial(new SolidColorBrush(color)); return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static void AddTube(MeshGeometry3D mesh, GeometryPoint3D a, GeometryPoint3D b, double radius, int sides)
    {
        var p0 = new Point3D(a.Xmm, a.Ymm, a.Zmm); var p1 = new Point3D(b.Xmm, b.Ymm, b.Zmm);
        var axis = p1 - p0; if (axis.Length < .01) return; axis.Normalize();
        var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Z) < .9 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0)); u.Normalize();
        var v = Vector3D.CrossProduct(axis, u); var start = mesh.Positions.Count;
        for (var end = 0; end < 2; end++) for (var j = 0; j < sides; j++)
        { var angle = j * Math.PI * 2 / sides; mesh.Positions.Add((end == 0 ? p0 : p1) + radius * (Math.Cos(angle) * u + Math.Sin(angle) * v)); }
        for (var j = 0; j < sides; j++) { var n = (j + 1) % sides; var a0 = start + j; var a1 = start + n; var b0 = start + sides + j; var b1 = start + sides + n; foreach (var q in new[] { a0,a1,b1,a0,b1,b0 }) mesh.TriangleIndices.Add(q); }
    }

    private static void AddLevelPlanes(Model3DGroup group, ColumnRebarGeometryPlan plan)
    {
        var xs = plan.Context.SelectMany(v => new[] { v.CenterXmm - v.WidthMm, v.CenterXmm + v.WidthMm }).ToArray();
        var ys = plan.Context.SelectMany(v => new[] { v.CenterYmm - v.HeightMm, v.CenterYmm + v.HeightMm }).ToArray(); if (xs.Length == 0) return;
        var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(14, 80, 175, 225)));
        foreach (var z in plan.Stack.Select(s => s.Storey.BaseElevationMm).Append(plan.Stack[^1].Storey.TopElevationMm).Distinct())
        { var m = new MeshGeometry3D { Positions = new Point3DCollection { new(xs.Min(),ys.Min(),z), new(xs.Max(),ys.Min(),z), new(xs.Max(),ys.Max(),z), new(xs.Min(),ys.Max(),z) }, TriangleIndices = new Int32Collection { 0,1,2,0,2,3 } }; group.Children.Add(new GeometryModel3D(m, mat) { BackMaterial = mat }); }
    }

    private void BuildLevelLabels()
    {
        _labels.Children.Clear(); var plan = Plan; if (plan == null || ActualHeight < 30) return;
        var levels = plan.Stack.Select(s => (s.Storey.LevelName, s.Storey.BaseElevationMm)).Append((plan.Stack[^1].Storey.LevelName + " TOP", plan.Stack[^1].Storey.TopElevationMm)).Distinct().ToArray();
        var min = levels.Min(x => x.Item2); var max = levels.Max(x => x.Item2);
        foreach (var (name, z) in levels) { var text = new TextBlock { Text = $"{name}  {z / 1000:0.###} m", Foreground = new SolidColorBrush(Color.FromRgb(142,205,238)), FontSize = 11, Background = new SolidColorBrush(Color.FromArgb(120,35,38,42)), Padding = new Thickness(3,1,3,1) }; Canvas.SetRight(text, 5); Canvas.SetTop(text, 8 + (max - z) / Math.Max(1, max - min) * Math.Max(1, ActualHeight - 42)); _labels.Children.Add(text); }
    }

    private void UpdateCamera()
    {
        var horizontal = _distance * Math.Cos(_pitch); var position = new Point3D(_target.X + horizontal * Math.Cos(_yaw), _target.Y + horizontal * Math.Sin(_yaw), _target.Z + _distance * Math.Sin(_pitch));
        _viewport.Camera = new PerspectiveCamera(position, _target - position, new Vector3D(0,0,1), 42);
    }
}
