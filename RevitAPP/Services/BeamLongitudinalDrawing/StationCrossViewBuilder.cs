using Autodesk.Revit.DB;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

public sealed class StationCrossViewBuilder
{
    public ViewSection Create(Document document, ElementId typeId, BoundingBoxXYZ box,
        int scale, ElementId? templateId, string name)
    {
        var view = LongitudinalViewBuilder.CreateByType(document, typeId, box);
        LongitudinalViewBuilder.Configure(view, scale, templateId, name);
        // The section box was already applied by CreateSection/CreateDetail.
        view.CropBoxActive = true;
        view.CropBoxVisible = false;
        return view;
    }
}
