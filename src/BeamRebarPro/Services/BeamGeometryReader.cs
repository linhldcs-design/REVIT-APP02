using Autodesk.Revit.DB;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services;

/// <summary>
///     Đọc hình học một dầm kết cấu thành <see cref="BeamSegment"/>: trục từ LocationCurve, tiết diện
///     (b, h) từ tham số/bounding box, cao độ mặt trên/dưới THẬT từ bounding box theo Z (không suy từ
///     Z trục) để thép nằm đúng trong host. Trả false + lý do nếu dầm không hợp lệ (cong, thẳng đứng...).
/// </summary>
public sealed class BeamGeometryReader
{
    public bool TryRead(FamilyInstance beam, out BeamSegment segment, out string error)
    {
        segment = null!;
        error = string.Empty;

        if (beam.Location is not LocationCurve { Curve: Line line })
        {
            error = $"Dầm id {beam.Id.ToValue()}: chỉ hỗ trợ dầm thẳng (LocationCurve dạng Line).";
            return false;
        }

        var start = line.GetEndPoint(0);
        var end = line.GetEndPoint(1);
        if (start.DistanceTo(end) < 1e-6)
        {
            error = $"Dầm id {beam.Id.ToValue()}: chiều dài bằng 0.";
            return false;
        }

        var bbox = beam.get_BoundingBox(null);
        if (bbox is null)
        {
            error = $"Dầm id {beam.Id.ToValue()}: không lấy được bounding box.";
            return false;
        }

        if (!TryReadSection(beam, out var section, out error))
            return false;

        // Lệch ngang: chiếu (tâm bbox − điểm đầu location) lên phương ngang tiết diện (trục dầm × Z).
        // Bù justification dầm (location line có thể ở mép, không phải tâm) để thép căn đúng giữa bê tông.
        var along = (end - start).Normalize();
        var across = along.CrossProduct(XYZ.BasisZ);
        var lateralOffset = 0.0;
        if (across.GetLength() > 1e-6)
        {
            across = across.Normalize();
            var bboxCenter = (bbox.Min + bbox.Max) / 2;
            lateralOffset = (bboxCenter - start).DotProduct(across);
        }

        // Mặt trên/dưới bê tông THẬT: quét đỉnh solid của dầm theo Z. Đây là nguồn chính xác duy nhất —
        // không phụ thuộc justification (Top/Center/Bottom) hay phần thừa của bbox. Fallback bbox nếu
        // không lấy được solid.
        if (TryReadSolidZ(beam, out var solidTop, out var solidBottom))
        {
            // Khi dầm JOIN với sàn, Revit cắt solid dầm ở vùng chồng sàn → đỉnh solid (solidTop) bị hạ
            // xuống đáy sàn, làm chiều cao dầm thiếu phần sàn. Đáy dầm (solidBottom) KHÔNG bị cắt (sàn nằm
            // trên), nên mặt trên dầm THẬT = đáy + chiều cao family (h). Lấy max với solidTop để dầm không
            // join (solidTop đúng) vẫn ra đúng kết quả.
            var heightFeet = section.HeightMm / 304.8;
            var trueTop = Math.Max(solidTop, solidBottom + heightFeet);
            segment = new BeamSegment(
                new Point3(start.X, start.Y, start.Z),
                new Point3(end.X, end.Y, end.Z),
                section,
                TopElevationFeet: trueTop,
                BottomElevationFeet: solidBottom,
                LateralOffsetFeet: lateralOffset);
            return true;
        }

        segment = new BeamSegment(
            new Point3(start.X, start.Y, start.Z),
            new Point3(end.X, end.Y, end.Z),
            section,
            TopElevationFeet: bbox.Max.Z,
            BottomElevationFeet: bbox.Min.Z,
            LateralOffsetFeet: lateralOffset);
        return true;
    }

    /// <summary>Quét solid bê tông của dầm, trả cao độ Z mặt trên/dưới thật (feet).</summary>
    private static bool TryReadSolidZ(FamilyInstance beam, out double topZ, out double bottomZ)
    {
        topZ = double.MinValue;
        bottomZ = double.MaxValue;

        var options = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
        var geom = beam.get_Geometry(options);
        if (geom is null) return false;

        var found = false;
        foreach (var solid in EnumerateSolids(geom))
        {
            foreach (Edge edge in solid.Edges)
            {
                foreach (var p in edge.Tessellate())
                {
                    if (p.Z > topZ) topZ = p.Z;
                    if (p.Z < bottomZ) bottomZ = p.Z;
                    found = true;
                }
            }
        }

        return found && topZ > bottomZ;
    }

    private static IEnumerable<Solid> EnumerateSolids(GeometryElement geom)
    {
        foreach (var obj in geom)
        {
            switch (obj)
            {
                case Solid { Volume: > 1e-9 } solid:
                    yield return solid;
                    break;
                case GeometryInstance gi:
                    foreach (var inner in EnumerateSolids(gi.GetInstanceGeometry()))
                        yield return inner;
                    break;
            }
        }
    }

    private static bool TryReadSection(FamilyInstance beam, out BeamSection section, out string error)
    {
        section = null!;
        error = string.Empty;

        var symbol = beam.Symbol;
        var widthFeet = ReadDimension(symbol, ["b", "Width", "Chiều rộng"]);
        var heightFeet = ReadDimension(symbol, ["h", "Height", "Chiều cao"]);

        if (widthFeet <= 0 || heightFeet <= 0)
        {
            error = $"Dầm id {beam.Id.ToValue()}: không đọc được tiết diện (b/h). Hãy đặt tham số b, h trên family dầm.";
            return false;
        }

        section = new BeamSection(widthFeet * 304.8, heightFeet * 304.8);
        return true;
    }

    private static double ReadDimension(FamilySymbol symbol, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var param = symbol.LookupParameter(name);
            if (param is { StorageType: StorageType.Double } && param.AsDouble() > 0)
                return param.AsDouble();
        }

        return 0;
    }
}
