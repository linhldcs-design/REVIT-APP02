using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace FootingDrawing.Addin.Services;

/// <summary>Lọc selection chỉ cho phép pick móng kết cấu (OST_StructuralFoundation).</summary>
public sealed class FoundationSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
        => elem.Category?.Id.ToLong() == (long)BuiltInCategory.OST_StructuralFoundation;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
