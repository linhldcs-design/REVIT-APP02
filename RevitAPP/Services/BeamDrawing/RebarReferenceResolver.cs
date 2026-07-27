using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitAPP.Services.BeamDrawing;

internal static class RebarReferenceResolver
{
    public static IReadOnlyList<Reference> FromExistingTags(Rebar rebar)
    {
        var result = new List<Reference>();
        var stable = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var tag in new FilteredElementCollector(rebar.Document)
                         .OfClass(typeof(IndependentTag))
                         .WhereElementIsNotElementType()
                         .Cast<IndependentTag>())
            {
                try
                {
                    if (!tag.GetTaggedLocalElementIds().Contains(rebar.Id)) continue;
                    foreach (var reference in tag.GetTaggedReferences())
                    {
                        if (reference == null || reference.ElementId != rebar.Id) continue;
                        var key = reference.ConvertToStableRepresentation(rebar.Document);
                        if (stable.Add(key)) result.Add(reference);
                    }
                }
                catch
                {
                    // Tag thuộc loại không expose tagged reference: bỏ qua.
                }
            }
        }
        catch
        {
            // Không có tag mẫu: caller tiếp tục bằng subelement của Rebar.
        }
        return result;
    }
}
