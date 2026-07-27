using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Services.BeamDrawing;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

public sealed class LongitudinalProjectResourceProvider
{
    public LongitudinalProjectResources Load(Document document)
    {
        var source = new ProjectResourceProvider().LoadResources(document);
        return new LongitudinalProjectResources(
            source.DimensionTypeNames, source.RebarTagTypeNames, source.BreakLineFamilyNames,
            source.ViewportTypeNames, source.SectionTypeNames, source.SpotElevationTypeNames,
            source.ViewTemplateNames, source.ExistingSheets, source.MultiRebarAnnotationTypeNames);
    }
}
