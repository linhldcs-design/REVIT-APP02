using Autodesk.Revit.DB;

namespace RevitAPP.Services.ColumnRebar;

/// <summary>
///     Tự động dò chiều cao dầm kết cấu giao đỉnh cột (per tầng).
///     Tìm OST_StructuralFraming giao vùng đỉnh cột; chiều cao dầm = bề dày Z của dầm nằm ngang
///     (không phụ thuộc param tiết diện → chắc chắn). Lấy dầm sâu nhất.
/// </summary>
public sealed class BeamDepthDetector
{
    private static readonly double MarginFeet = ToFeet(300);   // nới XY footprint cột
    private static readonly double BandDownFeet = ToFeet(2000); // dò xuống dưới đỉnh tối đa = MaxDepth
    private static readonly double BandUpFeet = ToFeet(300);    // nới lên trên đỉnh
    private const double MinDepthMm = 100;
    private const double MaxDepthMm = 2000;

    public double DetectBeamDepthMm(Document document, ElementId columnId, double topElevationFeet)
    {
        if (document.GetElement(columnId) is not FamilyInstance column) return 0;

        // Vùng tìm: footprint cột (nới rộng) × dải Z quanh đỉnh cột
        var colBox = column.get_BoundingBox(null);
        double cx0, cy0, cx1, cy1;
        if (colBox != null)
        {
            cx0 = colBox.Min.X - MarginFeet; cy0 = colBox.Min.Y - MarginFeet;
            cx1 = colBox.Max.X + MarginFeet; cy1 = colBox.Max.Y + MarginFeet;
        }
        else if (column.Location is LocationPoint lp)
        {
            cx0 = lp.Point.X - MarginFeet; cy0 = lp.Point.Y - MarginFeet;
            cx1 = lp.Point.X + MarginFeet; cy1 = lp.Point.Y + MarginFeet;
        }
        else return 0;

        var outline = new Outline(
            new XYZ(cx0, cy0, topElevationFeet - BandDownFeet),
            new XYZ(cx1, cy1, topElevationFeet + BandUpFeet));

        var candidates = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(outline))
            .ToList();

        var maxDepthMm = 0d;
        foreach (var beam in candidates)
        {
            var bb = beam.get_BoundingBox(null);
            if (bb == null) continue;

            var zExtMm = ToMm(bb.Max.Z - bb.Min.Z);
            var xExtMm = ToMm(bb.Max.X - bb.Min.X);
            var yExtMm = ToMm(bb.Max.Y - bb.Min.Y);
            var xyExtMm = Math.Max(xExtMm, yExtMm);

            // Dầm nằm ngang: dài theo XY hơn cao theo Z, bề dày Z hợp lý 100–2000mm.
            if (zExtMm < MinDepthMm || zExtMm > MaxDepthMm) continue;
            if (zExtMm >= xyExtMm) continue; // loại cột/giằng đứng

            // Đỉnh dầm gần đỉnh cột (dầm gác lên đỉnh cột)
            if (Math.Abs(ToMm(bb.Max.Z - topElevationFeet)) > 400) continue;

            if (zExtMm > maxDepthMm) maxDepthMm = zExtMm;
        }

        return maxDepthMm;
    }

    private static double ToFeet(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
    private static double ToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
}
