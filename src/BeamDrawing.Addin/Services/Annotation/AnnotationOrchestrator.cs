using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamDrawing.Addin.Models;
using BeamDrawing.Core.Models;

namespace BeamDrawing.Addin.Services.Annotation;

/// <summary>
///     Điều phối đặt ghi chú (tag/dim/spot/break) lên các view đã tạo, theo cấu hình.
///     PHẢI gọi trong Transaction đang mở, SAU khi view đã commit + Regenerate.
///     v1: hiện thực rebar tag (5a). Dimension/spot/break (5b) đặt khung gọi + warn để verify dần
///     trên model thật — tránh đặt sai reference khi chưa có dữ liệu hình học thực tế.
/// </summary>
public sealed class AnnotationOrchestrator
{
    private readonly RebarTagPlacer _rebarTagPlacer = new();
    private readonly MultiRebarAnnotationPlacer _multiRebarPlacer = new();
    private readonly BeamRebarLocator _rebarLocator = new();

    public void Annotate(Document doc, IReadOnlyList<FamilyInstance> beams, BeamDrawingSetting setting,
        BeamDrawingResult result)
    {
        // Nhóm tag mặt cắt dọc bị tắt (checkbox header) → bỏ qua tag sectional.
        if (!setting.TagMapping.SectionalEnabled)
        {
            result.Warnings.Add("Tag mặt cắt dọc đã tắt — bỏ qua đặt tag thép.");
            return;
        }

        var tagTypeId = ResolveTagType(doc, setting.TagMapping.T1TagTypeName);

        // 5a — Rebar tag. Tag trên CẢ mặt cắt dọc lẫn mặt cắt ngang (bản vẽ chuẩn tag chủ yếu ở
        // mặt cắt ngang: 13/15/17...). Bỏ qua tag ngang nếu nhóm cross bị tắt.
        var viewIds = new List<ElementId>(result.SectionViewIds);
        if (setting.TagMapping.CrossEnabled) viewIds.AddRange(result.CrossSectionViewIds);

        var taggedTotal = 0;
        var multiCount = 0;
        foreach (var beam in beams)
        {
            var rebars = _rebarLocator.GetRebars(doc, beam);
            if (rebars.Count == 0) continue;

            foreach (var viewId in viewIds)
            {
                if (doc.GetElement(viewId) is not View view) continue;

                // Ưu tiên Aligned Multi-Rebar Annotation (mỗi rebar 1 MRA, xếp thẳng hàng — giống bản
                // thương mại). Rebar nào MRA không nhận → fallback IndependentTag cho phần còn lại.
                var mraPlaced = _multiRebarPlacer.Place(doc, view, rebars, result.Warnings);
                multiCount += mraPlaced;
                if (mraPlaced == 0)
                    taggedTotal += _rebarTagPlacer.TagRebars(doc, view, rebars, tagTypeId, addLeader: true, result.Warnings);
            }
        }

        if (multiCount > 0)
            result.Warnings.Add($"Đã tạo {multiCount} Multi-Rebar Annotation (aligned).");
        if (taggedTotal > 0)
            result.Warnings.Add($"Đã tag {taggedTotal} thanh thép (independent tag).");
        if (multiCount == 0 && taggedTotal == 0)
            result.Warnings.Add("Không có thanh thép nào được tag (dầm chưa có Rebar hoặc thiếu tag/annotation type).");

        // 5b — Dimension/spot/break: verify dần trên model thật ở smoke test.
        if (setting.Dimension.Enabled)
            result.Warnings.Add("Dimension tự động sẽ bổ sung sau khi verify reference trên model thật.");
        if (setting.SpotElevation.Enabled)
            result.Warnings.Add("Spot elevation tự động sẽ bổ sung sau khi verify trên model thật.");
        if (setting.BreakLine.Enabled)
            result.Warnings.Add("Break line tự động sẽ bổ sung sau khi verify trên model thật.");
    }

    private static ElementId? ResolveTagType(Document doc, string? tagTypeName)
    {
        var tags = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_RebarTags)
            .WhereElementIsElementType()
            .ToList();

        if (tags.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(tagTypeName))
        {
            var match = tags.FirstOrDefault(t => t.Name == tagTypeName);
            if (match != null) return match.Id;
        }

        return tags[0].Id;
    }
}
