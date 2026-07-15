using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BeamRebar.Addin.Services;

/// <summary>
///     Cho user pick một hoặc nhiều dầm (StructuralFraming) trong model.
///     Trả danh sách rỗng nếu user huỷ (Esc), kèm error rỗng (không phải lỗi thật).
/// </summary>
public sealed class BeamPicker
{
    public IReadOnlyList<FamilyInstance> PickBeams(UIDocument uiDocument, out string error)
    {
        error = string.Empty;
        var document = uiDocument.Document;

        try
        {
            var references = uiDocument.Selection.PickObjects(
                ObjectType.Element, new BeamSelectionFilter(), "Chọn dầm cần tạo thép (Esc để kết thúc)");

            var beams = references
                .Select(r => document.GetElement(r))
                .OfType<FamilyInstance>()
                .ToList();

            if (beams.Count == 0)
                error = "Không có dầm nào được chọn.";

            return beams;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            // User nhấn Esc — không phải lỗi.
            return [];
        }
    }
}
