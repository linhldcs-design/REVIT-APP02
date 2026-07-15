using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace FootingDrawing.Addin.Services;

/// <summary>
///     Tìm và phân loại rebar liên quan một móng đã chọn.
///     Phân loại bằng <c>Rebar.GetHostId() → Category</c> (đã verify trên model thật):
///     host = Structural Foundations → lưới thép móng; host = Structural Columns (cột trên móng) → thép chờ/đai.
/// </summary>
public sealed class RebarLocator
{
    private const double MatchTolFeet = 2.0; // ~600mm: cột coi là "trên móng" nếu tâm XY gần tâm móng.

    public sealed record FootingRebars(IReadOnlyList<Rebar> Mesh, IReadOnlyList<Rebar> Column);

    public FootingRebars Locate(Document doc, Element footing)
    {
        var mesh = new List<Rebar>();
        var column = new List<Rebar>();

        var footingCenter = FootingCenterXY(footing);
        var columnIdsOnFooting = ColumnIdsOnFooting(doc, footingCenter);

        var allRebars = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rebar)
            .WhereElementIsNotElementType()
            .Cast<Rebar>();

        foreach (var rebar in allRebars)
        {
            var hostId = rebar.GetHostId();
            if (hostId == footing.Id)
            {
                mesh.Add(rebar);
                continue;
            }

            if (columnIdsOnFooting.Contains(hostId))
                column.Add(rebar);
        }

        return new FootingRebars(mesh, column);
    }

    private static XYZ? FootingCenterXY(Element footing)
        => (footing.Location as LocationPoint)?.Point;

    /// <summary>Id các cột kết cấu có tâm XY nằm gần tâm móng (coi là cột dựng trên móng đó).</summary>
    private static HashSet<ElementId> ColumnIdsOnFooting(Document doc, XYZ? footingCenter)
    {
        var ids = new HashSet<ElementId>();
        if (footingCenter is null) return ids;

        var columns = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType();

        foreach (var col in columns)
        {
            if (col.Location is not LocationPoint lp) continue;
            var d = new XYZ(lp.Point.X - footingCenter.X, lp.Point.Y - footingCenter.Y, 0).GetLength();
            if (d <= MatchTolFeet) ids.Add(col.Id);
        }

        return ids;
    }
}
