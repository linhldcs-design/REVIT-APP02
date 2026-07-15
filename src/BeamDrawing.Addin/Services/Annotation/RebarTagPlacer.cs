using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace BeamDrawing.Addin.Services.Annotation;

/// <summary>
///     Đặt tag cho thép trong một section view 2D.
///     QUAN TRỌNG (verify-first): với section view 2D phải dùng <c>Rebar.SetUnobscuredInView</c>
///     để thép hiện rõ trước khi tag — KHÔNG dùng SetSolidInView (chỉ áp dụng cho View3D).
///     PHẢI gọi trong Transaction đang mở, SAU khi view đã được tạo + commit + Regenerate.
/// </summary>
public sealed class RebarTagPlacer
{
    private Transform? _cropTransform;

    /// <summary>
    ///     Tag tất cả rebar đã cho trong view bằng tag type chỉ định.
    ///     Trả số tag đặt thành công; ghi cảnh báo nếu một rebar không tag được.
    /// </summary>
    public int TagRebars(Document doc, View view, IReadOnlyList<Rebar> rebars, ElementId? tagTypeId,
        bool addLeader, List<string> warnings)
    {
        if (tagTypeId == null || tagTypeId == ElementId.InvalidElementId)
        {
            warnings.Add($"Chưa chọn tag type cho view '{view.Name}', bỏ qua tag thép.");
            return 0;
        }

        // Bước 1: cho TẤT CẢ thép hiện rõ (solid + unobscured) trong section view 2D.
        // Phải làm trước + regenerate để reference trở nên hợp lệ cho IndependentTag.
        foreach (var rebar in rebars)
        {
            try { rebar.SetUnobscuredInView(view, true); }
            catch { /* một số view không hỗ trợ — bỏ qua */ }
        }
        doc.Regenerate();

        // Bước 2: mỗi rebar set = 1 tag (tránh trùng). Tag head xếp THẲNG HÀNG bên phải tiết diện,
        // cách đều theo chiều cao — giống bản vẽ chuẩn (13/15/17/12 xếp dọc).
        var placed = 0;

        // Vùng đặt tag dựa trên CropBox (toạ độ view). Cột bên phải, dải Y từ trên xuống.
        var crop = view.CropBox;
        double tagX, yTop, yStep, z;
        var cropOk = view.CropBoxActive && crop != null;
        if (cropOk)
        {
            var t = crop.Transform;
            // Điểm phải-trên trong hệ view, đưa về world qua transform.
            tagX = crop.Max.X + (crop.Max.X - crop.Min.X) * 0.30;
            yTop = crop.Max.Y - (crop.Max.Y - crop.Min.Y) * 0.12;
            var span = (crop.Max.Y - crop.Min.Y) * 0.72;
            yStep = rebars.Count > 1 ? span / (rebars.Count - 1) : 0;
            z = (crop.Min.Z + crop.Max.Z) * 0.5;
            // Lưu transform để map sang world.
            _cropTransform = t;
        }
        else
        {
            tagX = 0; yTop = 0; yStep = 1; z = 0; _cropTransform = null;
        }
        var i = 0;
        foreach (var rebar in rebars)
        {
            var reference = GetTaggableReference(rebar);
            if (reference == null)
            {
                warnings.Add($"Không lấy được reference hợp lệ cho thép '{rebar.Id}' trong view '{view.Name}'.");
                continue;
            }

            try
            {
                XYZ tagHead;
                if (cropOk && _cropTransform != null)
                {
                    // Toạ độ trong hệ view → world qua transform của CropBox.
                    var local = new XYZ(tagX, yTop - yStep * i, z);
                    tagHead = _cropTransform.OfPoint(local);
                }
                else
                {
                    tagHead = GetTagPoint(rebar, view);
                }

                var tag = IndependentTag.Create(doc, tagTypeId, view.Id, reference, addLeader,
                    TagOrientation.Horizontal, tagHead);
                // Leader THẲNG bám thép (giống bản thương mại đo qua MCP: LeaderEndCondition=Attached)
                // → tránh leader chéo/khuỷu.
                try { tag.LeaderEndCondition = LeaderEndCondition.Attached; }
                catch { /* một số tag không cho đổi — bỏ qua */ }
                placed++;
                i++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Không tag được thép '{rebar.Id}' trong view '{view.Name}': {ex.Message}");
            }
        }

        return placed;
    }

    /// <summary>
    ///     Lấy reference TAG được cho rebar. Revit 2023+ yêu cầu reference subelement (1 thanh con),
    ///     không nhận reference cả set. Thử GetSubelements trước; fallback new Reference(rebar) (rebar đơn).
    /// </summary>
    private static Reference? GetTaggableReference(Rebar rebar)
    {
        try
        {
            var subelements = rebar.GetSubelements();
            if (subelements != null && subelements.Count > 0)
            {
                var reference = subelements[0].GetReference();
                if (reference != null) return reference;
            }
        }
        catch
        {
            // GetSubelements không khả dụng → fallback bên dưới.
        }

        try { return new Reference(rebar); }
        catch { return null; }
    }

    /// <summary>
    ///     Điểm đặt tag — tâm bounding box của rebar TRONG view (để tag nằm cạnh thép, không văng ra
    ///     gốc toạ độ). Fallback: điểm đầu centerline; cuối cùng tâm bbox view.
    /// </summary>
    private static XYZ GetTagPoint(Rebar rebar, View view)
    {
        try
        {
            var bbox = rebar.get_BoundingBox(view);
            if (bbox != null) return (bbox.Min + bbox.Max) * 0.5;
        }
        catch { /* fallback */ }

        try
        {
            var curves = rebar.GetCenterlineCurves(false, false, false,
                MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            if (curves.Count > 0) return curves[0].GetEndPoint(0);
        }
        catch { /* fallback */ }

        var viewBox = view.get_BoundingBox(view);
        return viewBox != null ? (viewBox.Min + viewBox.Max) * 0.5 : XYZ.Zero;
    }
}
