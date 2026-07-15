using Autodesk.Revit.DB;
using FootingDrawing.Addin.ViewModels;
using FootingDrawing.Core.Models;

namespace FootingDrawing.Addin.Services.Annotation;

/// <summary>
///     Điều phối đặt annotation (tag lưới + tag thép chờ + dimension + bending detail) lên plan view đã tạo,
///     theo enable-flags của user. PHẢI gọi trong Transaction đang mở, SAU khi view committed + regenerated.
/// </summary>
public sealed class FootingAnnotationOrchestrator
{
    private readonly RebarLocator _locator = new();
    private readonly RebarTagPlacer _tagPlacer = new();
    private readonly BendingDetailPlacer _bendingPlacer = new();
    private readonly FootingDimensionPlacer _dimPlacer = new();

    public void Annotate(Document doc, View view, Element footing, FootingGeometry geometry,
        FootingDrawingSetting setting, ProjectResourceProvider resources, FootingDrawingResult result)
    {
        var rebars = _locator.Locate(doc, footing);
        var warnings = result.Warnings;

        // Tag lưới thép móng.
        if (setting.FootingTagEnabled)
        {
            var tagId = resources.ResolveType(doc, BuiltInCategory.OST_RebarTags,
                setting.FootingRebarTagTypeName, warnings);
            result.TagCount += _tagPlacer.Tag(doc, view, rebars.Mesh, geometry, tagId, warnings);
        }

        // Bending detail cho thanh chính (ưu tiên lưới móng).
        if (setting.BendingDetailEnabled)
        {
#if REVIT2025_OR_GREATER
            var bdId = resources.ResolveType(doc, BuiltInCategory.OST_RebarBendingDetails,
                setting.BendingDetailTypeName, warnings);
            result.BendingDetailCount += _bendingPlacer.Place(doc, view, rebars.Mesh, geometry, bdId, warnings);
#else
            warnings.Add("Bending Detail chỉ được Revit API hỗ trợ từ Revit 2025 — đã bỏ qua trên phiên bản này.");
#endif
        }

        // Dimension: dựng từ vertical planar face của móng (đế + cổ), 2 phương.
        var dimId = resources.ResolveDimensionType(doc, setting.DimensionTypeName, warnings);
        result.DimensionCount += _dimPlacer.Place(doc, view, geometry, setting, footing, dimId, warnings);

        SetRebarObscured(view, rebars, warnings);

        if (rebars.Mesh.Count == 0 && rebars.Column.Count == 0)
            warnings.Add("Móng chưa có thép nào (lưới/thép chờ) — không có gì để tag.");
    }

    private static void SetRebarObscured(View view, RebarLocator.FootingRebars rebars, List<string> warnings)
    {
        foreach (var rebar in rebars.Mesh.Concat(rebars.Column))
        {
            try
            {
                rebar.SetUnobscuredInView(view, false);
            }
            catch (Exception ex)
            {
                warnings.Add($"Không đặt được View Visibility State cho thép '{rebar.Id}': {ex.Message}");
            }
        }
    }
}
