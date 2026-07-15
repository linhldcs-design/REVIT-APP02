using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using FootingDrawing.Core.Models;

namespace FootingDrawing.Addin.Services.Annotation;

/// <summary>
///     Đặt Structural Rebar Bending Detail cho các thanh chính trên view.
///     Dùng <see cref="RebarBendingDetail.Create"/> (annotation gốc Revit). PHẢI gọi trong Transaction đang mở.
/// </summary>
public sealed class BendingDetailPlacer
{
#if REVIT2025_OR_GREATER
    public int Place(Document doc, View view, IReadOnlyList<Rebar> rebars, FootingGeometry geometry,
        ElementId? bendingDetailTypeId, List<string> warnings)
    {
        if (rebars.Count == 0) return 0;
        if (bendingDetailTypeId is null || bendingDetailTypeId == ElementId.InvalidElementId)
        {
            warnings.Add("Chưa chọn Bending Detail type — bỏ qua bending detail.");
            return 0;
        }

        var detailType = doc.GetElement(bendingDetailTypeId) as RebarBendingDetailType;
        if (detailType is null)
        {
            warnings.Add("Bending Detail type không hợp lệ — bỏ qua.");
            return 0;
        }

        var placed = 0;
        foreach (var rebar in rebars)
        {
            try
            {
                var position = PositionInsideFooting(rebar, view, geometry);
                // R25: viewId đứng trước reinforcementElementId.
                var detail = RebarBendingDetail.Create(doc, view.Id, rebar.Id, 0, detailType, position, 0.0);
                if (detail != null) placed++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Không tạo bending detail cho thép '{rebar.Id}': {ex.Message}");
            }
        }

        return placed;
    }

    private static XYZ PositionInsideFooting(Rebar rebar, View view, FootingGeometry geometry)
    {
        var center = new XYZ(geometry.BaseCenter.X, geometry.BaseCenter.Y, geometry.BaseCenter.Z);
        var normal = view.ViewDirection.Normalize();
        center -= normal * normal.DotProduct(center - view.Origin);

        var dirX = new XYZ(geometry.DirX.X, geometry.DirX.Y, geometry.DirX.Z).Normalize();
        var dirY = new XYZ(geometry.DirY.X, geometry.DirY.Y, geometry.DirY.Z).Normalize();
        var rebarDirection = PrimaryDirection(rebar);
        var runsAlongX = Math.Abs(rebarDirection.DotProduct(dirX)) >= Math.Abs(rebarDirection.DotProduct(dirY));

        return runsAlongX
            ? center - dirY * (geometry.WidthYFeet * 0.38)
            : center + dirX * (geometry.WidthXFeet * 0.25);
    }

    private static XYZ PrimaryDirection(Rebar rebar)
    {
        try
        {
            var curve = rebar.GetCenterlineCurves(false, false, false,
                    MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .OrderByDescending(item => item.Length)
                .FirstOrDefault();
            if (curve != null) return (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
        }
        catch { /* fallback */ }

        return XYZ.BasisX;
    }
#else
    public int Place(Document doc, View view, IReadOnlyList<Rebar> rebars, FootingGeometry geometry,
        ElementId? bendingDetailTypeId, List<string> warnings) => 0;
#endif
}
