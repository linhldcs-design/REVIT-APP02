using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace BeamDrawing.Addin.Services.Annotation;

/// <summary>
///     Đặt "Aligned Multi-Rebar Annotation" (MultiReferenceAnnotation) cho thép trong view.
///     Học từ bản thương mại qua revit-mcp: MỖI rebar = 1 MRA riêng (refs=1), tag head xếp THẲNG
///     HÀNG cột dọc (X cố định cách mép phải ~220mm, Y giảm dần). Gom nhiều rebar/1 MRA fail khi
///     rebar không vuông góc dimension line — nên tách từng cái.
///     PHẢI gọi trong Transaction đang mở, SAU khi view + rebar visible đã regenerate.
/// </summary>
public sealed class MultiRebarAnnotationPlacer
{
    /// <summary>Khoảng lệch phải cố định của tag head so với mép crop (ft, ~220mm) — đo từ bản TM.</summary>
    private const double TagOffsetXFeet = 0.72;

    /// <summary>
    ///     Tạo MRA cho từng rebar trong view, tag xếp thẳng cột. Trả số MRA tạo thành công.
    /// </summary>
    public int Place(Document doc, View view, IReadOnlyList<Rebar> rebars, List<string> warnings)
    {
        if (rebars.Count == 0) return 0;

        var annoType = GetAnnotationType(doc);
        if (annoType == null)
        {
            warnings.Add("Project không có Multi-Rebar Annotation Type — bỏ qua aligned annotation.");
            return 0;
        }

        // Cho rebar hiện rõ trong view trước + regenerate để reference hợp lệ.
        foreach (var rebar in rebars)
        {
            try { rebar.SetUnobscuredInView(view, true); }
            catch { /* bỏ qua */ }
        }
        doc.Regenerate();

        var crop = view.CropBox;
        var t = crop.Transform;
        var up = t.BasisY.Normalize();
        var normal = t.BasisZ.Normalize();

        var height = crop.Max.Y - crop.Min.Y;
        var dimOrigin = t.OfPoint(new XYZ(crop.Max.X + 0.15, (crop.Min.Y + crop.Max.Y) * 0.5, 0));

        // Tag xếp dọc: X cố định bên phải, Y trải đều từ trên (0.90) xuống (0.10) theo số rebar.
        var yTopRatio = 0.90;
        var yBotRatio = 0.10;
        var step = rebars.Count > 1 ? (yTopRatio - yBotRatio) / (rebars.Count - 1) : 0;

        var placed = 0;
        for (var i = 0; i < rebars.Count; i++)
        {
            var rebar = rebars[i];
            var yRatio = yTopRatio - step * i;
            var tagHead = t.OfPoint(new XYZ(crop.Max.X + TagOffsetXFeet, crop.Min.Y + height * yRatio, 0));

            var options = new MultiReferenceAnnotationOptions(annoType)
            {
                DimensionLineOrigin = dimOrigin,
                DimensionLineDirection = up,
                DimensionPlaneNormal = normal,
                TagHeadPosition = tagHead,
                TagHasLeader = true
            };
            options.SetElementsToDimension([rebar.Id]);

            if (!MultiReferenceAnnotation.AreElementsValidForMultiReferenceAnnotation(doc, options))
                continue; // rebar không hợp lệ (vd không vuông góc) — bỏ qua, IndependentTag sẽ lo phần còn lại

            try
            {
                var mra = MultiReferenceAnnotation.Create(doc, view.Id, options);
                // Leader thẳng bám thép — giống bản TM (LeaderEndCondition=Attached).
                if (doc.GetElement(mra.TagId) is IndependentTag tag)
                {
                    try { tag.LeaderEndCondition = LeaderEndCondition.Attached; } catch { }
                }
                placed++;
            }
            catch (Exception ex)
            {
                warnings.Add($"MRA thất bại cho thép '{rebar.Id}' trong '{view.Name}': {ex.Message}");
            }
        }

        return placed;
    }

    private static MultiReferenceAnnotationType? GetAnnotationType(Document doc)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(MultiReferenceAnnotationType))
            .Cast<MultiReferenceAnnotationType>()
            .ToList();

        // Ưu tiên type chuyên cho thép dầm/mặt cắt (giống bản thương mại: "MRA", "SL&DK", "MCN", "Dầm").
        var preferred = types.FirstOrDefault(t =>
            t.Name.Contains("MRA", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("SL", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("MCN", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Dầm", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Dam", StringComparison.OrdinalIgnoreCase));

        return preferred ?? types.FirstOrDefault();
    }
}
