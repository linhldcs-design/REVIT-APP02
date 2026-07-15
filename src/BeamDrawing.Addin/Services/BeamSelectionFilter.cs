using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace BeamDrawing.Addin.Services;

/// <summary>Chỉ cho phép chọn FamilyInstance thuộc category Khung kết cấu (dầm).</summary>
public sealed class BeamSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
        => elem is FamilyInstance && elem.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
