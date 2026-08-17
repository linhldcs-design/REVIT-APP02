using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RevitAPP.Core.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using WpfGrid = System.Windows.Controls.Grid;

namespace BeamRebarPro.Views;

/// <summary>
/// Xem thép dầm trong không gian, xoay và phóng được. Mỗi thanh dựng thành ống theo đúng đường kính
/// thật; bê tông vẽ dạng khung rỗng để không che thép bên trong.
/// </summary>
public sealed class BeamRebarPreview3D : WpfGrid
{
    public static readonly DependencyProperty PlanProperty = DependencyProperty.Register(
        nameof(Plan), typeof(BeamRebarGeometryPlan), typeof(BeamRebarPreview3D),
        new PropertyMetadata(null, (d, _) => ((BeamRebarPreview3D)d).Rebuild()));

    /// <summary>
    /// Số mặt cắt ngang của ống thép. Giữ ống tròn đầy đủ tới khi số thanh rất lớn, vì độ trung thực
    /// hình dạng được ưu tiên hơn tốc độ dựng.
    /// </summary>
    private const int SidesHigh = 8;
    private const int SidesMedium = 5;
    private const int SidesLow = 3;
    private const int MediumThreshold = 12_000;
    private const int LowThreshold = 18_000;

    private static readonly Color BackgroundColor = Color.FromRgb(35, 38, 42);

    private readonly Viewport3D _viewport = new();
    private Point3D _target;
    private double _distance = 5000;
    private double _minimumDistance = 100;
    private double _yaw = -.75;
    private double _pitch = .45;
    private Point _dragOrigin;
    private Point3D _panOrigin;
    private CameraDragMode _dragMode;
    private MouseButton? _dragButton;

    private enum CameraDragMode { None, Orbit, Pan }

