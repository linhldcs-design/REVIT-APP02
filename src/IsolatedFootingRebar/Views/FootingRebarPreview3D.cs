using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using IsolatedFootingRebar.Models;
using IsolatedFootingRebar.Services;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using WpfGrid = System.Windows.Controls.Grid;

namespace IsolatedFootingRebar.Views;

/// <summary>Viewport3D thuần WPF: xoay trái, pan Shift+trái/chuột giữa, lăn để zoom, double-click để Fit.</summary>
public sealed class FootingRebarPreview3D : WpfGrid
{
    public static readonly DependencyProperty PlanProperty = DependencyProperty.Register(
        nameof(Plan), typeof(FootingRebarPreviewPlan), typeof(FootingRebarPreview3D),
        new PropertyMetadata(null, (d, _) => ((FootingRebarPreview3D)d).Rebuild()));

    // Hai viewport chồng nhau để bê tông bán trong suốt không ghi depth che thép.
    private readonly Viewport3D _concreteViewport = new() { IsHitTestVisible = false };
    private readonly Viewport3D _rebarViewport = new() { IsHitTestVisible = false };
    private Point3D _target;
    private double _distance = 5000;
    private double _minimumDistance = 100;
    private double _yaw = -.75;
    private double _pitch = .45;
    private Point _dragOrigin;
    private Point3D _panOrigin;
    private DragMode _dragMode;
    private MouseButton? _dragButton;
    private bool _hasFitted;

    private enum DragMode { None, Orbit, Pan }

