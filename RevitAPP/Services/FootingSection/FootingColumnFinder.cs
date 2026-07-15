using Autodesk.Revit.DB;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Dò cột kết cấu đứng trên móng: chọn cột GẦN TÂM móng nhất (không phải mọi cột trong bbox — móng lớn
///     có thể trùm nhiều cột). Trả cao độ đỉnh section = Top Level của cột đó (vd TẦNG 1), KHÔNG phải đỉnh
///     cột vật lý (cột cao tới mái). Mặt cắt móng chỉ cần bao đế + cổ + đoạn cột tới tầng 1.
/// </summary>
public sealed class FootingColumnFinder
{
    /// <summary>Đoạn cột nhô lên trên Top Level (feet ~ 500mm) để mặt cắt thấy cột bị cắt (đặt break line).</summary>
    private const double ColumnStubAboveLevelFeet = 500.0 / 304.8;

    /// <summary>Cột gần tâm móng nhất (tâm nằm trong hình chiếu bằng của móng). Null nếu không có.</summary>
    public Element? FindNearestColumn(Document document, BoundingBoxXYZ footingBox)
    {
        var centerX = (footingBox.Min.X + footingBox.Max.X) * 0.5;
        var centerY = (footingBox.Min.Y + footingBox.Max.Y) * 0.5;

        Element? nearest = null;
        var bestDistSq = double.MaxValue;
        foreach (var column in new FilteredElementCollector(document)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType())
        {
            var xy = ColumnXy(column);
            if (xy == null) continue;
            if (xy.X < footingBox.Min.X || xy.X > footingBox.Max.X ||
                xy.Y < footingBox.Min.Y || xy.Y > footingBox.Max.Y) continue;

            var dx = xy.X - centerX;
            var dy = xy.Y - centerY;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                nearest = column;
            }
        }

        return nearest;
    }

    /// <summary>Tọa độ XY tim cột (feet). Null nếu column null.</summary>
    public XYZ? ColumnCenterXy(Element? column) => column == null ? null : ColumnXy(column);

    /// <summary>
    ///     Cao độ đỉnh section (feet) từ cột gần tâm nhất: Top Level elevation + đoạn nhô.
    ///     Null nếu không có cột (mặt cắt chỉ bao móng + cổ).
    /// </summary>
    public double? SectionTopZFeet(Document document, Element? column)
    {
        if (column == null) return null;

        var topLevelZ = TopLevelElevation(document, column);
        if (topLevelZ != null) return topLevelZ.Value + ColumnStubAboveLevelFeet;

        // Fallback: đỉnh cột vật lý (hiếm — cột không gán Top Level).
        var box = column.get_BoundingBox(null);
        return box?.Max.Z;
    }

    private static double? TopLevelElevation(Document document, Element column)
    {
        var topLevelParam = column.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_PARAM);
        if (topLevelParam == null) return null;
        var level = document.GetElement(topLevelParam.AsElementId()) as Level;
        return level?.Elevation;
    }

    private static XYZ? ColumnXy(Element column)
    {
        if (column.Location is LocationPoint point) return point.Point;
        var box = column.get_BoundingBox(null);
        if (box == null) return null;
        return new XYZ((box.Min.X + box.Max.X) * 0.5, (box.Min.Y + box.Max.Y) * 0.5, 0);
    }
}
