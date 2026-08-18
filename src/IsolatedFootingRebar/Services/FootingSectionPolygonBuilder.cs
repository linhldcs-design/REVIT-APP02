using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services;

/// <summary>Dựng đường bao mặt cắt ngang của mesh bê tông và lùi biên theo lớp bảo vệ.</summary>
public static class FootingSectionPolygonBuilder
{
    private const double PlaneToleranceMm = 0.01;
    private const double JoinToleranceMm = 0.75;

    public static IReadOnlyList<IReadOnlyList<PreviewPoint3D>> Build(
        IReadOnlyList<FootingPreviewTriangle> concrete, double zMm)
    {
        var segments = new List<Segment>();
        foreach (var triangle in concrete)
        {
            var hits = new List<PreviewPoint3D>(3);
            AddIntersection(hits, triangle.A, triangle.B, zMm);
            AddIntersection(hits, triangle.B, triangle.C, zMm);
            AddIntersection(hits, triangle.C, triangle.A, zMm);
            hits = Unique(hits);
            if (hits.Count < 2) continue;

            var pair = FarthestPair(hits);
            if (Distance(pair.A, pair.B) > JoinToleranceMm)
                segments.Add(pair);
        }

        var contours = new List<IReadOnlyList<PreviewPoint3D>>();
        while (segments.Count > 0)
        {
            var first = segments[^1];
            segments.RemoveAt(segments.Count - 1);
            var contour = new List<PreviewPoint3D> { first.A, first.B };
            while (!Near(contour[^1], contour[0]))
            {
                var index = segments.FindIndex(s => Near(s.A, contour[^1]) || Near(s.B, contour[^1]));
                if (index < 0) break;
                var next = segments[index];
                segments.RemoveAt(index);
                contour.Add(Near(next.A, contour[^1]) ? next.B : next.A);
            }

            if (contour.Count >= 4 && Near(contour[^1], contour[0]))
            {
                contour.RemoveAt(contour.Count - 1);
                RemoveCollinear(contour);
                if (contour.Count >= 3)
                {
                    if (SignedArea(contour) < 0) contour.Reverse();
                    contours.Add(contour);
                }
            }
        }

        return contours.OrderByDescending(p => Math.Abs(SignedArea(p))).ToArray();
    }

    public static IReadOnlyList<PreviewPoint3D> Inset(IReadOnlyList<PreviewPoint3D> polygon, double distanceMm)
    {
        if (polygon.Count < 3 || distanceMm < 0) return [];
        var source = SignedArea(polygon) < 0 ? polygon.Reverse().ToArray() : polygon.ToArray();
        var result = new List<PreviewPoint3D>(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var previous = source[(i - 1 + source.Length) % source.Length];
            var current = source[i];
            var next = source[(i + 1) % source.Length];
            var a = OffsetLine(previous, current, distanceMm);
            var b = OffsetLine(current, next, distanceMm);
            result.Add(Intersection(a, b, current, distanceMm));
        }

        RemoveCollinear(result);
        return result.Count >= 3 && Math.Abs(SignedArea(result)) > 1 &&
               IsSafeInset(source, result, distanceMm)
            ? result
            : [];
    }

