using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace FootingDrawing.Addin.Services;

/// <summary>Cho người dùng pick móng kết cấu. Trả rỗng nếu hủy (ESC).</summary>
public sealed class FoundationPicker
{
    public IReadOnlyList<Element> PickFoundations(UIDocument uiDocument)
    {
        var doc = uiDocument.Document;
        try
        {
            var references = uiDocument.Selection.PickObjects(
                ObjectType.Element,
                new FoundationSelectionFilter(),
                "Chọn một hoặc nhiều móng đơn (Structural Foundation), bấm Finish để tiếp tục.");

            return references.Select(doc.GetElement).Where(e => e != null).ToList()!;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return [];
        }
    }
}
