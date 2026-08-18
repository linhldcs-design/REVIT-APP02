using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services;

/// <summary>Dựng toàn bộ centerline thép bằng mm, không truy cập Revit API.</summary>
public static class FootingRebarPreviewFactory
{
    private static readonly FootingPreviewBarKind[] MeshKinds =
    [
        FootingPreviewBarKind.BottomX, FootingPreviewBarKind.BottomY,
        FootingPreviewBarKind.TopX, FootingPreviewBarKind.TopY,
        FootingPreviewBarKind.MidX, FootingPreviewBarKind.MidY
    ];

    public static FootingRebarPreviewPlan Build(FootingGeometry geometry, FootingRebarModel model)
    {
        var widthX = FootingMath.FeetToMm(geometry.WidthXFeet);
        var widthY = FootingMath.FeetToMm(geometry.WidthYFeet);
        var height = FootingMath.FeetToMm(geometry.BaseHeightFeet);
        Validate(widthX, widthY, height, model);

        var paths = new List<FootingPreviewPath>();
        var concrete = Concrete(geometry);
        if (model.BottomEnabled)
            AddMesh(paths, concrete, widthX, widthY, height, model.Cover, model.BottomX, model.BottomY,
                false, FootingPreviewBarKind.BottomX, FootingPreviewBarKind.BottomY);
        if (model.TopEnabled)
            AddMesh(paths, concrete, widthX, widthY, height, model.Cover, model.TopX, model.TopY,
                true, FootingPreviewBarKind.TopX, FootingPreviewBarKind.TopY);
        if (model.MidEnabled)
            AddMid(paths, concrete, widthX, widthY, height, model);
        if (model.VerticalEnabled)
            AddChairs(paths, concrete, widthX, widthY, height, model);
        if (model.HorizontalEnabled)
            AddHorizontal(paths, concrete, height, model);

        return new FootingRebarPreviewPlan(paths, concrete);
    }

    public static bool RequiresIndividualMeshBars(
        FootingRebarPreviewPlan plan, FootingGeometry geometry, FootingRebarModel model)
    {
        var enabledKinds = new List<FootingPreviewBarKind>();
        if (model.BottomEnabled)
        {
            if (model.BottomX.Enabled) enabledKinds.Add(FootingPreviewBarKind.BottomX);
            if (model.BottomY.Enabled) enabledKinds.Add(FootingPreviewBarKind.BottomY);
        }
        if (model.TopEnabled)
        {
            if (model.TopX.Enabled) enabledKinds.Add(FootingPreviewBarKind.TopX);
            if (model.TopY.Enabled) enabledKinds.Add(FootingPreviewBarKind.TopY);
        }
        if (model.MidEnabled)
        {
            if (model.MidX.Enabled) enabledKinds.Add(FootingPreviewBarKind.MidX);
            if (model.MidY.Enabled) enabledKinds.Add(FootingPreviewBarKind.MidY);
        }

        var meshPaths = plan.Paths.Where(path => MeshKinds.Contains(path.Kind)).ToArray();
        foreach (var kind in enabledKinds)
        {
            var paths = meshPaths.Where(path => path.Kind == kind).ToArray();
            if (paths.Length == 0) return true;
            var alongX = kind is FootingPreviewBarKind.BottomX or FootingPreviewBarKind.TopX or FootingPreviewBarKind.MidX;
            var diameter = paths[0].DiameterMm;
            var expected = FootingMath.FeetToMm(alongX ? geometry.WidthXFeet : geometry.WidthYFeet)
                           - 2 * (model.Cover.SideMm + diameter / 2d);
            if (paths.Any(path => Math.Abs(LongestHorizontalSegment(path) - expected) > 1))
                return true;
        }
        return false;
    }

    private static double LongestHorizontalSegment(FootingPreviewPath path)
    {
        var longest = 0d;
        for (var i = 1; i < path.Points.Count; i++)
        {
            var a = path.Points[i - 1]; var b = path.Points[i];
            if (Math.Abs(a.Zmm - b.Zmm) > 0.1) continue;
            var dx = b.Xmm - a.Xmm; var dy = b.Ymm - a.Ymm;
            longest = Math.Max(longest, Math.Sqrt(dx * dx + dy * dy));
        }
        return longest;
    }

