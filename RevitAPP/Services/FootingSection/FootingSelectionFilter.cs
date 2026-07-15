using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using RevitAPP.Helpers;

namespace RevitAPP.Services.FootingSection;

/// <summary>Chỉ cho phép chọn móng (StructuralFoundation).</summary>
public sealed class FootingSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) =>
        elem.Category?.Id.ToValue() == (long)BuiltInCategory.OST_StructuralFoundation;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
