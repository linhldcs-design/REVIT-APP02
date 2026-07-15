using Autodesk.Revit.DB;
using FootingDrawing.Core.Models;

namespace FootingDrawing.Addin.Services;

/// <summary>
///     Tìm ViewSheet đích theo SheetNumber trong setting và đặt plan view lên bằng Viewport
///     (gán ViewportType nếu chỉ định). PHẢI gọi trong Transaction đang mở.
/// </summary>
public sealed class SheetBuilder
{
    private const double ViewportUpOffsetFeet = 0.9 / 12.0;

    public ViewSheet ResolveSheet(Document doc, FootingDrawingSetting setting, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(setting.SheetNumber))
            throw new InvalidOperationException("Chưa chọn Sheet đích có sẵn.");

        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .FirstOrDefault(s => s.SheetNumber == setting.SheetNumber);

        if (existing != null) return existing;
        throw new InvalidOperationException($"Không tìm thấy Sheet đích có sẵn '{setting.SheetNumber}'.");
    }

    /// <summary>Đặt view lên sheet + gán viewport type. Trả viewport hoặc null nếu không đặt được.</summary>
    public Viewport? PlaceView(Document doc, ViewSheet sheet, ElementId viewId, XYZ point,
        ElementId? viewportTypeId, List<string> warnings)
    {
        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
        {
            warnings.Add("Plan view không thể đặt lên sheet (có thể đã nằm trên sheet khác).");
            return null;
        }

        var viewport = Viewport.Create(doc, sheet.Id, viewId, point);
        if (viewport != null && viewportTypeId != null && viewportTypeId != ElementId.InvalidElementId)
        {
            try { viewport.ChangeTypeId(viewportTypeId); }
            catch { warnings.Add("Không gán được Viewport type — dùng type mặc định."); }
        }
        if (viewport != null) CenterViewportWithTitleSpace(viewport, sheet);
        return viewport;
    }

    private static void CenterViewportWithTitleSpace(Viewport viewport, ViewSheet sheet)
    {
        var center = (sheet.Outline.Max + sheet.Outline.Min) * 0.5;
        viewport.SetBoxCenter(new XYZ(center.U, center.V + ViewportUpOffsetFeet, 0));
    }
}
