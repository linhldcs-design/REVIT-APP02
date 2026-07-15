using Autodesk.Revit.DB;
using BeamDrawing.Core.Models;

namespace BeamDrawing.Addin.Services;

/// <summary>
///     Tìm/tạo ViewSheet theo sheet number trong setting và đặt các view lên sheet bằng Viewport.
///     PHẢI gọi trong Transaction đang mở.
/// </summary>
public sealed class SheetBuilder
{
    /// <summary>Tìm sheet theo number; không có → tạo mới với title block đầu tiên (hoặc theo setting).</summary>
    public ViewSheet ResolveSheet(Document doc, BeamDrawingSetting setting, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(setting.SheetNumber))
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(s => s.SheetNumber == setting.SheetNumber);

            if (existing != null)
            {
                warnings.Add($"Sheet '{setting.SheetNumber}' đã tồn tại — đặt view lên sheet này.");
                return existing;
            }
        }

        var titleBlockId = ResolveTitleBlock(doc, setting.TitleBlockName, warnings);
        var sheet = ViewSheet.Create(doc, titleBlockId);
        if (!string.IsNullOrWhiteSpace(setting.SheetNumber)) TrySetSheetNumber(sheet, setting.SheetNumber!);
        if (!string.IsNullOrWhiteSpace(setting.SheetName)) sheet.Name = setting.SheetName;
        return sheet;
    }

    /// <summary>Đặt view lên sheet tại điểm cho trước (UV trên sheet). Trả viewport hoặc null nếu view đã ở sheet khác.</summary>
    public Viewport? PlaceView(Document doc, ViewSheet sheet, ElementId viewId, XYZ point, List<string> warnings)
    {
        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
        {
            warnings.Add("Một view không thể đặt lên sheet (có thể đã nằm trên sheet khác).");
            return null;
        }
        return Viewport.Create(doc, sheet.Id, viewId, point);
    }

    private static ElementId ResolveTitleBlock(Document doc, string? titleBlockName, List<string> warnings)
    {
        var titleBlocks = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsElementType()
            .ToList();

        if (titleBlocks.Count == 0)
        {
            warnings.Add("Project không có Title Block — sheet tạo không có khung tên.");
            return ElementId.InvalidElementId;
        }

        if (!string.IsNullOrWhiteSpace(titleBlockName))
        {
            var match = titleBlocks.FirstOrDefault(t => t.Name == titleBlockName);
            if (match != null) return match.Id;
            warnings.Add($"Không tìm thấy Title Block '{titleBlockName}', dùng '{titleBlocks[0].Name}'.");
        }

        return titleBlocks[0].Id;
    }

    private static void TrySetSheetNumber(ViewSheet sheet, string number)
    {
        try { sheet.SheetNumber = number; }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { /* trùng số — giữ số Revit tự gán */ }
    }
}
