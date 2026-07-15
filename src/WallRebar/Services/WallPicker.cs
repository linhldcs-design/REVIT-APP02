using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace WallRebar.Services;

/// <summary>
///     Cho người dùng pick một tường trong view. Trả về null nếu người dùng hủy (ESC).
/// </summary>
public sealed class WallPicker
{
    public Wall? PickWall(UIDocument uiDocument)
    {
        var doc = uiDocument.Document;
        try
        {
            var reference = uiDocument.Selection.PickObject(
                ObjectType.Element,
                new WallSelectionFilter(),
                "Chọn một tường (Wall).");

            return doc.GetElement(reference) as Wall;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }
}
