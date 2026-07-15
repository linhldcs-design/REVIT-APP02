using Autodesk.Revit.DB;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services;

/// <summary>
///     Tự động dò các cột kết cấu (Structural Columns) giao với dầm để làm điểm chia nhịp. Một cột được
///     coi là "cắt qua" dầm khi tâm cột (theo XY) nằm gần trục dầm trong dung sai. Nhờ vậy người dùng
///     không phải chọn cột thủ công: dầm dài qua nhiều cột tự chia thành nhiều nhịp tính toán.
/// </summary>
public sealed class ColumnDetector
{
    // Dung sai khoảng cách ngang từ tâm cột tới trục dầm (mm). Giảm còn 250mm để không bắt nhầm cột
    // lưới khác / cột song song. Cột thật của dầm nằm gần đúng trên trục.
    private const double ProximityMm = 250.0;
    private const double ProximityFeet = ProximityMm / 304.8;

    // Dung sai cao độ (mm): cột chỉ tính là gối nếu bbox theo Z CHẠM vùng cao độ dầm (loại cột tầng khác
    // cùng vị trí XY). 600mm cho phép cột đứng dưới đỡ đáy dầm hoặc xuyên qua dầm.
    private const double ZTouchToleranceFeet = 600.0 / 304.8;

    private readonly List<(XYZ Center, BoundingBoxXYZ? Bbox)> _columns;
    // Dầm giao (Structural Framing) — để dò gối là dầm giao (không phải cột) tại biên/giữa, lấy bề rộng THẬT.
    private readonly List<(Line Axis, double WidthFeet, BoundingBoxXYZ? Bbox, List<XYZ> Verts)> _crossBeams;

    public ColumnDetector(Document document)
    {
        _columns = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Select(c => (Center: GetCenterXY(c), Bbox: c.get_BoundingBox(null)))
            .Where(c => c.Center is not null)
            .Select(c => (Center: c.Center!, Bbox: (BoundingBoxXYZ?)c.Bbox))
            .ToList();

        _crossBeams = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Select(b => (Axis: (b.Location as LocationCurve)?.Curve as Line, Width: ReadBeamWidthFeet(b),
                Bbox: b.get_BoundingBox(null), Verts: ReadSolidVerticesXy(b)))
            .Where(b => b.Axis is not null && b.Width > 0)
            .Select(b => (Axis: b.Axis!, WidthFeet: b.Width, Bbox: (BoundingBoxXYZ?)b.Bbox, Verts: b.Verts))
            .ToList();
    }

