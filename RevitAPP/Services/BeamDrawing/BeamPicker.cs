using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitAPP.Helpers;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Chọn 1..n dầm để tạo bản vẽ. Ưu tiên dầm ĐÃ được chọn trước khi bấm lệnh; nếu chưa có thì pick
///     tương tác. Trả rỗng khi user huỷ (Esc) — error rỗng nghĩa là huỷ, không phải lỗi thật.
/// </summary>
public sealed class BeamPicker
{
    public IReadOnlyList<FamilyInstance> PickBeams(UIDocument uiDocument, out string error)
    {
        error = string.Empty;
        var document = uiDocument.Document;

        // 1. Dùng dầm đã chọn sẵn (nếu hợp lệ).
        var preselected = uiDocument.Selection.GetElementIds()
            .Select(id => document.GetElement(id))
            .OfType<FamilyInstance>()
            .Where(fi => fi.Category?.Id.ToValue() == (long)BuiltInCategory.OST_StructuralFraming)
            .ToList();

        if (preselected.Count > 0) return preselected;

        // 2. Pick tương tác.
        try
        {
            var references = uiDocument.Selection.PickObjects(
                ObjectType.Element, new BeamSelectionFilter(), "Chọn dầm cần tạo bản vẽ (Esc để kết thúc)");

            var beams = references
                .Select(r => document.GetElement(r))
                .OfType<FamilyInstance>()
                .ToList();

            if (beams.Count == 0) error = "Không có dầm nào được chọn.";
            return beams;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return [];
        }
    }
}