    private static void Validate(double widthX, double widthY, double height, FootingRebarModel model)
    {
        if (widthX <= 0 || widthY <= 0 || height <= 0)
            throw new ArgumentException("Hình học móng không hợp lệ.");
        if (model.Cover.SideMm < 0 || model.Cover.BottomMm < 0 || model.Cover.TopMm < 0)
            throw new ArgumentException("Lớp bảo vệ không được âm.");
        if (widthX <= 2 * model.Cover.SideMm || widthY <= 2 * model.Cover.SideMm ||
            height <= model.Cover.BottomMm + model.Cover.TopMm)
            throw new ArgumentException("Móng quá nhỏ so với lớp bê tông bảo vệ.");
        if (model.MidEnabled && model.MidLayers < 1)
            throw new ArgumentException("Số lớp thép giữa phải lớn hơn 0.");
        if (model.HorizontalEnabled && model.Horizontal.Layers < 1)
            throw new ArgumentException("Số lớp đai ngang phải lớn hơn 0.");
    }

    private static IReadOnlyList<FootingPreviewTriangle> Concrete(FootingGeometry geometry)
    {
        if (geometry.ConcreteTriangles.Count == 0)
            return BoxFallback(geometry);

        var origin = geometry.BaseCenter;
        var x = geometry.DirX;
        var y = geometry.DirY;
        PreviewPoint3D Local(Point3 p)
        {
            var dx = p.X - origin.X;
            var dy = p.Y - origin.Y;
            var dz = p.Z - geometry.BottomZFeet;
            return new PreviewPoint3D(
                FootingMath.FeetToMm(dx * x.X + dy * x.Y),
                FootingMath.FeetToMm(dx * y.X + dy * y.Y),
                FootingMath.FeetToMm(dz));
        }

        return geometry.ConcreteTriangles
            .Select(t => new FootingPreviewTriangle(Local(t.A), Local(t.B), Local(t.C)))
            .ToArray();
    }

    private static IReadOnlyList<FootingPreviewTriangle> BoxFallback(FootingGeometry geometry)
    {
        var x = FootingMath.FeetToMm(geometry.WidthXFeet) / 2;
        var y = FootingMath.FeetToMm(geometry.WidthYFeet) / 2;
        var z = FootingMath.FeetToMm(geometry.BaseHeightFeet);
        var p = new[]
        {
            new PreviewPoint3D(-x,-y,0), new PreviewPoint3D(x,-y,0), new PreviewPoint3D(x,y,0), new PreviewPoint3D(-x,y,0),
            new PreviewPoint3D(-x,-y,z), new PreviewPoint3D(x,-y,z), new PreviewPoint3D(x,y,z), new PreviewPoint3D(-x,y,z)
        };
        int[][] faces = [[0,1,2],[0,2,3],[4,6,5],[4,7,6],[0,4,5],[0,5,1],[1,5,6],[1,6,2],[2,6,7],[2,7,3],[3,7,4],[3,4,0]];
        return faces.Select(f => new FootingPreviewTriangle(p[f[0]], p[f[1]], p[f[2]])).ToArray();
    }

    private static void AddMesh(List<FootingPreviewPath> paths, IReadOnlyList<FootingPreviewTriangle> concrete,
        double wx, double wy, double h,
        CoverSettings cover, LayerBarConfig x, LayerBarConfig y, bool top,
        FootingPreviewBarKind kindX, FootingPreviewBarKind kindY)
    {
        if (x.Enabled)
        {
            var z = top ? h - cover.TopMm - x.Diameter.Millimeters / 2d : cover.BottomMm + x.Diameter.Millimeters / 2d;
            AddDirection(paths, concrete, wx, wy, h, cover, x, true, z, top, kindX);
        }
        if (y.Enabled)
        {
            var stack = x.Diameter.Millimeters;
            var z = top ? h - cover.TopMm - y.Diameter.Millimeters / 2d - stack : cover.BottomMm + y.Diameter.Millimeters / 2d + stack;
            AddDirection(paths, concrete, wx, wy, h, cover, y, false, z, top, kindY);
        }
    }

    private static void AddMid(List<FootingPreviewPath> paths, IReadOnlyList<FootingPreviewTriangle> concrete,
        double wx, double wy, double h, FootingRebarModel model)
    {
        var lo = model.Cover.BottomMm;
        var hi = h - model.Cover.TopMm;
        for (var i = 0; i < model.MidLayers; i++)
        {
            var baseZ = lo + (hi - lo) * (i + 1) / (model.MidLayers + 1d);
            if (model.MidX.Enabled)
                AddDirection(paths, concrete, wx, wy, h, model.Cover, model.MidX, true,
                    baseZ + model.MidX.Diameter.Millimeters / 2d, false, FootingPreviewBarKind.MidX);
            if (model.MidY.Enabled)
                AddDirection(paths, concrete, wx, wy, h, model.Cover, model.MidY, false,
                    baseZ + model.MidX.Diameter.Millimeters + model.MidY.Diameter.Millimeters / 2d,
                    false, FootingPreviewBarKind.MidY);
        }
    }

