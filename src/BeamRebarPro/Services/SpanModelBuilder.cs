using BeamRebarPro.Models;

namespace BeamRebarPro.Services;

/// <summary>
///     Builds a continuous beam run from physical beam geometry. A run may come from several beam elements, or from
///     one long beam split into calculation spans by internal supports/columns.
/// </summary>
public static class SpanModelBuilder
{
    // Join/split tolerance: 50 mm. Hand-modeled beams/supports rarely hit exact points.
    private const double JoinTolFeet = 50.0 / 304.8;

    public static BeamRun Build(IReadOnlyList<BeamSegment> segments)
        => Build(segments, []);

    public static BeamRun Build(IReadOnlyList<BeamSegment> segments, IReadOnlyList<Point3> internalSupportPoints)
    {
        var warnings = new List<string>();

        if (segments.Count == 0)
            return new BeamRun([], [], ["Không có dầm nào để dựng nhịp."]);

        var ordered = OrderAlongAxis(segments);

        var spans = new List<Span>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var seg = ordered[i];
            foreach (var span in SplitSegmentBySupports(seg, internalSupportPoints))
                spans.Add(span with { Index = spans.Count });

            if (i <= 0) continue;

            var prev = ordered[i - 1];
            if (prev.End.DistanceTo(seg.Start) > JoinTolFeet)
                warnings.Add($"Nhịp {i - 1} và {i} không nối liền (lệch > 50mm) - thép liên tục có thể không tạo được.");
            if (Math.Abs(prev.Section.WidthMm - seg.Section.WidthMm) > 1 ||
                Math.Abs(prev.Section.HeightMm - seg.Section.HeightMm) > 1)
                warnings.Add($"Tiết diện đổi giữa nhịp {i - 1} và {i} - thép chủ liên tục sẽ bị cắt tại đây.");
        }

        var supports = BuildSupports(spans);
        return new BeamRun(spans, supports, warnings);
    }

    private static List<BeamSegment> OrderAlongAxis(IReadOnlyList<BeamSegment> segments)
    {
        if (segments.Count == 1) return [segments[0]];

        var reference = segments.OrderByDescending(s => s.LengthFeet).First();
        var ax = reference.End.X - reference.Start.X;
        var ay = reference.End.Y - reference.Start.Y;
        var len = Math.Sqrt(ax * ax + ay * ay);
        if (len < 1e-9) return segments.ToList();
        ax /= len;
        ay /= len;

        double Proj(Point3 p) => (p.X - reference.Start.X) * ax + (p.Y - reference.Start.Y) * ay;

        return segments
            .OrderBy(s => Proj(new Point3((s.Start.X + s.End.X) / 2, (s.Start.Y + s.End.Y) / 2, 0)))
            .ToList();
    }

    private static IEnumerable<Span> SplitSegmentBySupports(BeamSegment segment, IReadOnlyList<Point3> internalSupportPoints)
    {
        var axis = UnitAxis(segment);
        if (axis is null)
        {
            yield return new Span(0, segment.Start, segment.End, segment.Section);
            yield break;
        }

        var (ax, ay, az) = axis.Value;
        var length = segment.LengthFeet;
        var splitDistances = internalSupportPoints
            .Select(p => ProjectDistance(segment.Start, p, ax, ay, az))
            .Where(d => d > JoinTolFeet && d < length - JoinTolFeet)
            .DistinctBy(d => Math.Round(d / JoinTolFeet))
            .OrderBy(d => d)
            .ToList();

        var points = new List<Point3> { segment.Start };
        points.AddRange(splitDistances.Select(d => PointAt(segment.Start, ax, ay, az, d)));
        points.Add(segment.End);

        for (var i = 0; i < points.Count - 1; i++)
            yield return new Span(i, points[i], points[i + 1], segment.Section);
    }

    private static List<Support> BuildSupports(IReadOnlyList<Span> spans)
    {
        var supports = new List<Support>();
        supports.Add(new Support(0, spans[0].Start, IsEnd: true));
        for (var i = 0; i < spans.Count - 1; i++)
            supports.Add(new Support(i + 1, spans[i].End, IsEnd: false));
        supports.Add(new Support(spans.Count, spans[^1].End, IsEnd: true));
        return supports;
    }

    private static (double X, double Y, double Z)? UnitAxis(BeamSegment segment)
    {
        var x = segment.End.X - segment.Start.X;
        var y = segment.End.Y - segment.Start.Y;
        var z = segment.End.Z - segment.Start.Z;
        var len = Math.Sqrt(x * x + y * y + z * z);
        return len < 1e-9 ? null : (x / len, y / len, z / len);
    }

    private static double ProjectDistance(Point3 origin, Point3 point, double ax, double ay, double az)
        => (point.X - origin.X) * ax + (point.Y - origin.Y) * ay + (point.Z - origin.Z) * az;

    private static Point3 PointAt(Point3 origin, double ax, double ay, double az, double distance)
        => new(origin.X + ax * distance, origin.Y + ay * distance, origin.Z + az * distance);
}
