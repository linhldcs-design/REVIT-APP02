using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BeamRebarPro.Services;

/// <summary>
///     Cho người dùng pick một hoặc nhiều dầm kết cấu trong view. Trả về danh sách FamilyInstance;
///     rỗng nếu người dùng hủy (ESC).
/// </summary>
public sealed class BeamPicker
{
    public IReadOnlyList<FamilyInstance> PickBeams(UIDocument uiDocument)
    {
        var doc = uiDocument.Document;
        try
        {
            var refs = uiDocument.Selection.PickObjects(
                ObjectType.Element,
                new BeamSelectionFilter(),
                "Chọn dầm (có thể chọn nhiều dầm liên tục). Nhấn Finish khi xong.");

            return refs
                .Select(r => doc.GetElement(r))
                .OfType<FamilyInstance>()
                .ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return [];
        }
    }
}