    /// <summary>Đọc các đỉnh solid dầm giao (XY, feet) để đo vùng dầm giao chiếm trên trục dầm chính THẬT
    ///     (theo hình dạng solid, không phình như bbox axis-aligned).</summary>
    private static List<XYZ> ReadSolidVerticesXy(FamilyInstance beam)
    {
        var verts = new List<XYZ>();
        var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Medium };
        var geom = beam.get_Geometry(opt);
        if (geom is null) return verts;
        foreach (var solid in EnumerateSolidsCb(geom))
            foreach (Edge e in solid.Edges)
                foreach (var p in e.Tessellate())
                    verts.Add(new XYZ(p.X, p.Y, 0));
        return verts;
    }

    private static IEnumerable<Solid> EnumerateSolidsCb(GeometryElement geom)
    {
        foreach (var obj in geom)
        {
            if (obj is Solid { Volume: > 1e-9 } s) yield return s;
            else if (obj is GeometryInstance gi)
                foreach (var inner in EnumerateSolidsCb(gi.GetInstanceGeometry()))
                    yield return inner;
        }
    }

    private static double ReadBeamWidthFeet(FamilyInstance beam)
    {
        foreach (var name in new[] { "b", "Width", "Chiều rộng" })
        {
            var p = beam.Symbol.LookupParameter(name);
            if (p is { StorageType: StorageType.Double } && p.AsDouble() > 0) return p.AsDouble();
        }
        return 0;
    }

    /// <summary>Điểm cột/dầm-giao trên trục dầm + nửa bề rộng (feet). <see cref="EdgeMinFeet"/>/<see cref="EdgeMaxFeet"/>
    ///     là 2 mép của vùng cột/dầm-giao chiếu lên trục dầm chính (station feet từ segment.Start), để tính
    ///     mép trong THẬT dù dầm giao nằm lệch hẳn 1 phía (XY tâm có thể trùng điểm gối → không dùng proj được).</summary>
    public sealed record ColumnHit(Point3 Location, double HalfWidthFeet, double EdgeMinFeet = double.NaN, double EdgeMaxFeet = double.NaN);


    /// <summary>
    ///     Tìm các điểm trên trục dầm nơi có cột cắt qua (trừ hai đầu mút dầm), kèm nửa bề rộng cột để
    ///     tính chiều dài thép gia cường TỪ MÉP CỘT.
    /// </summary>
    public IReadOnlyList<ColumnHit> FindInternalSupports(IReadOnlyList<BeamSegment> segments)
    {
        // Quy tắc kết cấu: cột ĐẦU và cột CUỐI của dải dầm là GỐI BIÊN (không chia nhịp). Chỉ cột nằm
        // GIỮA (không phải đầu/cuối) mới chia nhịp. → dầm qua 2 cột = 1 nhịp; qua 3 cột = 2 nhịp...
        if (segments.Count == 0) return [];

        // Hướng trục tham chiếu = đoạn đầu (dầm thẳng); chiếu mọi cột lên trục để sắp thứ tự dọc dầm.
        var origin = new XYZ(segments[0].Start.X, segments[0].Start.Y, segments[0].Start.Z);
        var endRef = new XYZ(segments[^1].End.X, segments[^1].End.Y, segments[^1].End.Z);
        var refAxis = endRef - origin;
        if (refAxis.GetLength() < 1e-6) return [];
        var refDirXy = new XYZ(refAxis.X, refAxis.Y, 0).Normalize();

        var ordered = FindAllColumnHits(segments)
            // Khử trùng lặp cột (cùng vị trí từ nhiều segment) theo khoảng cách dọc trục.
            .GroupBy(h => Math.Round(Proj(h.Location) / ProximityFeet))
            .Select(g => g.First())
            .OrderBy(h => Proj(h.Location))
            .ToList();

        // Bỏ cột đầu + cột cuối (gối biên). Còn lại là cột giữa → điểm chia nhịp.
        return ordered.Count <= 2 ? [] : ordered.Skip(1).Take(ordered.Count - 2).ToList();

        double Proj(Point3 p) => (p.X - origin.X) * refDirXy.X + (p.Y - origin.Y) * refDirXy.Y;
    }

    /// <summary>Backward-compat: chỉ lấy điểm (không width).</summary>
    public IReadOnlyList<Point3> FindInternalSupportPoints(IReadOnlyList<BeamSegment> segments)
        => FindInternalSupports(segments).Select(h => h.Location).ToList();

    /// <summary>
    ///     TẤT CẢ cột giao trục dầm (kể cả 2 đầu mút) + nửa bề rộng — để gán width cho mọi gối (gồm biên),
    ///     phục vụ tính thép gia cường từ mép cột. Khác <see cref="FindInternalSupports"/> ở chỗ KHÔNG
    ///     loại cột đầu mút.
    /// </summary>
    public IReadOnlyList<ColumnHit> FindAllColumnHits(IReadOnlyList<BeamSegment> segments)
    {
        var hits = new List<ColumnHit>();

        foreach (var segment in segments)
        {
            var start = new XYZ(segment.Start.X, segment.Start.Y, segment.Start.Z);
            var end = new XYZ(segment.End.X, segment.End.Y, segment.End.Z);
            var axis = end - start;
            var length = axis.GetLength();
            if (length < 1e-6) continue;
            var dir = axis / length;
            var dirXy = new XYZ(dir.X, dir.Y, 0);

            foreach (var (center, bbox) in _columns)
            {
                var v = new XYZ(center.X - start.X, center.Y - start.Y, 0);
                var t = v.DotProduct(dirXy);
                if (t < -ProximityFeet || t > length + ProximityFeet) continue; // gồm cả 2 đầu mút.

                var clampedOnAxis = start + dir * Math.Clamp(t, 0, length);
                var distXy = Math.Sqrt(
                    (center.X - clampedOnAxis.X) * (center.X - clampedOnAxis.X) +
                    (center.Y - clampedOnAxis.Y) * (center.Y - clampedOnAxis.Y));
                if (distXy > ProximityFeet) continue;

                // Lọc cao độ: cột phải CHẠM vùng cao độ dầm (loại cột tầng khác cùng XY).
                if (!ColumnTouchesBeam(bbox, segment)) continue;

                var onAxis = start + dir * t;
                hits.Add(new ColumnHit(new Point3(onAxis.X, onAxis.Y, onAxis.Z), HalfWidthAlong(bbox, dirXy)));
            }
        }

        return hits;
    }

    /// <summary>
    ///     Tìm các dầm GIAO (Structural Framing khác) cắt ngang trục dầm chính + nửa bề rộng THẬT của dầm
    ///     giao chiếu lên phương dầm chính. Dùng để đai/thép lùi khỏi gối là dầm giao (không phải cột).
    /// </summary>
    public IReadOnlyList<ColumnHit> FindCrossBeamHits(IReadOnlyList<BeamSegment> segments)
    {
        var hits = new List<ColumnHit>();

        foreach (var segment in segments)
        {
            var start = new XYZ(segment.Start.X, segment.Start.Y, segment.Start.Z);
            var end = new XYZ(segment.End.X, segment.End.Y, segment.End.Z);
            var axis = end - start;
            var length = axis.GetLength();
            if (length < 1e-6) continue;
            var dir = axis / length;
            var dirXy = new XYZ(dir.X, dir.Y, 0).Normalize();

            foreach (var (cbAxis, widthFeet, bbox, verts) in _crossBeams)
            {
                var cbA = cbAxis.GetEndPoint(0);
                var cbB = cbAxis.GetEndPoint(1);
                var cbDirXy = new XYZ(cbB.X - cbA.X, cbB.Y - cbA.Y, 0);
                if (cbDirXy.GetLength() < 1e-6) continue;
                cbDirXy = cbDirXy.Normalize();

                // Bỏ qua dầm gần SONG SONG trục chính (chính nó / dầm nối tiếp, không phải dầm giao).
                if (Math.Abs(cbDirXy.DotProduct(dirXy)) > 0.95) continue;

                // GIAO ĐIỂM 2 ĐƯỜNG TRỤC trên mặt phẳng XY (cơ sở hình học thật, không chiếu tâm).
                // Giải: start + dir*t = cbA + cbDir*u. t = vị trí dọc dầm chính; u = vị trí dọc dầm giao (feet).
                if (!TryIntersectXy(start, dirXy, cbA, cbDirXy, out var t, out var u)) continue;

                // Giao điểm phải nằm trong nhịp dầm chính (gồm 2 đầu mút).
                if (t < -ProximityFeet || t > length + ProximityFeet) continue;

                // Giao điểm phải nằm TRONG đoạn dầm giao [0, cbLen] (u đo từ cbA). Cho dung sai để dầm giao
                // gác kiểu chữ T (đầu dừng tại mép dầm chính) vẫn được nhận.
                var cbLenFeet = cbA.DistanceTo(cbB);
                if (u < -widthFeet || u > cbLenFeet + widthFeet) continue;

                if (!ColumnTouchesBeam(bbox, segment)) continue;

                // CƠ SỞ: chiếu BBOX dầm giao lên trục dầm chính → vùng [tMin,tMax] (station feet từ
                // segment.Start). Kiểm chứng MCP: dầm giao vuông góc cho bbox khớp đúng bề rộng (b=400 →
                // vùng=400). KHÔNG dùng solid-vertex vì đỉnh solid phân bố theo CHIỀU DÀI dầm giao (perp lớn),
                // lọc theo bề rộng dầm chính chỉ giữ vài đỉnh cùng station → vùng rỗng → half=0 (đai không lùi).
                if (!ProjectBboxOntoAxis(bbox, start, dirXy, out var tMin, out var tMax))
                {
                    var sinAngle = Math.Sqrt(Math.Max(1e-6, 1 - Math.Pow(cbDirXy.DotProduct(dirXy), 2)));
                    var halfA = widthFeet / 2 / sinAngle;
                    tMin = t - halfA;
                    tMax = t + halfA;
                }

                var centerT = (tMin + tMax) / 2;
                var halfAlongFeet = (tMax - tMin) / 2;

                var onAxis = start + dir * centerT;
                // EdgeMin/Max = station tuyệt đối (feet từ segment.Start) của 2 mép vùng dầm giao trên trục.
                hits.Add(new ColumnHit(new Point3(onAxis.X, onAxis.Y, onAxis.Z), halfAlongFeet, tMin, tMax));
            }
        }

        return hits;
    }

    /// <summary>
    ///     Giao 2 đường thẳng XY: p1 + d1*t = p2 + d2*u. Trả t, u (tham số dọc mỗi đường, đơn vị feet vì
    ///     d1/d2 đã normalize). false nếu gần song song.
    /// </summary>
    private static bool TryIntersectXy(XYZ p1, XYZ d1, XYZ p2, XYZ d2, out double t, out double u)
    {
        t = 0; u = 0;
        var denom = d1.X * d2.Y - d1.Y * d2.X; // cross 2D của 2 hướng.
        if (Math.Abs(denom) < 1e-9) return false; // song song.
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        t = (dx * d2.Y - dy * d2.X) / denom;
        u = (dx * d1.Y - dy * d1.X) / denom;
        return true;
    }

    /// <summary>Chiếu 8 góc bbox lên trục (origin + dirXy) → [tMin, tMax] feet. false nếu bbox null.</summary>
    private static bool ProjectBboxOntoAxis(BoundingBoxXYZ? bbox, XYZ origin, XYZ dirXy, out double tMin, out double tMax)
    {
        tMin = 0; tMax = 0;
        if (bbox is null) return false;
        var min = bbox.Min;
        var max = bbox.Max;
        tMin = double.MaxValue;
        tMax = double.MinValue;
        foreach (var cx in new[] { min.X, max.X })
        foreach (var cy in new[] { min.Y, max.Y })
        {
            var t = (cx - origin.X) * dirXy.X + (cy - origin.Y) * dirXy.Y;
            if (t < tMin) tMin = t;
            if (t > tMax) tMax = t;
        }
        return true;
    }

    private static double DistancePointToLineXy(XYZ point, Line line)
    {
        var a = line.GetEndPoint(0);
        var b = line.GetEndPoint(1);
        var ab = new XYZ(b.X - a.X, b.Y - a.Y, 0);
        var abLen = ab.GetLength();
        if (abLen < 1e-9) return double.MaxValue;
        var abDir = ab / abLen;
        var ap = new XYZ(point.X - a.X, point.Y - a.Y, 0);
        var proj = ap.DotProduct(abDir);
        var closest = new XYZ(a.X + abDir.X * proj, a.Y + abDir.Y * proj, 0);
        return Math.Sqrt((point.X - closest.X) * (point.X - closest.X) + (point.Y - closest.Y) * (point.Y - closest.Y));
    }

    // Nửa bề rộng cột chiếu lên phương dầm = nửa hình chiếu của bbox cột lên trục dầm.
    private static double HalfWidthAlong(BoundingBoxXYZ? bbox, XYZ dirXy)
    {
        if (bbox is null) return 0;
        var size = bbox.Max - bbox.Min;
        // Hình chiếu hộp chữ nhật (sizeX, sizeY) lên phương dir = |dx*dirx| + |dy*diry|.
        var projected = Math.Abs(size.X * dirXy.X) + Math.Abs(size.Y * dirXy.Y);
        return projected / 2;
    }

    // Cột chạm dầm theo Z: khoảng [min,max] của cột phủ lấn vùng cao độ dầm trong dung sai → loại cột
    // tầng trên/dưới có cùng vị trí XY (nguyên nhân dò nhầm 16 cột).
    private static bool ColumnTouchesBeam(BoundingBoxXYZ? bbox, BeamSegment segment)
    {
        if (bbox is null) return true; // không có bbox → không loại (an toàn).
        var beamBottom = segment.BottomElevationFeet;
        var beamTop = segment.TopElevationFeet;
        return bbox.Max.Z >= beamBottom - ZTouchToleranceFeet
            && bbox.Min.Z <= beamTop + ZTouchToleranceFeet;
    }

    private static XYZ? GetCenterXY(FamilyInstance column)
    {
        if (column.Location is LocationPoint lp)
            return lp.Point;

        var bbox = column.get_BoundingBox(null);
        if (bbox is null) return null;
        return (bbox.Min + bbox.Max) / 2;
    }

    /// <summary>
    ///     Gán nửa bề rộng cột vào gối tương ứng (match theo vị trí gần nhất trong dung sai). Gối GIỮA
    ///     không match cột nào (gối do dầm giao thêm thủ công ở mục 9) sẽ được gán
    ///     <paramref name="defaultInternalHalfWidthFeet"/> để đai/thép vẫn lùi khỏi vùng dầm giao,
    ///     KHÔNG xuyên vào gối đó.
    /// </summary>
    public static BeamRun EnrichSupportsWithColumnWidth(BeamRun run, IReadOnlyList<ColumnHit> hits,
        double defaultInternalHalfWidthFeet = 0)
    {
        const double SupportToleranceFeet = 60.0 / 304.8;
        const double ColumnFaceMatchToleranceFeet = 100.0 / 304.8;

        var supports = run.Supports;
        var enriched = supports.Select((s, idx) =>
        {
            var hit = hits
                .Where(h => h.Location.DistanceTo(s.Location) <= Math.Max(SupportToleranceFeet, h.HalfWidthFeet + ColumnFaceMatchToleranceFeet))
                .OrderBy(h => h.Location.DistanceTo(s.Location))
                .FirstOrDefault();
            if (hit is null)
            {
                return defaultInternalHalfWidthFeet > 0
                    ? s with { HalfWidthFeet = defaultInternalHalfWidthFeet }
                    : s;
            }

            // Hướng VÀO NHỊP từ gối này (đầu → gối kế; cuối → gối trước; giữa → cả 2 phía, lấy max).
            Point3? prev = idx > 0 ? supports[idx - 1].Location : null;
            Point3? next = idx < supports.Count - 1 ? supports[idx + 1].Location : null;

            double InnerFaceDistance(Point3? towardsN)
            {
                if (towardsN is not { } towards) return double.MinValue;
                var dirX = towards.X - s.Location.X;
                var dirY = towards.Y - s.Location.Y;
                var len = Math.Sqrt(dirX * dirX + dirY * dirY);
                if (len < 1e-9) return double.MinValue;
                dirX /= len; dirY /= len;

                // Nếu hit là DẦM GIAO (có EdgeMin/Max): chiếu 2 MÉP của vùng dầm giao (điểm trên trục chính)
                // lên hướng vào nhịp → lấy mép XA nhất theo hướng đó = mép trong THẬT. Đúng cả khi dầm giao
                // nằm lệch hẳn 1 phía (XY tâm trùng gối → proj tâm = 0 nhưng vùng vẫn trải dọc trục).
                if (!double.IsNaN(hit.EdgeMinFeet))
                {
                    // 2 mép = hit.Location ± half theo hướng trục dầm chính (≈ hướng gối→gối kề).
                    var d1 = (hit.Location.X + dirX * hit.HalfWidthFeet - s.Location.X) * dirX
                           + (hit.Location.Y + dirY * hit.HalfWidthFeet - s.Location.Y) * dirY;
                    var d2 = (hit.Location.X - dirX * hit.HalfWidthFeet - s.Location.X) * dirX
                           + (hit.Location.Y - dirY * hit.HalfWidthFeet - s.Location.Y) * dirY;
                    return Math.Max(d1, d2);
                }

                var proj = (hit.Location.X - s.Location.X) * dirX + (hit.Location.Y - s.Location.Y) * dirY;
                return proj + hit.HalfWidthFeet;
            }

            var innerToNext = InnerFaceDistance(next);
            var innerToPrev = InnerFaceDistance(prev);
            var halfWidth = Math.Max(0, Math.Max(innerToNext, innerToPrev));
            return s with { HalfWidthFeet = halfWidth };
        }).ToList();

        return run with { Supports = enriched };
    }
}
