using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace WallRebar.Services;

/// <summary>
///     Lọc selection chỉ cho phép pick tường (category OST_Walls).
/// </summary>
public sealed class WallSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
        => elem.Category?.Id.ToValue() == (long)BuiltInCategory.OST_Walls;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
