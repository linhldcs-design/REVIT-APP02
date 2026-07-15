using BeamRebar.Core.Models;

namespace BeamRebar.Core.Services;

/// <summary>
///     Một đoạn dầm thẳng đã đọc geometry — input thuần cho <see cref="SpanModelBuilder"/>.
///     <paramref name="TopElevationFeet"/>/<paramref name="BottomElevationFeet"/> là cao độ thật của
///     mặt trên/dưới dầm (đọc từ bounding box), KHÔNG suy từ Z của trục — tránh đặt thép ngoài host.
/// </summary>
public sealed record BeamSegment(
    Point3 Start,
    Point3 End,
    BeamSection Section,
    double TopElevationFeet,
    double BottomElevationFeet);

/// <summary>Kết quả dựng span model kèm cảnh báo (vd dầm không liền mạch, tiết diện khác nhau).</summary>
public sealed record SpanModelResult(BeamRun Run, IReadOnlyList<string> Warnings);

/// <summary>
///     Dựng <see cref="BeamRun"/> từ các đoạn dầm đã pick: sắp xếp theo trục chung, nối liên tục,
///     suy ra gối tại điểm nối + hai đầu mút. Logic thuần (Point3, không Revit) → test xUnit.
/// </summary>
public static class SpanModelBuilder
{
    /// <summary>Dung sai (feet) coi hai điểm là trùng nhau ≈ 50mm.</summary>
    private const double JoinToleranceFeet = 50.0 / 304.8;

    public static SpanModelResult Build(IReadOnlyList<BeamSegment> segments)
    {
        var warnings = new List<string>();

        if (segments.Count == 0)
            return new SpanModelResult(new BeamRun([], []), ["Không có đoạn dầm nào để dựng nhịp."]);

        // Sắp xếp các đoạn dọc trục chung: chiếu điểm giữa lên hướng của đoạn đầu tiên.
        var axisDir = Normalize(Subtract(segments[0].End, segments[0].Start));
        var ordered = segments
            .OrderBy(s => Dot(Midpoint(s.Start, s.End), axisDir))
            .ToList();

        // Mỗi đoạn = 1 span; đảo chiều start/end để các span nối đầu-đuôi theo trục.
        var spans = new List<Span>(ordered.Count);
        var sections = new List<BeamSection>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var seg = ordered[i];
            var (start, end) = Dot(seg.Start, axisDir) <= Dot(seg.End, axisDir)
                ? (seg.Start, seg.End)
                : (seg.End, seg.Start);

            spans.Add(new Span(i, start, end, seg.Section));
            sections.Add(seg.Section);

            // Cảnh báo nếu đầu span này không nối liền đuôi span trước.
            if (i > 0 && spans[i - 1].End.DistanceTo(start) > JoinToleranceFeet)
                warnings.Add($"Nhịp {i - 1} và {i} không nối liền mạch — gối được suy theo điểm gần nhất.");
        }

        if (sections.Distinct().Count() > 1)
            warnings.Add("Các nhịp có tiết diện khác nhau — thép được tạo theo tiết diện từng nhịp.");

        var supports = BuildSupports(spans);
        return new SpanModelResult(new BeamRun(spans, supports), warnings);
    }

    private static List<Support> BuildSupports(IReadOnlyList<Span> spans)
    {
        var supports = new List<Support>(spans.Count + 1);

        // Gối đầu = đầu span 0.
        supports.Add(new Support(spans[0].Start, spans[0].Section.WidthMm / 304.8, IsEnd: true));

        // Gối giữa = điểm nối giữa các span.
        for (var i = 1; i < spans.Count; i++)
            supports.Add(new Support(spans[i].Start, spans[i].Section.WidthMm / 304.8, IsEnd: false));

        // Gối cuối = đuôi span cuối.
        var last = spans[^1];
        supports.Add(new Support(last.End, last.Section.WidthMm / 304.8, IsEnd: true));

        return supports;
    }

    private static Point3 Subtract(Point3 a, Point3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static Point3 Midpoint(Point3 a, Point3 b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
    private static double Dot(Point3 a, Point3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static Point3 Normalize(Point3 v)
    {
        var len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-9 ? new Point3(1, 0, 0) : new Point3(v.X / len, v.Y / len, v.Z / len);
    }
}