    private static void AddDirection(List<FootingPreviewPath> paths, IReadOnlyList<FootingPreviewTriangle> concrete,
        double wx, double wy, double h,
        CoverSettings cover, LayerBarConfig config, bool alongX, double z, bool top, FootingPreviewBarKind kind)
    {
        if (config.Diameter.Millimeters <= 0) throw new ArgumentException("Đường kính thép phải lớn hơn 0.");
        var clearance = cover.SideMm + config.Diameter.Millimeters / 2d;
        var inset = InsetContours(concrete, z, clearance);
        if (inset.Count == 0) return;
        var crossValues = inset.SelectMany(polygon => polygon)
            .Select(point => alongX ? point.Ymm : point.Xmm).ToArray();
        var crossMin = crossValues.Min();
        var field = crossValues.Max() - crossMin;
        var run = field;
        if (run <= 0 || field <= 0) throw new ArgumentException("Không đủ không gian bố trí thép.");
        var count = LayoutCount(field, config.UseSpacing, config.SpacingMm, config.Count);
        foreach (var offset in FootingMath.EvenPositions(field, count))
        {
            var cross = crossMin + offset;
            foreach (var interval in FootingSectionPolygonBuilder.Clip(inset, alongX, cross))
            {
                var start = interval.StartMm;
                var end = interval.EndMm;
                if (end - start <= config.Diameter.Millimeters) continue;
                var a = alongX ? new PreviewPoint3D(start, cross, z) : new PreviewPoint3D(cross, start, z);
                var b = alongX ? new PreviewPoint3D(end, cross, z) : new PreviewPoint3D(cross, end, z);
                var points = new List<PreviewPoint3D>();
                var desiredHook = config.HookEnabled
                    ? Math.Min(config.HookLengthMm, top ? z - cover.BottomMm : h - cover.TopMm - z)
                    : 0;
                var hook = SafeHookLength(concrete, a, b, desiredHook, top, clearance);
                if (hook > 0) points.Add(a with { Zmm = z + (top ? -hook : hook) });
                points.Add(a); points.Add(b);
                if (hook > 0) points.Add(b with { Zmm = z + (top ? -hook : hook) });
                paths.Add(new FootingPreviewPath(kind, config.Diameter.Millimeters, points));
            }
        }
    }

    private static int LayoutCount(double field, bool useSpacing, double spacing, int count)
    {
        if (!useSpacing) return Math.Max(1, count);
        if (spacing <= 0) throw new ArgumentException("Khoảng cách thép phải lớn hơn 0.");
        return Math.Max(2, (int)Math.Ceiling(field / spacing) + 1);
    }

    private static void AddChairs(List<FootingPreviewPath> paths,
        IReadOnlyList<FootingPreviewTriangle> concrete, double wx, double wy, double h, FootingRebarModel model)
    {
        var c = model.Vertical;
        var d = c.Diameter.Millimeters;
        var usableX = wx - 2 * model.Cover.SideMm;
        var usableY = wy - 2 * model.Cover.SideMm;
        var bottomStack = model.BottomEnabled ? model.BottomX.Diameter.Millimeters + model.BottomY.Diameter.Millimeters : 0;
        var topStack = model.TopEnabled ? model.TopX.Diameter.Millimeters + model.TopY.Diameter.Millimeters : 0;
        var footZ = model.Cover.BottomMm + bottomStack + d / 2d;
        var topZ = h - model.Cover.TopMm - topStack - d / 2d;
        if (topZ <= footZ + d) return;
        var topSpan = Math.Min(c.WidthMm > 0 ? c.WidthMm : topZ - footZ, usableX);
        var foot = Math.Max(0, c.HookLengthMm);
        // DowelCreator luôn dùng SpacingX để tính khoảng lùi mép, kể cả khi bố trí theo số lượng.
        var spacingX = c.UseSpacing ? c.SpacingXMm : usableX / Math.Max(1, c.CountX);
        var spacingY = c.UseSpacing ? c.SpacingYMm : usableY / Math.Max(1, c.CountY);
        if (spacingX <= 0 || spacingY <= 0) throw new ArgumentException("Khoảng cách thép đứng phải lớn hơn 0.");
        var marginX = (c.UseSpacing ? Math.Max(spacingX / 2, 100) : 100) + foot + topSpan / 2;
        var marginY = c.UseSpacing ? Math.Max(spacingY / 2, 100) : 100;
        var fieldX = wx - 2 * (model.Cover.SideMm + marginX);
        var fieldY = wy - 2 * (model.Cover.SideMm + marginY);
        if (fieldX <= 0 || fieldY <= 0) return;
        // DowelCreator tính số hàng X bằng FootingMath.SpacingToCount (floor + 1), nên preview phải dùng đúng công thức đó.
        var nx = c.UseSpacing
            ? Math.Max(1, FootingMath.SpacingToCount(fieldX, c.SpacingXMm))
            : Math.Max(1, c.CountX);
        // CreateChairRow chỉ bật MaximumSpacing khi fieldY lớn hơn spacingY; ngược lại chỉ có một ghế.
        var ny = c.UseSpacing
            ? fieldY > spacingY ? LayoutCount(fieldY, true, spacingY, c.CountY) : 1
            : Math.Max(1, c.CountY);
        foreach (var xOffset in FootingMath.EvenPositions(fieldX, nx))
        foreach (var yOffset in FootingMath.EvenPositions(fieldY, ny))
        {
            var x = -fieldX / 2 + xOffset;
            var y = -fieldY / 2 + yOffset;
            var left = x - topSpan / 2;
            var right = x + topSpan / 2;
            var points = new PreviewPoint3D[]
            {
                new PreviewPoint3D(left-foot,y,footZ), new PreviewPoint3D(left,y,footZ),
                new PreviewPoint3D(left,y,topZ), new PreviewPoint3D(right,y,topZ),
                new PreviewPoint3D(right,y,footZ), new PreviewPoint3D(right+foot,y,footZ)
            };
            if (PathInsideConcrete(points, concrete, model.Cover.SideMm + d / 2d))
                paths.Add(new FootingPreviewPath(FootingPreviewBarKind.Chair, d, points));
        }
    }

