using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitAPP.Helpers;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Chọn 1 móng để tạo mặt cắt. Ưu tiên móng ĐÃ được chọn trước khi bấm lệnh; nếu chưa có thì pick
///     tương tác. Trả null khi user huỷ (Esc) — error rỗng nghĩa là huỷ, không phải lỗi thật.
/// </summary>
public sealed class FootingPicker
{
    public Element? PickFooting(UIDocument uiDocument, out string error)
    {
        error = string.Empty;
        var document = uiDocument.Document;

        // 1. Dùng móng đã chọn sẵn (nếu hợp lệ) — lấy cái đầu tiên.
        var preselected = uiDocument.Selection.GetElementIds()
            .Select(id => document.GetElement(id))
            .FirstOrDefault(e => e.Category?.Id.ToValue() == (long)BuiltInCategory.OST_StructuralFoundation);

        if (preselected != null) return preselected;

        // 2. Pick tương tác.
        try
        {
            var reference = uiDocument.Selection.PickObject(
                ObjectType.Element, new FootingSelectionFilter(), "Chọn móng cần tạo mặt cắt");
            return document.GetElement(reference);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return null;
        }
    }
}
