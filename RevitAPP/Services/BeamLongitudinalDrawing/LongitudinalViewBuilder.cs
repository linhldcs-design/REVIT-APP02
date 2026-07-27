using Autodesk.Revit.DB;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

public sealed class LongitudinalViewBuilder
{
    public ViewSection Create(Document document, ElementId typeId, BoundingBoxXYZ box,
        int scale, ElementId? templateId, string name)
    {
        var view = CreateByType(document, typeId, box);
        Configure(view, scale, templateId, name);
        return view;
    }

    internal static ViewSection CreateByType(Document document, ElementId typeId, BoundingBoxXYZ box)
    {
        var type = document.GetElement(typeId) as ViewFamilyType
                   ?? throw new InvalidOperationException("Section Type không còn tồn tại.");
        return type.ViewFamily == ViewFamily.Detail
            ? ViewSection.CreateDetail(document, typeId, box)
            : ViewSection.CreateSection(document, typeId, box);
    }

    internal static void Configure(ViewSection view, int scale, ElementId? templateId, string baseName)
    {
        if (scale > 0) view.Scale = scale;
        view.CropBoxActive = true;
        view.CropBoxVisible = false;
        for (var index = 0; index < 100; index++)
        {
            try { view.Name = index == 0 ? baseName : $"{baseName} ({index})"; break; }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        }
        if (templateId is { } id && id != ElementId.InvalidElementId) view.ViewTemplateId = id;
    }
}
