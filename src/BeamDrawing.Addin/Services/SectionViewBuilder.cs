using Autodesk.Revit.DB;
using BeamDrawing.Core.Models;

namespace BeamDrawing.Addin.Services;

/// <summary>
///     Tạo section view (sectional elevation + cross section) từ BoundingBoxXYZ của
///     <see cref="SectionPlaneCalculator"/>, gán scale + view template + tên view.
///     PHẢI gọi trong Transaction đang mở.
/// </summary>
public sealed class SectionViewBuilder
{
    private readonly SectionPlaneCalculator _planeCalculator = new();

    public ViewSection CreateSectional(Document doc, BeamGeometry beam, ElementId sectionTypeId,
        ViewConfig config, ElementId? viewTemplateId, string viewName)
    {
        var box = _planeCalculator.CreateSectionalBox(beam);
        var view = ViewSection.CreateSection(doc, sectionTypeId, box);
        ApplyConfig(view, config.Scale, viewTemplateId, viewName);
        return view;
    }

    public ViewSection CreateCrossSection(Document doc, BeamGeometry beam, double t, ElementId sectionTypeId,
        ViewConfig config, ElementId? viewTemplateId, string viewName)
    {
        var box = _planeCalculator.CreateCrossSectionBox(beam, t);
        var view = ViewSection.CreateSection(doc, sectionTypeId, box);
        ApplyConfig(view, config.Scale, viewTemplateId, viewName);
        return view;
    }

    private static void ApplyConfig(ViewSection view, int scale, ElementId? viewTemplateId, string viewName)
    {
        if (scale > 0) view.Scale = scale;

        // Bật crop để view ôm sát vùng section box (không hiện khung trắng thừa).
        view.CropBoxActive = true;
        view.CropBoxVisible = true;

        // Đặt tên view — nếu trùng, Revit ném exception; thử thêm hậu tố.
        TrySetUniqueName(view, viewName);

        // Gán view template sau cùng (template có thể khoá scale → set scale trước).
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
