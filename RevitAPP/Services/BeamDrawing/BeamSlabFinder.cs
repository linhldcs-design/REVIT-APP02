using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Dò sàn (OST_Floors) giao/nằm trên dầm để biết cao độ MẶT DƯỚI SÀN — phục vụ dim chuỗi chiều cao
///     (dày sàn / phần dầm dưới sàn) và break-line ở mép sàn. Trả null nếu dầm không có sàn.
/// </summary>
public sealed class BeamSlabFinder
{
    /// <summary>
    ///     Cao độ mặt DƯỚI sàn (Z feet) nếu có sàn phủ trên dầm tại station. Sàn hợp lệ khi bbox Z của nó
    ///     nằm quanh đỉnh dầm (mặt dưới sàn ≈ đỉnh dầm). Null = không có sàn.
    /// </summary>
    public double? FindSlabBottomZ(Document doc, FamilyInstance beam, BeamGeometry geometry)
    {
        var beamTop = geometry.TopZFeet;
        var beamBox = beam.get_BoundingBox(null);
        if (beamBox == null) return null;

        var filter = new BoundingBoxIntersectsFilter(new Outline(beamBox.Min, beamBox.Max));
        var floors = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType()
            .WherePasses(filter)
            .ToList();

        double? best = null;
        foreach (var floor in floors)
        {
            var fb = floor.get_BoundingBox(null);
            if (fb == null) continue;
            var slabBottom = fb.Min.Z;
            // Sàn phủ đỉnh dầm: mặt dưới sàn ≈ đỉnh dầm (chênh trong ±150mm). Lấy sàn có mặt dưới gần đỉnh nhất.
            if (Math.Abs(slabBottom - beamTop) <= 150.0 / 304.8 && fb.Max.Z > slabBottom)
            {
                if (best == null || Math.Abs(slabBottom - beamTop) < Math.Abs(best.Value - beamTop))
                    best = slabBottom;
            }
        }
        return best;
    }
}
