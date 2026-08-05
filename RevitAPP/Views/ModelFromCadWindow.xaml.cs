using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using RevitAPP.Core.Models.CadStructure;
using RevitAPP.ViewModels;
using Line = System.Windows.Shapes.Line;
using Point = System.Windows.Point;

namespace RevitAPP.Views;

public partial class ModelFromCadWindow : Window
{
    private const double BaseCanvasSize = 900.0;
    private const double CanvasPadding = 42.0;

    private readonly ModelFromCadViewModel _viewModel;
    private Point _dragOrigin;
    private ScrollViewer? _dragViewer;
    private Canvas? _dragCanvas;
    private Point _orbitOrigin;
    private bool _isOrbiting;
    private bool _cameraInitialized;
    private double _cameraYaw = -0.93;
    private double _cameraPitch = 0.48;
    private double _cameraDistance = 5000.0;
    private double _cameraMinimumDistance = 100.0;
    private Point3D _cameraTarget;

    internal ModelFromCadWindow(ModelFromCadViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.CloseRequested += (_, confirmed) =>
        {
            DialogResult = confirmed;
            Close();
        };
        viewModel.RenderRequested += (_, _) => RenderAll();
        viewModel.ReselectCompleted += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _cameraInitialized = false;
            RenderAll();
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });

        Loaded += (_, _) => RenderAll();
        AttachInteraction(GridPreviewScroll, GridPreviewCanvas);
        AttachInteraction(ColumnPreviewScroll, ColumnPreviewCanvas);
        ColumnPreview3DHost.PreviewMouseWheel += On3DMouseWheel;
        ColumnPreview3DHost.MouseLeftButtonDown += On3DOrbitStart;
        ColumnPreview3DHost.MouseMove += On3DOrbitMove;
        ColumnPreview3DHost.MouseLeftButtonUp += On3DOrbitEnd;
    }

    private void AttachInteraction(ScrollViewer viewer, Canvas canvas)
    {
        viewer.PreviewMouseWheel += (_, args) => OnPreviewMouseWheel(viewer, canvas, args);
        canvas.MouseLeftButtonDown += (_, args) => OnDragStart(viewer, canvas, args);
        canvas.MouseMove += (_, args) => OnDragMove(viewer, args);
        canvas.MouseLeftButtonUp += (_, args) => OnDragEnd(args);
    }

    private void RenderAll()
    {
        if (_viewModel.SelectedMode == ModelFromCadMode.Grid)
        {
            RenderGrid();
            return;
        }

        if (_viewModel.PreviewModeIndex == 0) RenderColumns();
        else RenderColumns3D();
    }

    private void RenderGrid()
    {
        GridPreviewCanvas.Children.Clear();
        var axes = _viewModel.GridAxes;
        var bounds = BoundsOf(
            axes.SelectMany(axis => new[]
            {
                RotateForColumn(new CadStructurePoint2(axis.Axis.Start.Xmm, axis.Axis.Start.Ymm)),
                RotateForColumn(new CadStructurePoint2(axis.Axis.End.Xmm, axis.Axis.End.Ymm))
            }));
        var viewport = Prepare(GridPreviewCanvas, bounds);

        foreach (var axis in axes)
        {
            var start = viewport.ToCanvas(RotateForColumn(
                new CadStructurePoint2(axis.Axis.Start.Xmm, axis.Axis.Start.Ymm)));
            var end = viewport.ToCanvas(RotateForColumn(
                new CadStructurePoint2(axis.Axis.End.Xmm, axis.Axis.End.Ymm)));
            var line = new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = axis.IsSelected
                    ? Brush(axis.IsSkew ? "Brush.Danger" : "Brush.Accent")
                    : Brush("Brush.TextSecondary"),
                StrokeThickness = axis.IsSelected ? 1.7 : 1.0,
                Opacity = axis.IsSelected ? 1.0 : 0.4,
                StrokeDashArray = axis.IsSelected ? null : new DoubleCollection { 4, 3 }
            };
            GridPreviewCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = axis.Name,
                Foreground = line.Stroke,
                Opacity = line.Opacity,
                FontSize = 11
            };
            Canvas.SetLeft(label, end.X + 4);
            Canvas.SetTop(label, end.Y - 16);
            GridPreviewCanvas.Children.Add(label);
        }
    }

    private void RenderColumns()
    {
        ColumnPreviewCanvas.Children.Clear();
        var points = _viewModel.Columns.SelectMany(row => row.Candidate.CornersMm.Select(RotateForColumn))
            .Concat(_viewModel.GridAxes.SelectMany(axis => new[]
            {
                RotateForColumn(new CadStructurePoint2(axis.Axis.Start.Xmm, axis.Axis.Start.Ymm)),
                RotateForColumn(new CadStructurePoint2(axis.Axis.End.Xmm, axis.Axis.End.Ymm))
            }));
        var viewport = Prepare(ColumnPreviewCanvas, BoundsOf(points));

        if (_viewModel.ShowGridOverlay)
        {
            foreach (var axis in _viewModel.GridAxes)
            {
                var start = viewport.ToCanvas(RotateForColumn(
                    new CadStructurePoint2(axis.Axis.Start.Xmm, axis.Axis.Start.Ymm)));
                var end = viewport.ToCanvas(RotateForColumn(
                    new CadStructurePoint2(axis.Axis.End.Xmm, axis.Axis.End.Ymm)));
                ColumnPreviewCanvas.Children.Add(new Line
                {
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = end.X,
                    Y2 = end.Y,
                    Stroke = Brush("Brush.TextSecondary"),
                    StrokeThickness = 1.0,
                    Opacity = 0.38
                });
            }
        }

        foreach (var row in _viewModel.Columns)
        {
            var polygon = new Polygon
            {
                Points = new PointCollection(row.Candidate.CornersMm.Select(RotateForColumn).Select(viewport.ToCanvas)),
                Stroke = row.IsIncluded ? Brush("Brush.Accent") : Brush("Brush.TextSecondary"),
                Fill = row.IsIncluded ? Brush("Brush.Accent") : Brushes.Transparent,
                StrokeThickness = row == _viewModel.SelectedColumn ? 3.0 : 1.8,
                Opacity = row.IsIncluded ? 0.72 : 0.35,
                Cursor = Cursors.Hand,
                Tag = row
            };
            polygon.MouseLeftButtonDown += OnColumnClicked;
            ColumnPreviewCanvas.Children.Add(polygon);

            if (!_viewModel.ShowColumnLabels) continue;
            var center = viewport.ToCanvas(RotateForColumn(row.Candidate.CenterMm));
            var label = new TextBlock
            {
                Text = $"C{row.Number}  {row.Candidate.WidthMm:0}×{row.Candidate.HeightMm:0}",
                Foreground = Brush("Brush.Text"),
                Background = Brush("Brush.Background"),
                FontSize = 11,
                Padding = new Thickness(3, 1, 3, 1),
                Opacity = 0.9
            };
            Canvas.SetLeft(label, center.X + 5);
            Canvas.SetTop(label, center.Y - 18);
            ColumnPreviewCanvas.Children.Add(label);
        }
    }

    private void RenderColumns3D()
    {
        ColumnPreview3D.Children.Clear();
        var selected = _viewModel.Columns.Where(row => row.IsIncluded).ToArray();
        var allPlanPoints = selected.SelectMany(row => row.Candidate.CornersMm.Select(RotateForColumn))
            .Concat(_viewModel.GridAxes.SelectMany(axis => new[]
            {
                RotateForColumn(new CadStructurePoint2(axis.Axis.Start.Xmm, axis.Axis.Start.Ymm)),
                RotateForColumn(new CadStructurePoint2(axis.Axis.End.Xmm, axis.Axis.End.Ymm))
            })).ToArray();
        var bounds = BoundsOf(allPlanPoints);
        var planSpan = Math.Max(Math.Max(bounds.MaxX - bounds.MinX, bounds.MaxY - bounds.MinY), 1000.0);
        var heightMm = PreviewHeightMm();
        var centerX = (bounds.MinX + bounds.MaxX) / 2.0;
        var centerY = (bounds.MinY + bounds.MaxY) / 2.0;
        var centerZ = heightMm / 2.0;
        var cameraDistance = Math.Max(planSpan, heightMm) * 1.8;

        _cameraTarget = new Point3D(centerX, centerY, centerZ);
        _cameraMinimumDistance = Math.Max(Math.Min(planSpan, heightMm) * 0.15, 100.0);
        if (!_cameraInitialized)
        {
            _cameraDistance = cameraDistance;
            _cameraYaw = -0.93;
            _cameraPitch = 0.48;
            _cameraInitialized = true;
        }
        Update3DCamera();

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Colors.DimGray));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, 1, -2)));
        ColumnPreview3D.Children.Add(new ModelVisual3D { Content = lights });

        var models = new Model3DGroup();
        if (_viewModel.ShowGridOverlay)
        {
            foreach (var axis in _viewModel.GridAxes)
            {
                var start = RotateForColumn(new CadStructurePoint2(axis.Axis.Start.Xmm, axis.Axis.Start.Ymm));
                var end = RotateForColumn(new CadStructurePoint2(axis.Axis.End.Xmm, axis.Axis.End.Ymm));
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-6) continue;
                var offset = new CadStructurePoint2(-dy / length * 10, dx / length * 10);
                models.Children.Add(BoxModel(new[]
                {
                    start + offset, end + offset, end - offset, start - offset
                }, 5, ColorOf("Brush.TextSecondary"), 0.45));
            }
        }

        foreach (var row in selected)
        {
            var corners = row.Candidate.CornersMm.Select(RotateForColumn).ToArray();
            models.Children.Add(BoxModel(corners, heightMm, ColorOf("Brush.Accent"), 0.82));
        }
        ColumnPreview3D.Children.Add(new ModelVisual3D { Content = models });
    }

    private void On3DMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 0.84 : 1.19;
        _cameraDistance = Math.Clamp(
            _cameraDistance * factor,
            _cameraMinimumDistance,
            _cameraMinimumDistance * 200.0);
        Update3DCamera();
        e.Handled = true;
    }

    private void On3DOrbitStart(object sender, MouseButtonEventArgs e)
    {
        _orbitOrigin = e.GetPosition(ColumnPreview3DHost);
        _isOrbiting = true;
        ColumnPreview3DHost.CaptureMouse();
        e.Handled = true;
    }

    private void On3DOrbitMove(object sender, MouseEventArgs e)
    {
        if (!_isOrbiting || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(ColumnPreview3DHost);
        _cameraYaw += (current.X - _orbitOrigin.X) * 0.008;
        _cameraPitch = Math.Clamp(
            _cameraPitch - (current.Y - _orbitOrigin.Y) * 0.008,
            -1.45,
            1.45);
        _orbitOrigin = current;
        Update3DCamera();
        e.Handled = true;
    }

    private void On3DOrbitEnd(object sender, MouseButtonEventArgs e)
    {
        _isOrbiting = false;
        ColumnPreview3DHost.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Update3DCamera()
    {
        var horizontal = _cameraDistance * Math.Cos(_cameraPitch);
        var position = new Point3D(
            _cameraTarget.X + horizontal * Math.Cos(_cameraYaw),
            _cameraTarget.Y + horizontal * Math.Sin(_cameraYaw),
            _cameraTarget.Z + _cameraDistance * Math.Sin(_cameraPitch));
        ColumnPreview3D.Camera = new PerspectiveCamera
        {
            Position = position,
            LookDirection = _cameraTarget - position,
            UpDirection = new Vector3D(0, 0, 1),
            FieldOfView = 42
        };
    }

    private GeometryModel3D BoxModel(
        IReadOnlyList<CadStructurePoint2> corners,
        double height,
        System.Windows.Media.Color color,
        double opacity)
    {
        var mesh = new MeshGeometry3D();
        foreach (var corner in corners) mesh.Positions.Add(new Point3D(corner.X, corner.Y, 0));
        foreach (var corner in corners) mesh.Positions.Add(new Point3D(corner.X, corner.Y, height));
        var triangles = new[]
        {
            0,2,1, 0,3,2, 4,5,6, 4,6,7,
            0,1,5, 0,5,4, 1,2,6, 1,6,5,
            2,3,7, 2,7,6, 3,0,4, 3,4,7
        };
        foreach (var index in triangles) mesh.TriangleIndices.Add(index);
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        var material = new DiffuseMaterial(brush);
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private double PreviewHeightMm()
    {
        if (_viewModel.SelectedBaseLevel is null || _viewModel.SelectedTopLevel is null)
            return 3000.0;
        var height = (_viewModel.SelectedTopLevel.Elevation - _viewModel.SelectedBaseLevel.Elevation) * 304.8
                     + _viewModel.TopOffsetMm - _viewModel.BaseOffsetMm;
        return Math.Max(height, 100.0);
    }

    private CadStructurePoint2 RotateForColumn(CadStructurePoint2 point)
    {
        var radians = _viewModel.RotationDegrees * Math.PI / 180.0;
        if (Math.Abs(radians) < 1e-12) return point;
        var anchor = _viewModel.Data.AnchorPreviewMm;
        var x = point.X - anchor.X;
        var y = point.Y - anchor.Y;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new CadStructurePoint2(
            anchor.X + x * cosine - y * sine,
            anchor.Y + x * sine + y * cosine);
    }

    private System.Windows.Media.Color ColorOf(string key) => Brush(key) is SolidColorBrush brush
        ? brush.Color
        : Colors.Gray;

    private void OnColumnClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Polygon { Tag: CadColumnRowViewModel row }) return;
        _viewModel.SelectedColumn = row;
        row.IsIncluded = !row.IsIncluded;
        e.Handled = true;
    }

    private Viewport Prepare(Canvas canvas, Bounds bounds)
    {
        var canvasSize = BaseCanvasSize * _viewModel.Zoom;
        canvas.Width = canvasSize;
        canvas.Height = canvasSize;
        var width = Math.Max(bounds.MaxX - bounds.MinX, 1.0);
        var height = Math.Max(bounds.MaxY - bounds.MinY, 1.0);
        var scale = (canvasSize - 2 * CanvasPadding) / Math.Max(width, height);
        return new Viewport(bounds, canvasSize, scale);
    }

    private static Bounds BoundsOf(IEnumerable<CadStructurePoint2> source)
    {
        var points = source.ToArray();
        return points.Length == 0
            ? new Bounds(0, 0, 1, 1)
            : new Bounds(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.X),
                points.Max(point => point.Y));
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private void OnPreviewMouseWheel(
        ScrollViewer viewer,
        Canvas canvas,
        MouseWheelEventArgs e)
    {
        var before = _viewModel.Zoom;
        var cursor = e.GetPosition(canvas);
        if (e.Delta > 0) _viewModel.ZoomInCommand.Execute(null);
        else _viewModel.ZoomOutCommand.Execute(null);
        var ratio = _viewModel.Zoom / before;
        if (Math.Abs(ratio - 1.0) > 1e-9)
        {
            var viewerPoint = e.GetPosition(viewer);
            viewer.ScrollToHorizontalOffset(cursor.X * ratio - viewerPoint.X);
            viewer.ScrollToVerticalOffset(cursor.Y * ratio - viewerPoint.Y);
        }
        e.Handled = true;
    }

    private void OnDragStart(ScrollViewer viewer, Canvas canvas, MouseButtonEventArgs e)
    {
        // Shapes consume clicks for selection. Drag starts only on the canvas background.
        if (e.OriginalSource is Shape) return;
        _dragOrigin = e.GetPosition(viewer);
        _dragViewer = viewer;
        _dragCanvas = canvas;
        canvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMove(ScrollViewer viewer, MouseEventArgs e)
    {
        if (_dragViewer != viewer || _dragCanvas is null) return;
        var current = e.GetPosition(viewer);
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - (current.X - _dragOrigin.X));
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - (current.Y - _dragOrigin.Y));
        _dragOrigin = current;
        e.Handled = true;
    }

    private void OnDragEnd(MouseButtonEventArgs e)
    {
        if (_dragCanvas is null) return;
        _dragCanvas.ReleaseMouseCapture();
        _dragCanvas = null;
        _dragViewer = null;
        e.Handled = true;
    }

    private readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY);

    private readonly record struct Viewport(Bounds Bounds, double CanvasSize, double Scale)
    {
        public Point ToCanvas(CadStructurePoint2 point) => new(
            CanvasPadding + (point.X - Bounds.MinX) * Scale,
            CanvasSize - CanvasPadding - (point.Y - Bounds.MinY) * Scale);
    }
}
