using Autodesk.Revit.DB;
using RevitAPP.Core.Models;

namespace RevitAPP.Services.ColumnRebar;

/// <summary>
///     Đọc tiết diện (B,H) + cao độ base/top + góc xoay của một cột kết cấu chữ nhật.
///     Thử lần lượt: structural-section param → param "b"/"h" → bounding box. Trả về lỗi tiếng Việt nếu thất bại.
/// </summary>
public sealed class ColumnSectionReader
{
    public bool TryRead(Document document, FamilyInstance column, out ColumnSection section, out double baseMm,
        out double topMm, out double rotationRad, out string error)
    {
        section = null!;
        baseMm = topMm = rotationRad = 0;
        error = string.Empty;

        if (column.Location is not LocationPoint location)
        {
            error = "Cột không có điểm đặt (LocationPoint) — không hỗ trợ.";
            return false;
        }

        var slant = column.get_Parameter(BuiltInParameter.SLANTED_COLUMN_TYPE_PARAM);
        if (slant is { } s && s.AsInteger() != 0)
        {
            error = "Cột nghiêng chưa được hỗ trợ (v1 chỉ cột thẳng đứng).";
            return false;
        }

        if (!TryReadDimensions(document, column, out var widthMm, out var heightMm))
        {
            error = "Không đọc được kích thước tiết diện (B,H). Chỉ hỗ trợ cột chữ nhật có tham số b/h hoặc tiết diện kết cấu.";
            return false;
        }

        if (!TryReadElevations(document, column, out baseMm, out topMm) || topMm <= baseMm)
        {
            error = "Không đọc được cao độ base/top hợp lệ của cột.";
            return false;
        }

        section = new ColumnSection(widthMm, heightMm);
        rotationRad = location.Rotation;
        return true;
    }

    private static bool TryReadDimensions(Document document, FamilyInstance column, out double widthMm, out double heightMm)
    {
        widthMm = heightMm = 0;
        var symbol = column.Symbol;

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

        // Fallback: bounding box theo trục thế giới (đủ dùng cho cột không xoay / xoay 90°).
        var bbox = column.get_BoundingBox(null);
        if (bbox == null) return false;
        widthMm = ToMm(bbox.Max.X - bbox.Min.X);
        heightMm = ToMm(bbox.Max.Y - bbox.Min.Y);
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

    private static bool TryReadElevations(Document document, FamilyInstance column, out double baseMm, out double topMm)
    {
        baseMm = topMm = 0;
        var baseLevelId = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();
        var topLevelId = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId();
        if (baseLevelId == null || topLevelId == null) return false;

        if (document.GetElement(baseLevelId) is not Level baseLevel ||
            document.GetElement(topLevelId) is not Level topLevel)
            return false;

        var baseOffset = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;
        var topOffset = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;

        baseMm = ToMm(baseLevel.Elevation + baseOffset);
        topMm = ToMm(topLevel.Elevation + topOffset);
        return true;
    }

    private static double ToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
}
