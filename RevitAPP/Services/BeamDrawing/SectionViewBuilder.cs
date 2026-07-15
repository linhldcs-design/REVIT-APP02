using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Tạo section view (sectional elevation + cross section) từ BoundingBoxXYZ của
///     <see cref="SectionPlaneCalculator"/>, gán scale + view template + tên view. PHẢI trong Transaction đang mở.
/// </summary>
public sealed class SectionViewBuilder
{
    private readonly SectionPlaneCalculator _planeCalculator = new();

    public ViewSection CreateSectional(Document doc, BeamGeometry beam, ElementId sectionTypeId,
        int scale, ElementId? viewTemplateId, string viewName)
    {
        var box = _planeCalculator.CreateSectionalBox(beam);
        var view = CreateSectionOrDetail(doc, sectionTypeId, box);
        ApplyConfig(view, scale, viewTemplateId, viewName);
        return view;
    }

    public ViewSection CreateCrossSection(Document doc, BeamGeometry beam, double t, ElementId sectionTypeId,
        int scale, ElementId? viewTemplateId, string viewName)
    {
        var box = _planeCalculator.CreateCrossSectionBox(beam, t);
        var view = CreateSectionOrDetail(doc, sectionTypeId, box);
        ApplyConfig(view, scale, viewTemplateId, viewName);
        // View Template có thể ghi đè chiều sâu lấy từ section box. Ép lại sau cùng để Properties luôn = 150 mm.
        ApplyCrossFarClipOffset(view);
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

    private static void ApplyCrossFarClipOffset(ViewSection view)
    {
        var parameter = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)
                        ?? throw new InvalidOperationException("Không tìm thấy parameter Far Clip Offset của mặt cắt ngang.");
        if (parameter.IsReadOnly)
            throw new InvalidOperationException("View Template đang khóa Far Clip Offset; không thể ép về 150 mm.");

        var targetFeet = BeamSectionBoxMath.CrossFarClipOffsetMm / BeamSectionBoxMath.MillimetersPerFoot;
        if (!parameter.Set(targetFeet) || Math.Abs(parameter.AsDouble() - targetFeet) > 1e-6)
            throw new InvalidOperationException("Không thể đặt Far Clip Offset mặt cắt ngang về 150 mm.");
    }

    private static void ApplyConfig(ViewSection view, int scale, ElementId? viewTemplateId, string viewName)
    {
        if (scale > 0) view.Scale = scale;

        view.CropBoxActive = true;
        view.CropBoxVisible = false; // Ẩn viền crop cho MỌI view khi xuất bản vẽ (crop vẫn cắt hình).

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