    private static void AddHorizontal(List<FootingPreviewPath> paths,
        IReadOnlyList<FootingPreviewTriangle> concrete, double h, FootingRebarModel model)
    {
        var c = model.Horizontal;
        var d = c.DiameterX.Millimeters;
        var bottom = model.Cover.BottomMm + d / 2d;
        var top = h - model.Cover.TopMm - d / 2d;
        for (var i = 0; i < c.Layers; i++)
        {
            var z = top <= bottom ? h / 2 : bottom + (top - bottom) * (i + 1) / (c.Layers + 1d);
            foreach (var rawContour in FootingSectionPolygonBuilder.Build(concrete, z))
            {
                var contour = FootingSectionPolygonBuilder.Inset(rawContour, model.Cover.SideMm + d / 2d);
                if (contour.Count < 3) continue;
                var points = c.Closed ? contour : OpenContour(contour, c, d);
                if (points.Count >= 2)
                    paths.Add(new FootingPreviewPath(FootingPreviewBarKind.Horizontal, d, points, c.Closed));
            }
        }
    }

    private static IReadOnlyList<PreviewPoint3D> OpenContour(
        IReadOnlyList<PreviewPoint3D> contour, HorizontalStirrupConfig config, double diameter)
    {
        // Mở cạnh có trung điểm nhỏ nhất theo X. Chuỗi đi vòng theo các cạnh còn lại nên vẫn bám toàn bộ đa giác.
        var edgeIndex = Enumerable.Range(0, contour.Count)
            .MinBy(i => (contour[i].Xmm + contour[(i + 1) % contour.Count].Xmm) / 2d);
        var a = contour[edgeIndex];
        var b = contour[(edgeIndex + 1) % contour.Count];
        var dx = b.Xmm - a.Xmm; var dy = b.Ymm - a.Ymm;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 2) return contour;
        var gap = Math.Min(length - 1, Math.Max(50, diameter * 6));
        var ux = dx / length; var uy = dy / length;
        var lower = new PreviewPoint3D(a.Xmm + ux * gap / 2, a.Ymm + uy * gap / 2, a.Zmm);
        var upper = new PreviewPoint3D(b.Xmm - ux * gap / 2, b.Ymm - uy * gap / 2, b.Zmm);
        var route = new List<PreviewPoint3D> { upper };
        for (var step = 1; step < contour.Count; step++)
            route.Add(contour[(edgeIndex + 1 + step) % contour.Count]);
        route.Add(lower);

