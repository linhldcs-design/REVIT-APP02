using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Tìm vị trí GỐI (cạnh cột) và NHỊP (giữa 2 cột) trên dầm để cắt mặt cắt ĐÚNG chỗ — kể cả dầm nhiều nhịp.
///     Dò cột (OST_StructuralColumns) giao trục dầm, chiếu lên trục lấy station t∈[0,1]. Fallback 3.5%/50% nếu
///     không dò được cột. Trả 1 gối đại diện (cột đầu) + 1 nhịp đại diện (giữa cột đầu & cột kế).
/// </summary>
public sealed class BeamSupportFinder
{
    private const double IntersectTolFeet = 400.0 / 304.8; // ~400mm: cột coi là giao dầm nếu tâm cách trục ≤ tol.
    // Tâm lát cắt phải cách mép cột ít nhất nửa Far Clip (75 mm) + khe an toàn 10 mm.
    private static readonly double SupportClearanceFeet = BeamSectionBoxMath.CrossSupportClearanceFeet();
    // Dầm KHÔNG có cột 2 đầu (dầm phụ gác dầm chính): gối phải cách đầu dầm ≥ 300mm để không cắt sát đầu.
    private const double NoColumnSupportInsetFeet = 300.0 / 304.8;

    /// <summary>Trả (stationGối, stationNhịp) ∈ [0,1] dọc trục dầm.</summary>
    public (double Support, double MidSpan) FindStations(Document doc, FamilyInstance beam, BeamGeometry geometry)
    {
        var start = ToXyz(geometry.Start);
        var end = ToXyz(geometry.End);
        var dir = (end - start).Normalize();
        var length = geometry.LengthFeet;
        if (length < 1e-6) return (0.035, 0.5);

        // Cột giao dầm → station (t) theo chiếu tâm cột lên trục dầm.
        var columnHits = new List<ColumnHit>();
        var bb = beam.get_BoundingBox(null);
        var filter = bb != null ? new BoundingBoxIntersectsFilter(new Outline(bb.Min, bb.Max)) : null;
        var collector = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType();
        if (filter != null) collector = collector.WherePasses(filter);

        foreach (var col in collector)
        {
            if ((col as FamilyInstance)?.Location is not LocationPoint lp) continue;
            var p = lp.Point;
            var v = p - start;
            var t = v.DotProduct(dir) / length;              // vị trí dọc trục [0,1]
            if (t < -0.05 || t > 1.05) continue;
            var onAxis = start + dir * (t * length);
            var perp = (p - onAxis); perp = new XYZ(perp.X, perp.Y, 0);
            if (perp.GetLength() > IntersectTolFeet) continue; // cột không nằm trên trục dầm
            var center = Math.Min(Math.Max(t, 0), 1);
            var colBox = col.get_BoundingBox(null);
            if (colBox == null)
            {
                columnHits.Add(new ColumnHit(center, center, center));
                continue;
            }

            var stations = BoxCorners(colBox)
                .Select(point => (point - start).DotProduct(dir) / length)
                .ToList();
            columnHits.Add(new ColumnHit(center, stations.Min(), stations.Max()));
        }

        // Nhiều cột có thể chồng nhau theo tầng/instance và cho cùng station. Gom trong 100 mm vật lý
        // trước khi chọn nhịp, nếu không hai phần tử trùng sẽ làm GỐI và NHỊP cắt cùng một chỗ.
        var duplicateTolerance = (100.0 / 304.8) / length;
        var merged = MergeOverlappingColumns(columnHits, duplicateTolerance);
        var resolved = BeamSectionStationMath.Resolve(merged.Select(hit => hit.Center), duplicateTolerance);
        if (merged.Count == 0)
        {
            // Không có cột giao dầm → gối cách đầu dầm ≥ 300mm (station tương ứng), nhịp giữa dầm.
            var minInset = Math.Min(NoColumnSupportInsetFeet / length, 0.45);
            return (Math.Max(resolved.Support, minInset), resolved.MidSpan);
        }

        // Với ≥2 cột, nhịp đại diện đi từ cột đầu sang cột kế (station tăng).
        // Với chỉ 1 cột, đi về phía phần dầm dài hơn giống quy tắc Resolve().
        var supportColumn = merged[0];
        var towardIncreasing = merged.Count >= 2 || supportColumn.Center <= 0.5;
        var clearanceStation = SupportClearanceFeet / length;
        var support = BeamSectionStationMath.EnsureOutsideSupport(resolved.Support,
            supportColumn.Min, supportColumn.Max, towardIncreasing, clearanceStation);
        return (support, resolved.MidSpan);
    }

    private static List<ColumnHit> MergeOverlappingColumns(IEnumerable<ColumnHit> hits, double tolerance)
    {
        var sorted = hits.OrderBy(hit => hit.Center).ToList();
        var merged = new List<ColumnHit>();
        foreach (var hit in sorted)
        {
            if (merged.Count == 0 ||
                (hit.Center - merged[^1].Center > tolerance && hit.Min > merged[^1].Max + tolerance))
            {
                merged.Add(hit);
                continue;
            }

            var previous = merged[^1];
            merged[^1] = new ColumnHit(previous.Center, Math.Min(previous.Min, hit.Min), Math.Max(previous.Max, hit.Max));
        }
        return merged;
    }

    private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ box)
    {
        var transform = box.Transform;
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            yield return transform.OfPoint(new XYZ(x, y, z));
    }

    private static XYZ ToXyz(Point3 p) => new(p.X, p.Y, p.Z);
    private sealed record ColumnHit(double Center, double Min, double Max);
}