    /// <summary>Cắt đường X/Y bằng một hay nhiều polygon đã offset; hỗ trợ polygon lõm trả nhiều khoảng.</summary>
    public static IReadOnlyList<FootingLineInterval> Clip(
        IEnumerable<IReadOnlyList<PreviewPoint3D>> polygons, bool alongX, double crossMm)
    {
        var intervals = new List<FootingLineInterval>();
        foreach (var polygon in polygons)
        {
            if (polygon.Count < 3) continue;
            var hits = new List<double>();
            var collinear = new List<FootingLineInterval>();
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                var ac = alongX ? a.Ymm : a.Xmm;
                var bc = alongX ? b.Ymm : b.Xmm;
                var aa = alongX ? a.Xmm : a.Ymm;
                var ba = alongX ? b.Xmm : b.Ymm;
                if (Math.Abs(ac - crossMm) <= JoinToleranceMm && Math.Abs(bc - crossMm) <= JoinToleranceMm)
                {
                    collinear.Add(new FootingLineInterval(Math.Min(aa, ba), Math.Max(aa, ba)));
                    continue;
                }
                if (!((ac <= crossMm && bc > crossMm) || (bc <= crossMm && ac > crossMm))) continue;
                var t = (crossMm - ac) / (bc - ac);
                hits.Add(aa + (ba - aa) * t);
            }

            hits.Sort();
            for (var i = 1; i < hits.Count; i += 2)
                if (hits[i] - hits[i - 1] > JoinToleranceMm)
                    intervals.Add(new FootingLineInterval(hits[i - 1], hits[i]));
            intervals.AddRange(collinear.Where(x => x.EndMm - x.StartMm > JoinToleranceMm));
        }
        return Merge(intervals);
    }

    public static bool Contains(IReadOnlyList<PreviewPoint3D> polygon, double xMm, double yMm)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            if (DistanceToSegment(xMm, yMm, a, b) <= JoinToleranceMm) return true;
            if ((a.Ymm > yMm) == (b.Ymm > yMm)) continue;
            var x = (b.Xmm - a.Xmm) * (yMm - a.Ymm) / (b.Ymm - a.Ymm) + a.Xmm;
            if (x > xMm) inside = !inside;
        }
        return inside;
    }

    private static void AddIntersection(List<PreviewPoint3D> hits, PreviewPoint3D a, PreviewPoint3D b, double z)
    {
        var da = a.Zmm - z;
        var db = b.Zmm - z;
        if (Math.Abs(da) <= PlaneToleranceMm && Math.Abs(db) <= PlaneToleranceMm) return;
        if (Math.Abs(da) <= PlaneToleranceMm) { hits.Add(a with { Zmm = z }); return; }
        if (Math.Abs(db) <= PlaneToleranceMm) { hits.Add(b with { Zmm = z }); return; }
        if ((da < 0) == (db < 0)) return;
        var t = da / (da - db);
        hits.Add(new PreviewPoint3D(a.Xmm + (b.Xmm - a.Xmm) * t, a.Ymm + (b.Ymm - a.Ymm) * t, z));
    }

    private static List<PreviewPoint3D> Unique(IEnumerable<PreviewPoint3D> points)
    {
        var result = new List<PreviewPoint3D>();
        foreach (var point in points)
            if (result.All(existing => !Near(existing, point))) result.Add(point);
        return result;
    }

    private static Segment FarthestPair(IReadOnlyList<PreviewPoint3D> points)
    {
        var result = new Segment(points[0], points[1]);
        var longest = Distance(result.A, result.B);
        for (var i = 0; i < points.Count; i++)
        for (var j = i + 1; j < points.Count; j++)
        {
            var length = Distance(points[i], points[j]);
            if (length <= longest) continue;
            longest = length;
            result = new Segment(points[i], points[j]);
        }
        return result;
    }

    private static Line2 OffsetLine(PreviewPoint3D a, PreviewPoint3D b, double distance)
    {
        var dx = b.Xmm - a.Xmm;
        var dy = b.Ymm - a.Ymm;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return new Line2(a.Xmm, a.Ymm, dx, dy);
        var nx = -dy / length * distance;
        var ny = dx / length * distance;
        return new Line2(a.Xmm + nx, a.Ymm + ny, dx, dy);
    }

    private static PreviewPoint3D Intersection(Line2 a, Line2 b, PreviewPoint3D fallback, double distance)
    {
        var cross = a.Dx * b.Dy - a.Dy * b.Dx;
        if (Math.Abs(cross) < 1e-9)
            return new PreviewPoint3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, fallback.Zmm);
        var t = ((b.X - a.X) * b.Dy - (b.Y - a.Y) * b.Dx) / cross;
        var point = new PreviewPoint3D(a.X + t * a.Dx, a.Y + t * a.Dy, fallback.Zmm);
        // Góc lõm/góc rất nhọn có thể sinh miter vô hạn; giới hạn để renderer/Revit không tạo thanh vọt ra xa.
        var maxMiter = Math.Max(10, distance * 8);
        return Distance(point, fallback) <= maxMiter ? point : fallback;
    }

    private static bool IsSafeInset(IReadOnlyList<PreviewPoint3D> source,
        IReadOnlyList<PreviewPoint3D> inset, double distance)
    {
        var tolerance = Math.Max(0.75, distance * 0.01);
        for (var i = 0; i < inset.Count; i++)
        {
            var a = inset[i]; var b = inset[(i + 1) % inset.Count];
            var middleX = (a.Xmm + b.Xmm) / 2;
            var middleY = (a.Ymm + b.Ymm) / 2;
            if (!Contains(source, a.Xmm, a.Ymm) || !Contains(source, middleX, middleY)) return false;
            for (var j = 0; j < source.Count; j++)
            {
                var c = source[j]; var d = source[(j + 1) % source.Count];
                if (SegmentDistance(a, b, c, d) < distance - tolerance) return false;
            }
        }

        for (var i = 0; i < inset.Count; i++)
        for (var j = i + 1; j < inset.Count; j++)
        {
            if (j == i + 1 || i == 0 && j == inset.Count - 1) continue;
            if (SegmentsIntersect(inset[i], inset[(i + 1) % inset.Count],
                    inset[j], inset[(j + 1) % inset.Count])) return false;
        }
        return true;
    }

    private static double SegmentDistance(PreviewPoint3D a, PreviewPoint3D b,
        PreviewPoint3D c, PreviewPoint3D d)
    {
        if (SegmentsIntersect(a, b, c, d)) return 0;
        return Math.Min(
            Math.Min(DistanceToSegment(a.Xmm, a.Ymm, c, d), DistanceToSegment(b.Xmm, b.Ymm, c, d)),
            Math.Min(DistanceToSegment(c.Xmm, c.Ymm, a, b), DistanceToSegment(d.Xmm, d.Ymm, a, b)));
    }

    private static double DistanceToSegment(double x, double y, PreviewPoint3D a, PreviewPoint3D b)
    {
        var dx = b.Xmm - a.Xmm; var dy = b.Ymm - a.Ymm;
        var length2 = dx * dx + dy * dy;
        if (length2 < 1e-12) return Math.Sqrt((x - a.Xmm) * (x - a.Xmm) + (y - a.Ymm) * (y - a.Ymm));
        var t = Math.Max(0, Math.Min(1, ((x - a.Xmm) * dx + (y - a.Ymm) * dy) / length2));
        var px = a.Xmm + t * dx; var py = a.Ymm + t * dy;
        return Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
    }

    private static bool SegmentsIntersect(PreviewPoint3D a, PreviewPoint3D b,
        PreviewPoint3D c, PreviewPoint3D d)
    {
        static double Cross(PreviewPoint3D p, PreviewPoint3D q, PreviewPoint3D r) =>
            (q.Xmm - p.Xmm) * (r.Ymm - p.Ymm) - (q.Ymm - p.Ymm) * (r.Xmm - p.Xmm);
        static bool OnSegment(PreviewPoint3D p, PreviewPoint3D q, PreviewPoint3D r) =>
            q.Xmm >= Math.Min(p.Xmm, r.Xmm) - JoinToleranceMm &&
            q.Xmm <= Math.Max(p.Xmm, r.Xmm) + JoinToleranceMm &&
            q.Ymm >= Math.Min(p.Ymm, r.Ymm) - JoinToleranceMm &&
            q.Ymm <= Math.Max(p.Ymm, r.Ymm) + JoinToleranceMm;
        var abC = Cross(a, b, c); var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
        if (abC * abD < -1e-6 && cdA * cdB < -1e-6) return true;
        return Math.Abs(abC) <= 1e-6 && OnSegment(a, c, b) ||
               Math.Abs(abD) <= 1e-6 && OnSegment(a, d, b) ||
               Math.Abs(cdA) <= 1e-6 && OnSegment(c, a, d) ||
               Math.Abs(cdB) <= 1e-6 && OnSegment(c, b, d);
    }

    private static IReadOnlyList<FootingLineInterval> Merge(List<FootingLineInterval> intervals)
    {
        if (intervals.Count == 0) return [];
        intervals.Sort((a, b) => a.StartMm.CompareTo(b.StartMm));
        var result = new List<FootingLineInterval> { intervals[0] };
        foreach (var interval in intervals.Skip(1))
        {
            var last = result[^1];
            if (interval.StartMm <= last.EndMm + JoinToleranceMm)
                result[^1] = new FootingLineInterval(last.StartMm, Math.Max(last.EndMm, interval.EndMm));
            else result.Add(interval);
        }
        return result;
    }

    private static void RemoveCollinear(List<PreviewPoint3D> points)
    {
        var changed = true;
        while (changed && points.Count >= 3)
        {
            changed = false;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[(i - 1 + points.Count) % points.Count];
                var b = points[i];
                var c = points[(i + 1) % points.Count];
                var cross = (b.Xmm - a.Xmm) * (c.Ymm - b.Ymm) - (b.Ymm - a.Ymm) * (c.Xmm - b.Xmm);
                var scale = Math.Max(1, Distance(a, b) + Distance(b, c));
                if (Math.Abs(cross) > 0.01 * scale) continue;
                points.RemoveAt(i);
                changed = true;
                break;
            }
        }
    }

    private static double SignedArea(IReadOnlyList<PreviewPoint3D> points)
    {
        var area = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            area += a.Xmm * b.Ymm - b.Xmm * a.Ymm;
        }
        return area / 2;
    }

    private static bool Near(PreviewPoint3D a, PreviewPoint3D b) => Distance(a, b) <= JoinToleranceMm;
    private static double Distance(PreviewPoint3D a, PreviewPoint3D b)
    {
        var dx = a.Xmm - b.Xmm; var dy = a.Ymm - b.Ymm;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private readonly record struct Segment(PreviewPoint3D A, PreviewPoint3D B);
    private readonly record struct Line2(double X, double Y, double Dx, double Dy);
}
