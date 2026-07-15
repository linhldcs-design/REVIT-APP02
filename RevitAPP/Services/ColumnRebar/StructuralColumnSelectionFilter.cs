using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using RevitAPP.Helpers;

namespace RevitAPP.Services.ColumnRebar;

/// <summary>Chỉ cho phép chọn FamilyInstance thuộc category Cột kết cấu.</summary>
public sealed class StructuralColumnSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
        => elem is FamilyInstance && elem.Category?.Id.ToValue() == (long)BuiltInCategory.OST_StructuralColumns;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