        var hook = config.HookEnabled ? Math.Max(0, config.HookLengthMm) : 0;
        if (hook > 0)
        {
            // Polygon đã chuẩn hóa ngược chiều kim đồng hồ: pháp tuyến trái của cạnh mở luôn hướng vào bê tông.
            var inwardX = -uy;
            var inwardY = ux;
            PreviewPoint3D HookEnd(PreviewPoint3D p) =>
                new(p.Xmm + inwardX * hook, p.Ymm + inwardY * hook, p.Zmm);
            route.Insert(0, HookEnd(route[0]));
            route.Add(HookEnd(route[^1]));
        }
        return route;
    }

    private static bool PathInsideConcrete(IReadOnlyList<PreviewPoint3D> points,
        IReadOnlyList<FootingPreviewTriangle> concrete, double clearanceMm)
    {
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1]; var b = points[i];
            if (Math.Abs(a.Zmm - b.Zmm) <= 0.01 && Math.Abs(a.Ymm - b.Ymm) <= 0.01)
            {
                var inset = InsetContours(concrete, a.Zmm, clearanceMm);
                var min = Math.Min(a.Xmm, b.Xmm); var max = Math.Max(a.Xmm, b.Xmm);
                if (!FootingSectionPolygonBuilder.Clip(inset, true, a.Ymm)
                    .Any(interval => min >= interval.StartMm - 0.5 && max <= interval.EndMm + 0.5)) return false;
            }
            else if (Math.Abs(a.Xmm - b.Xmm) <= 0.01 && Math.Abs(a.Ymm - b.Ymm) <= 0.01)
            {
                if (!VerticalSegmentInside(concrete, a, b, clearanceMm)) return false;
            }
            else if (!SegmentSamplesInside(concrete, a, b, clearanceMm)) return false;
        }
        return true;
    }

    private static double SafeHookLength(IReadOnlyList<FootingPreviewTriangle> concrete,
        PreviewPoint3D a, PreviewPoint3D b, double desired, bool top, double clearance)
    {
        if (desired <= 0) return 0;
        bool Fits(double length)
        {
            var z = a.Zmm + (top ? -length : length);
            return PathInsideConcrete([a, a with { Zmm = z }], concrete, clearance) &&
                   PathInsideConcrete([b, b with { Zmm = z }], concrete, clearance);
        }
        if (Fits(desired)) return desired;
        var low = 0d; var high = desired;
        for (var i = 0; i < 18; i++)
        {
            var middle = (low + high) / 2;
            if (Fits(middle)) low = middle; else high = middle;
        }
        return low >= 1 ? low : 0;
    }

    private static IReadOnlyList<IReadOnlyList<PreviewPoint3D>> InsetContours(
        IReadOnlyList<FootingPreviewTriangle> concrete, double z, double clearance)
        => FootingSectionPolygonBuilder.Build(concrete, z)
            .Select(contour => FootingSectionPolygonBuilder.Inset(contour, clearance))
            .Where(contour => contour.Count >= 3)
            .Cast<IReadOnlyList<PreviewPoint3D>>()
            .ToArray();

    private static bool PointInside(IReadOnlyList<FootingPreviewTriangle> concrete,
        PreviewPoint3D point, double clearance)
        => InsetContours(concrete, point.Zmm, clearance)
            .Any(contour => FootingSectionPolygonBuilder.Contains(contour, point.Xmm, point.Ymm));

    private static bool VerticalSegmentInside(IReadOnlyList<FootingPreviewTriangle> concrete,
        PreviewPoint3D a, PreviewPoint3D b, double clearance)
    {
        var lo = Math.Min(a.Zmm, b.Zmm); var hi = Math.Max(a.Zmm, b.Zmm);
        var levels = concrete.SelectMany(t => new[] { t.A.Zmm, t.B.Zmm, t.C.Zmm })
            .Where(z => z > lo + 0.01 && z < hi - 0.01)
            .Append(lo).Append(hi).Distinct().OrderBy(z => z).ToArray();
        for (var i = 0; i < levels.Length; i++)
        {
            if (!PointInside(concrete, a with { Zmm = levels[i] }, clearance)) return false;
            if (i > 0 && !PointInside(concrete, a with { Zmm = (levels[i - 1] + levels[i]) / 2 }, clearance))
                return false;
        }
        return true;
    }

    private static bool SegmentSamplesInside(IReadOnlyList<FootingPreviewTriangle> concrete,
        PreviewPoint3D a, PreviewPoint3D b, double clearance)
    {
        var dx = b.Xmm - a.Xmm; var dy = b.Ymm - a.Ymm; var dz = b.Zmm - a.Zmm;
        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        var samples = Math.Max(2, (int)Math.Ceiling(length / 25));
        for (var i = 0; i <= samples; i++)
        {
            var t = i / (double)samples;
            if (!PointInside(concrete, new PreviewPoint3D(
                    a.Xmm + dx * t, a.Ymm + dy * t, a.Zmm + dz * t), clearance)) return false;
        }
        return true;
    }
}
