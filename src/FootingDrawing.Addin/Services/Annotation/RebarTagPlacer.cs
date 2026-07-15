using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using FootingDrawing.Core.Models;

namespace FootingDrawing.Addin.Services.Annotation;

/// <summary>
///     Đặt IndependentTag cho rebar trong plan view. Cho thép hiện rõ (SetUnobscuredInView) + regenerate
///     trước khi tag để reference hợp lệ. PHẢI gọi trong Transaction đang mở, SAU khi view đã commit.
/// </summary>
public sealed class RebarTagPlacer
{
    public int Tag(Document doc, View view, IReadOnlyList<Rebar> rebars, FootingGeometry geometry,
        ElementId? tagTypeId, List<string> warnings)
    {
        if (rebars.Count == 0) return 0;
        if (tagTypeId is null || tagTypeId == ElementId.InvalidElementId)
        {
            warnings.Add($"Chưa chọn tag type — bỏ qua tag {rebars.Count} thanh trong '{view.Name}'.");
            return 0;
        }

        var placed = 0;
        foreach (var rebar in rebars)
        {
            var reference = GetTaggableReference(rebar);
            if (reference is null)
            {
                warnings.Add($"Không lấy được reference cho thép '{rebar.Id}' trong '{view.Name}'.");
                continue;
            }

            try
            {
                var (tagHead, orientation) = GetTagLayout(rebar, view, geometry);
                IndependentTag.Create(doc, tagTypeId, view.Id, reference, false, orientation, tagHead);
                placed++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Không tag được thép '{rebar.Id}' trong '{view.Name}': {ex.Message}");
            }
        }

        return placed;
    }

    /// <summary>Reference tag được: subelement (thanh con) trước, fallback new Reference(rebar).</summary>
    private static Reference? GetTaggableReference(Rebar rebar)
    {
        try
        {
            var subelements = rebar.GetSubelements();
            if (subelements is { Count: > 0 })
            {
                var reference = subelements[0].GetReference();
                if (reference != null) return reference;
            }
        }
        catch { /* fallback */ }

        try { return new Reference(rebar); }
        catch { return null; }
    }

    private static (XYZ Point, TagOrientation Orientation) GetTagLayout(
        Rebar rebar, View view, FootingGeometry geometry)
    {
        var center = new XYZ(geometry.BaseCenter.X, geometry.BaseCenter.Y, geometry.BaseCenter.Z);
        var normal = view.ViewDirection.Normalize();
        center -= normal * normal.DotProduct(center - view.Origin);

        var dirX = new XYZ(geometry.DirX.X, geometry.DirX.Y, geometry.DirX.Z).Normalize();
        var dirY = new XYZ(geometry.DirY.X, geometry.DirY.Y, geometry.DirY.Z).Normalize();
        var rebarDirection = PrimaryDirection(rebar);
        var runsAlongX = Math.Abs(rebarDirection.DotProduct(dirX)) >= Math.Abs(rebarDirection.DotProduct(dirY));

        return runsAlongX
            ? (center - dirY * (geometry.WidthYFeet * 0.38) + dirY * 0.25, TagOrientation.Horizontal)
            : (center + dirX * (geometry.WidthXFeet * 0.25) - dirX * 0.25, TagOrientation.Vertical);
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
}
