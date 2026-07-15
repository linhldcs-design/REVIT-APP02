using Autodesk.Revit.DB;
using RevitAPP.Core.Models.FootingSection;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Tạo section view mặt cắt móng từ <see cref="BoundingBoxXYZ"/> của <see cref="FootingSectionPlaneCalculator"/>,
///     gán scale + view template + tên view. PHẢI trong Transaction đang mở.
/// </summary>
public sealed class FootingSectionViewBuilder
{
    private readonly FootingSectionPlaneCalculator _planeCalculator = new();

    public ViewSection Create(Document doc, FootingSectionGeometry footing, ElementId sectionTypeId,
        int scale, ElementId? viewTemplateId, string viewName)
    {
        var box = _planeCalculator.CreateBox(footing);
        var view = CreateSectionOrDetail(doc, sectionTypeId, box);
        ApplyConfig(view, scale, viewTemplateId, viewName);
        // View Template có thể ghi đè chiều sâu từ section box. Ép lại sau cùng để Far Clip Offset = 500mm.
        ApplyFarClipOffset(view);
        return view;
    }

    private static ViewSection CreateSectionOrDetail(Document doc, ElementId typeId, BoundingBoxXYZ box)
    {
        var type = doc.GetElement(typeId) as ViewFamilyType
                   ?? throw new InvalidOperationException("Section Type đã chọn không còn tồn tại trong project.");

        return type.ViewFamily == ViewFamily.Detail
            ? ViewSection.CreateDetail(doc, typeId, box)
            : ViewSection.CreateSection(doc, typeId, box);
    }

    private static void ApplyFarClipOffset(ViewSection view)
    {
        var parameter = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
        if (parameter is null || parameter.IsReadOnly) return; // template khoá → giữ nguyên box depth.

        var targetFeet = FootingSectionPlaneCalculator.FarClipOffsetMm / BeamSectionBoxMath.MillimetersPerFoot;
        parameter.Set(targetFeet);
    }

    private static void ApplyConfig(ViewSection view, int scale, ElementId? viewTemplateId, string viewName)
    {
        if (scale > 0) view.Scale = scale;

        view.CropBoxActive = true;
        view.CropBoxVisible = false; // Ẩn viền crop khi xuất bản vẽ (crop vẫn cắt hình).

        TrySetUniqueName(view, viewName);

        // Gán template sau cùng (template có thể khoá scale → set scale trước).
        if (viewTemplateId != null && viewTemplateId != ElementId.InvalidElementId)
            view.ViewTemplateId = viewTemplateId;
    }

    private static void TrySetUniqueName(View view, string baseName)
    {
        for (var i = 0; i < 50; i++)
        {
            try
            {
                view.Name = i == 0 ? baseName : $"{baseName} ({i})";
                return;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Tên trùng — thử hậu tố tiếp theo.
            }
        }
    }
}
