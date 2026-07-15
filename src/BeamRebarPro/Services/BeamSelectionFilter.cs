using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace BeamRebarPro.Services;

/// <summary>
///     Lọc selection chỉ cho phép pick dầm kết cấu (category OST_StructuralFraming). Dùng khi người
///     dùng chọn dải dầm cho add-in.
/// </summary>
public sealed class BeamSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        return elem is FamilyInstance fi
               && fi.Category?.Id.ToValue() == (long)BuiltInCategory.OST_StructuralFraming;
    }

    public bool AllowReference(Reference reference, XYZ position) => false;
}
