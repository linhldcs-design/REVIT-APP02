using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using RevitAPP.Helpers;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>Chỉ cho phép chọn dầm (StructuralFraming).</summary>
public sealed class BeamSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) =>
        elem.Category?.Id.ToValue() == (long)BuiltInCategory.OST_StructuralFraming;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
