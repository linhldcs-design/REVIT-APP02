using Autodesk.Revit.DB;
using BeamRebar.Core.Models;
using BeamRebar.Core.Services;

namespace BeamRebar.Addin.Services;

/// <summary>
///     Đọc hình học dầm thẳng → <see cref="BeamSegment"/> thuần (Point3 feet) cho span model.
///     Thử lần lượt: structural-section param → param "b"/"h" → bounding box. v1 chỉ hỗ trợ dầm
///     có LocationCurve là Line (dầm cong defer v2). Trả lỗi tiếng Việt nếu thất bại.
/// </summary>
public sealed class BeamGeometryReader
{
    public bool TryRead(FamilyInstance beam, out BeamSegment segment, out string error)
    {
        segment = null!;
        error = string.Empty;

        if (beam.Location is not LocationCurve { Curve: Line line })
        {
            error = $"Dầm '{beam.Id}' không có trục thẳng (LocationCurve dạng Line) — v1 chỉ hỗ trợ dầm thẳng.";
            return false;
        }

        if (!TryReadDimensions(beam, out var widthMm, out var heightMm))
        {
            error = $"Không đọc được tiết diện (b,h) của dầm '{beam.Id}'. Chỉ hỗ trợ dầm chữ nhật.";
            return false;
        }

        // Cao độ thật mặt trên/dưới dầm từ bounding box — KHÔNG suy từ Z trục (trục dầm Revit thường
        // ở tâm hoặc đáy tuỳ z-justification; suy sai → thép nằm ngoài host → Revit crash).
        var bbox = beam.get_BoundingBox(null);
        if (bbox == null)
        {
            error = $"Không đọc được bounding box của dầm '{beam.Id}'.";
            return false;
        }

        var topFeet = bbox.Max.Z;
        var bottomFeet = bbox.Min.Z;

        var start = line.GetEndPoint(0);
        var end = line.GetEndPoint(1);
        segment = new BeamSegment(
            new Point3(start.X, start.Y, start.Z),
            new Point3(end.X, end.Y, end.Z),
            new BeamSection(widthMm, heightMm),
            topFeet,
            bottomFeet);
        return true;
    }

    private static bool TryReadDimensions(FamilyInstance beam, out double widthMm, out double heightMm)
    {
        widthMm = heightMm = 0;
        var symbol = beam.Symbol;

        var widthFeet = ReadFirstNonZero(symbol,
            BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH, "b", "Width", "Chiều rộng");
        var heightFeet = ReadFirstNonZero(symbol,
            BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT, "h", "Height", "Chiều cao", "Depth");

        if (widthFeet > 0 && heightFeet > 0)
        {
            widthMm = ToMm(widthFeet);
            heightMm = ToMm(heightFeet);
            return true;
        }

        // Fallback: bounding box theo trục thế giới.
        var bbox = beam.get_BoundingBox(null);
        if (bbox == null) return false;
        heightMm = ToMm(bbox.Max.Z - bbox.Min.Z);
        var dx = bbox.Max.X - bbox.Min.X;
        var dy = bbox.Max.Y - bbox.Min.Y;
        widthMm = ToMm(Math.Min(dx, dy));
        return widthMm > 0 && heightMm > 0;
    }

    private static double ReadFirstNonZero(Element symbol, BuiltInParameter builtIn, params string[] names)
    {
        var p = symbol.get_Parameter(builtIn);
        if (p is { StorageType: StorageType.Double } && p.AsDouble() > 0) return p.AsDouble();

        foreach (var name in names)
        {
            var named = symbol.LookupParameter(name);
            if (named is { StorageType: StorageType.Double } && named.AsDouble() > 0) return named.AsDouble();
        }

        return 0;
    }

    private static double ToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
}