    public BeamRebarGeometryPlan? Plan
    {
        get => (BeamRebarGeometryPlan?)GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    public BeamRebarPreview3D()
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(BackgroundColor);
        Children.Add(_viewport);
        // Thẻ bị ẩn vẫn nhận dữ liệu mới nhưng không hiển thị; dựng lại khi hiện ra để khớp dữ liệu.
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
            if (_dragMode == CameraDragMode.None || e.ChangedButton != _dragButton) return;
            EndDrag();
            e.Handled = true;
        };
        LostMouseCapture += (_, _) =>
        {
            _dragMode = CameraDragMode.None;
            _dragButton = null;
            Cursor = Cursors.Arrow;
        };
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                EndDrag();
                Fit();
                e.Handled = true;
                return;
            }

            BeginDrag(e.GetPosition(this), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? CameraDragMode.Pan
                : CameraDragMode.Orbit, MouseButton.Left);
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            BeginDrag(e.GetPosition(this), CameraDragMode.Pan, MouseButton.Middle);
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured || _dragMode == CameraDragMode.None) return;

        var stillPressed = _dragButton switch
        {
            MouseButton.Left => e.LeftButton == MouseButtonState.Pressed,
            MouseButton.Middle => e.MiddleButton == MouseButtonState.Pressed,
            _ => false
        };
        if (!stillPressed)
        {
            EndDrag();
            return;
        }

        var position = e.GetPosition(this);
        if (_dragMode == CameraDragMode.Orbit)
        {
            _yaw += (position.X - _dragOrigin.X) * .008;
            _pitch = Math.Clamp(_pitch - (position.Y - _dragOrigin.Y) * .008, -1.4, 1.4);
            _dragOrigin = position;
        }
        else
        {
            PanFromOrigin(position);
        }

        UpdateCamera();
        e.Handled = true;
    }

    private void BeginDrag(Point point, CameraDragMode mode, MouseButton button)
    {
        _dragOrigin = point;
        _panOrigin = _target;
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
        var camera = new Point3D(
            _target.X + horizontal * Math.Cos(_yaw),
            _target.Y + horizontal * Math.Sin(_yaw),
            _target.Z + _distance * Math.Sin(_pitch));

        var forward = _target - camera;
        forward.Normalize();
        var right = Vector3D.CrossProduct(forward, new Vector3D(0, 0, 1));
        if (right.LengthSquared < 1e-9) right = new Vector3D(1, 0, 0);
        else right.Normalize();
        var up = Vector3D.CrossProduct(right, forward);
        up.Normalize();

        var worldPerPixel = 2 * _distance * Math.Tan(42 * Math.PI / 360) / Math.Max(1, ActualHeight);
        var delta = current - _dragOrigin;
        _target = _panOrigin - right * (delta.X * worldPerPixel) + up * (delta.Y * worldPerPixel);
    }

    public void Fit()
    {
        var plan = Plan;
        if (plan is null || plan.IsEmpty) return;

        var points = plan.Paths.SelectMany(p => p.Points).ToArray();
        if (points.Length == 0) return;

        _target = new Point3D(
            (points.Min(p => p.Xmm) + points.Max(p => p.Xmm)) / 2,
            (points.Min(p => p.Ymm) + points.Max(p => p.Ymm)) / 2,
            (points.Min(p => p.Zmm) + points.Max(p => p.Zmm)) / 2);

        var span = Math.Max(
            points.Max(p => p.Zmm) - points.Min(p => p.Zmm),
            Math.Max(
                points.Max(p => p.Xmm) - points.Min(p => p.Xmm),
                points.Max(p => p.Ymm) - points.Min(p => p.Ymm)));

        _minimumDistance = Math.Max(100, span * .12);
        _distance = Math.Max(span * 1.65, 800);
        UpdateCamera();
    }

    private void Rebuild()
    {
        _viewport.Children.Clear();
        var plan = Plan;
        if (plan is null || plan.IsEmpty) return;

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(190, 190, 190)));
        group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -2)));

        foreach (var volume in plan.Context)
            group.Children.Add(ContextBox(volume));

        var sides = plan.Paths.Count switch
        {
            > LowThreshold => SidesLow,
            > MediumThreshold => SidesMedium,
            _ => SidesHigh
        };

        foreach (var byKind in plan.Paths.GroupBy(p => p.Kind))
        {
            var mesh = new MeshGeometry3D();
            foreach (var path in byKind)
                AddPath(mesh, path, sides);

            if (mesh.Positions.Count == 0) continue;
            group.Children.Add(BarModel(mesh, BarColor(byKind.Key)));
        }

        _viewport.Children.Add(new ModelVisual3D { Content = group });

        // Chỉ canh khung nhìn lần đầu: người dùng chỉnh thông số liên tục, tự canh lại mỗi lần sẽ
        // cướp mất góc nhìn họ vừa xoay tới.
        if (!_hasFitted)
        {
            Fit();
            _hasFitted = true;
        }
    }

    private bool _hasFitted;

    private static void AddPath(MeshGeometry3D mesh, BeamRebarPath path, int sides)
    {
        var radius = path.DiameterMm / 2;
        for (var i = 1; i < path.Points.Count; i++)
            AddTube(mesh, path.Points[i - 1], path.Points[i], radius, sides);

        if (path.IsClosedLoop && path.Points.Count > 2)
            AddTube(mesh, path.Points[^1], path.Points[0], radius, sides);
    }

    /// <summary>Thép phát sáng nhẹ để luôn thấy được bên trong khối bê tông, bất kể hướng nhìn.</summary>
    private static GeometryModel3D BarModel(MeshGeometry3D mesh, Color color)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(color)));
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    /// <summary>
    /// Bê tông vẽ bằng khung cạnh thay vì khối đặc: khối đặc ghi vào bộ đệm chiều sâu và che mất thép
    /// nằm bên trong.
    /// </summary>
    private static GeometryModel3D ContextBox(BeamRebarContextVolume volume)
    {
        var mesh = new MeshGeometry3D();
        var start = volume.StartCenterMm;
        var end = volume.EndCenterMm;

        var dx = end.Xmm - start.Xmm;
        var dy = end.Ymm - start.Ymm;
        var planLength = Math.Sqrt(dx * dx + dy * dy);
        var (ax, ay) = planLength < 1e-6 ? (1d, 0d) : (dx / planLength, dy / planLength);
        var (px, py) = (-ay, ax);

        var halfWidth = volume.WidthMm / 2;
        var halfHeight = volume.HeightMm / 2;

        GeometryPoint3D Corner(GeometryPoint3D centre, double lateral, double vertical) =>
            new(centre.Xmm + px * lateral, centre.Ymm + py * lateral, centre.Zmm + vertical);

        var startFace = new[]
        {
            Corner(start, -halfWidth, -halfHeight), Corner(start, halfWidth, -halfHeight),
            Corner(start, halfWidth, halfHeight), Corner(start, -halfWidth, halfHeight)
        };
        var endFace = new[]
        {
            Corner(end, -halfWidth, -halfHeight), Corner(end, halfWidth, -halfHeight),
            Corner(end, halfWidth, halfHeight), Corner(end, -halfWidth, halfHeight)
        };

        var edgeRadius = Math.Clamp(Math.Min(volume.WidthMm, volume.HeightMm) / 140d, 1.5, 5);
        for (var i = 0; i < 4; i++)
        {
            var next = (i + 1) % 4;
            AddTube(mesh, startFace[i], startFace[next], edgeRadius, 4);
            AddTube(mesh, endFace[i], endFace[next], edgeRadius, 4);
            AddTube(mesh, startFace[i], endFace[i], edgeRadius, 4);
        }

        var color = volume.Kind == BeamRebarContextKind.Beam
            ? Color.FromArgb(150, 185, 190, 194)
            : Color.FromArgb(170, 135, 140, 148);
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static void AddTube(
        MeshGeometry3D mesh, GeometryPoint3D a, GeometryPoint3D b, double radius, int sides)
    {
        var p0 = new Point3D(a.Xmm, a.Ymm, a.Zmm);
        var p1 = new Point3D(b.Xmm, b.Ymm, b.Zmm);
        var axis = p1 - p0;
        if (axis.Length < .01) return;
        axis.Normalize();

        var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Z) < .9 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0));
        u.Normalize();
        var v = Vector3D.CrossProduct(axis, u);

        var start = mesh.Positions.Count;
        for (var end = 0; end < 2; end++)
        {
            for (var j = 0; j < sides; j++)
            {
                var angle = j * Math.PI * 2 / sides;
                mesh.Positions.Add((end == 0 ? p0 : p1) + radius * (Math.Cos(angle) * u + Math.Sin(angle) * v));
            }
        }

        for (var j = 0; j < sides; j++)
        {
            var next = (j + 1) % sides;
            var a0 = start + j;
            var a1 = start + next;
            var b0 = start + sides + j;
            var b1 = start + sides + next;
            foreach (var index in new[] { a0, a1, b1, a0, b1, b0 })
                mesh.TriangleIndices.Add(index);
        }
    }

    private Color BarColor(BeamRebarPathKind kind) => kind switch
    {
        BeamRebarPathKind.MainTop or BeamRebarPathKind.MainBottom =>
            Resource("Color.RebarPreview3D", Color.FromRgb(235, 42, 42)),
        BeamRebarPathKind.AdditionalTop or BeamRebarPathKind.AdditionalBottom =>
            Resource("Color.RebarPreview3DAdditional", Color.FromRgb(255, 165, 50)),
        BeamRebarPathKind.StirrupSecondary =>
            Resource("Color.RebarPreview3DSecondary", Color.FromRgb(110, 225, 155)),
        _ => Resource("Color.RebarPreview3DStirrup", Color.FromRgb(80, 185, 255))
    };

    private Color Resource(string key, Color fallback) =>
        TryFindResource(key) is Color themed ? themed : fallback;

    private void UpdateCamera()
    {
        var horizontal = _distance * Math.Cos(_pitch);
        var position = new Point3D(
            _target.X + horizontal * Math.Cos(_yaw),
            _target.Y + horizontal * Math.Sin(_yaw),
            _target.Z + _distance * Math.Sin(_pitch));
        _viewport.Camera = new PerspectiveCamera(position, _target - position, new Vector3D(0, 0, 1), 42);
    }
}
