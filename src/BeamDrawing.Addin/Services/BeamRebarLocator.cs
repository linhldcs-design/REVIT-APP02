using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace BeamDrawing.Addin.Services;

/// <summary>
///     Tìm các thanh thép (Rebar) host bởi một dầm cụ thể — phục vụ đặt tag ở Phase 5.
/// </summary>
public sealed class BeamRebarLocator
{
    public IReadOnlyList<Rebar> GetRebars(Document document, FamilyInstance beam)
    {
        var beamId = beam.Id;

        return new FilteredElementCollector(document)
            .OfClass(typeof(Rebar))
            .WhereElementIsNotElementType()
            .Cast<Rebar>()
            .Where(rebar => rebar.GetHostId() == beamId)
            .ToList();
    }
}
