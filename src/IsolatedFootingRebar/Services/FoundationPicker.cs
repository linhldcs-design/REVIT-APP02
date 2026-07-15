using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace IsolatedFootingRebar.Services;

/// <summary>
///     Cho người dùng pick một móng kết cấu trong view. Trả về null nếu người dùng hủy (ESC).
/// </summary>
public sealed class FoundationPicker
{
    public Element? PickFoundation(UIDocument uiDocument)
    {
        var doc = uiDocument.Document;
        try
        {
            var reference = uiDocument.Selection.PickObject(
                ObjectType.Element,
                new FoundationSelectionFilter(),
                "Chọn một móng đơn (Structural Foundation).");

            return doc.GetElement(reference);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }
}