    public FootingRebarPreviewPlan? Plan
    {
        get => (FootingRebarPreviewPlan?)GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    public FootingRebarPreview3D()
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromRgb(35, 38, 42));
        Children.Add(_concreteViewport);
        Children.Add(_rebarViewport);
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Rebuild(); };
        MouseWheel += (_, e) =>
        {
            _distance = Math.Clamp(_distance * (e.Delta > 0 ? .84 : 1.19), _minimumDistance, _minimumDistance * 200);
            UpdateCamera();
            e.Handled = true;
        };
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += (_, e) =>
        {
            if (_dragMode == DragMode.None || e.ChangedButton != _dragButton) return;
            EndDrag(); e.Handled = true;
        };
        LostMouseCapture += (_, _) => { _dragMode = DragMode.None; _dragButton = null; Cursor = Cursors.Arrow; };
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            EndDrag(); Fit(); e.Handled = true; return;
        }
        if (e.ChangedButton == MouseButton.Left)
            BeginDrag(e.GetPosition(this), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? DragMode.Pan : DragMode.Orbit, MouseButton.Left);
        else if (e.ChangedButton == MouseButton.Middle)
            BeginDrag(e.GetPosition(this), DragMode.Pan, MouseButton.Middle);
        else return;
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured || _dragMode == DragMode.None) return;
        var pressed = _dragButton == MouseButton.Left ? e.LeftButton == MouseButtonState.Pressed : e.MiddleButton == MouseButtonState.Pressed;
        if (!pressed) { EndDrag(); return; }
        var current = e.GetPosition(this);
        if (_dragMode == DragMode.Orbit)
        {
            _yaw += (current.X - _dragOrigin.X) * .008;
            _pitch = Math.Clamp(_pitch - (current.Y - _dragOrigin.Y) * .008, -1.4, 1.4);
            _dragOrigin = current;
        }
        else Pan(current);
        UpdateCamera(); e.Handled = true;
    }

    private void BeginDrag(Point point, DragMode mode, MouseButton button)
    {
        _dragOrigin = point; _panOrigin = _target; _dragMode = mode; _dragButton = button;
        CaptureMouse(); Cursor = mode == DragMode.Pan ? Cursors.Hand : Cursors.SizeAll;
    }

    private void EndDrag()
    {
        _dragMode = DragMode.None; _dragButton = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void Pan(Point current)
    {
        var camera = CameraPosition();
        var forward = _target - camera; forward.Normalize();
        var right = Vector3D.CrossProduct(forward, new Vector3D(0, 0, 1));
        if (right.LengthSquared < 1e-9) right = new Vector3D(1, 0, 0); else right.Normalize();
        var up = Vector3D.CrossProduct(right, forward); up.Normalize();
        var worldPerPixel = 2 * _distance * Math.Tan(42 * Math.PI / 360) / Math.Max(1, ActualHeight);
        var delta = current - _dragOrigin;
        _target = _panOrigin - right * (delta.X * worldPerPixel) + up * (delta.Y * worldPerPixel);
    }

    public void Fit()
    {
        var plan = Plan;
        if (plan is null || plan.IsEmpty) return;
        var points = plan.Paths.SelectMany(p => p.Points)
            .Concat(plan.Concrete.SelectMany(t => new[] { t.A, t.B, t.C })).ToArray();
        if (points.Length == 0) return;
        var minX = points.Min(p => p.Xmm); var maxX = points.Max(p => p.Xmm);
        var minY = points.Min(p => p.Ymm); var maxY = points.Max(p => p.Ymm);
        var minZ = points.Min(p => p.Zmm); var maxZ = points.Max(p => p.Zmm);
        _target = new Point3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        var span = Math.Max(maxZ - minZ, Math.Max(maxX - minX, maxY - minY));
        _minimumDistance = Math.Max(100, span * .12);
        _distance = Math.Max(800, span * 1.65);
        UpdateCamera();
    }

    private void Rebuild()
    {
        _concreteViewport.Children.Clear();
        _rebarViewport.Children.Clear();
        var plan = Plan;
        if (plan is null || plan.IsEmpty) return;
        var concreteGroup = LitGroup();
        AddConcrete(concreteGroup, plan.Concrete);
        _concreteViewport.Children.Add(new ModelVisual3D { Content = concreteGroup });

        var rebarGroup = LitGroup();
        var sides = plan.Paths.Count > 12000 ? 3 : plan.Paths.Count > 5000 ? 5 : 8;
        foreach (var byKind in plan.Paths.GroupBy(p => p.Kind))
        {
            var mesh = new MeshGeometry3D();
            foreach (var path in byKind) AddPath(mesh, path, sides);
            if (mesh.Positions.Count > 0) rebarGroup.Children.Add(Model(mesh, BarColor(byKind.Key), true));
        }
        _rebarViewport.Children.Add(new ModelVisual3D { Content = rebarGroup });
        if (!_hasFitted) { Fit(); _hasFitted = true; }
    }

    private static Model3DGroup LitGroup()
    {
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(205, 205, 205)));
        group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2)));
        return group;
    }

    private static void AddConcrete(Model3DGroup group, IReadOnlyList<FootingPreviewTriangle> triangles)
    {
        if (triangles.Count == 0) return;
        var faces = new MeshGeometry3D();
        var edges = new MeshGeometry3D();
        foreach (var triangle in triangles)
        {
            var start = faces.Positions.Count;
            faces.Positions.Add(ToPoint(triangle.A));
            faces.Positions.Add(ToPoint(triangle.B));
            faces.Positions.Add(ToPoint(triangle.C));
            faces.TriangleIndices.Add(start);
            faces.TriangleIndices.Add(start + 1);
            faces.TriangleIndices.Add(start + 2);
        }
        foreach (var edge in FootingConcreteEdgeBuilder.Build(triangles))
            AddTube(edges, edge.A, edge.B, 2.2, 4);
        if (faces.Positions.Count > 0)
            group.Children.Add(Model(faces, Color.FromArgb(38, 155, 165, 175), false));
        if (edges.Positions.Count > 0)
            group.Children.Add(Model(edges, Color.FromRgb(170, 180, 190), true));
    }

    private static void AddPath(MeshGeometry3D mesh, FootingPreviewPath path, int sides)
    {
        for (var i = 1; i < path.Points.Count; i++) AddTube(mesh, path.Points[i - 1], path.Points[i], path.DiameterMm / 2, sides);
        if (path.IsClosed && path.Points.Count > 2) AddTube(mesh, path.Points[^1], path.Points[0], path.DiameterMm / 2, sides);
    }

    private static GeometryModel3D Model(MeshGeometry3D mesh, Color color, bool emissive)
    {
        var materials = new MaterialGroup();
        materials.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        if (emissive) materials.Children.Add(new EmissiveMaterial(new SolidColorBrush(color)));
        return new GeometryModel3D(mesh, materials) { BackMaterial = materials };
    }

    private static void AddTube(MeshGeometry3D mesh, PreviewPoint3D a, PreviewPoint3D b, double radius, int sides)
    {
        var p0 = ToPoint(a); var p1 = ToPoint(b); var axis = p1 - p0;
        if (axis.Length < .01) return; axis.Normalize();
        var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Z) < .9 ? new Vector3D(0,0,1) : new Vector3D(0,1,0)); u.Normalize();
        var v = Vector3D.CrossProduct(axis, u); var start = mesh.Positions.Count;
        for (var end = 0; end < 2; end++)
        for (var j = 0; j < sides; j++)
        {
            var angle = j * Math.PI * 2 / sides;
            mesh.Positions.Add((end == 0 ? p0 : p1) + radius * (Math.Cos(angle) * u + Math.Sin(angle) * v));
        }
        for (var j = 0; j < sides; j++)
        {
            var n = (j + 1) % sides;
            foreach (var index in new[] { start+j, start+n, start+sides+n, start+j, start+sides+n, start+sides+j })
                mesh.TriangleIndices.Add(index);
        }
    }

    private Color BarColor(FootingPreviewBarKind kind) => kind switch
    {
        FootingPreviewBarKind.BottomX => Resource("Color.Preview.BottomX", Color.FromRgb(45,125,255)),
        FootingPreviewBarKind.BottomY => Resource("Color.Preview.BottomY", Color.FromRgb(30,210,220)),
        FootingPreviewBarKind.TopX => Resource("Color.Preview.TopX", Color.FromRgb(245,65,65)),
        FootingPreviewBarKind.TopY => Resource("Color.Preview.TopY", Color.FromRgb(255,145,35)),
        FootingPreviewBarKind.MidX => Resource("Color.Preview.MidX", Color.FromRgb(155,90,245)),
        FootingPreviewBarKind.MidY => Resource("Color.Preview.MidY", Color.FromRgb(245,80,175)),
        FootingPreviewBarKind.Chair => Resource("Color.Preview.Chair", Color.FromRgb(70,220,115)),
        _ => Resource("Color.Preview.Horizontal", Color.FromRgb(250,215,45))
    };

    private Color Resource(string key, Color fallback) => TryFindResource(key) is Color value ? value : fallback;
    private static Point3D ToPoint(PreviewPoint3D p) => new(p.Xmm, p.Ymm, p.Zmm);
    private Point3D CameraPosition()
    {
        var horizontal = _distance * Math.Cos(_pitch);
        return new Point3D(_target.X + horizontal * Math.Cos(_yaw), _target.Y + horizontal * Math.Sin(_yaw), _target.Z + _distance * Math.Sin(_pitch));
    }
    private void UpdateCamera()
    {
        var position = CameraPosition();
        var look = _target - position;
        _concreteViewport.Camera = new PerspectiveCamera(position, look, new Vector3D(0,0,1), 42);
        _rebarViewport.Camera = new PerspectiveCamera(position, look, new Vector3D(0,0,1), 42);
    }
}
