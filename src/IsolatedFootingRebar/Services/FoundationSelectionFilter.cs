using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace IsolatedFootingRebar.Services;

/// <summary>
///     Lọc selection chỉ cho phép pick móng kết cấu (category OST_StructuralFoundation) — móng đơn
///     thường là FamilyInstance.
/// </summary>
public sealed class FoundationSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
        => elem.Category?.Id.ToValue() == (long)BuiltInCategory.OST_StructuralFoundation;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
