using Autodesk.Revit.DB;
using BeamDrawing.Core.Models;

namespace BeamDrawing.Addin.Services;

/// <summary>
///     Đọc hình học dầm thẳng: location line, hướng, tiết diện (b,h), cao độ mặt trên/dưới.
///     Thử lần lượt: structural-section param → param "b"/"h" → bounding box. Trả lỗi tiếng Việt nếu thất bại.
///     v1 chỉ hỗ trợ dầm có LocationCurve là Line (dầm cong defer v2).
/// </summary>
public sealed class BeamGeometryReader
{
    public bool TryRead(Document document, FamilyInstance beam, out BeamGeometry geometry, out string error)
    {
        geometry = null!;
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

        if (!TryReadElevations(beam, line, heightMm, out var topFeet, out var bottomFeet))
        {
            error = $"Không xác định được cao độ mặt trên/dưới của dầm '{beam.Id}'.";
            return false;
        }

        var direction = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
        geometry = new BeamGeometry(beam, line, direction, new BeamSection(widthMm, heightMm), topFeet, bottomFeet);
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
        // Bề rộng = cạnh ngang nhỏ hơn trong mặt phẳng XY.
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

    /// <summary>
    ///     Cao độ mặt trên/dưới dầm. Z của location line là cao độ trục dầm (thường = đỉnh dầm trong
    ///     Revit do z-justification mặc định Top). Lấy mặt trên = Z trục, mặt dưới = Z trục − h.
    ///     Có thể tinh chỉnh theo z-justification ở phase sau nếu cần chính xác hơn.
    /// </summary>
    private static bool TryReadElevations(FamilyInstance beam, Line line, double heightMm,
        out double topFeet, out double bottomFeet)
    {
        var zStart = line.GetEndPoint(0).Z;
        var zEnd = line.GetEndPoint(1).Z;
        var axisZFeet = Math.Min(zStart, zEnd);
        var heightFeet = ToFeet(heightMm);

        topFeet = axisZFeet;
        bottomFeet = axisZFeet - heightFeet;
        return heightFeet > 0;
    }

    private static double ToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
    private static double ToFeet(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
}
