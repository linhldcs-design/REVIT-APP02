using Autodesk.Revit.DB;
using FootingDrawing.Addin.Services.Annotation;
using FootingDrawing.Core.Models;
using Serilog;

namespace FootingDrawing.Addin.Services;

/// <summary>
///     Điều phối toàn bộ quá trình sinh bản vẽ mặt bằng thép móng cho MỘT móng đã chọn.
///     TransactionGroup với 2 Transaction con tách biệt: T1 tạo view + sheet + viewport (commit → regenerate,
///     để reference hợp lệ) → T2 annotate. Lỗi giữa chừng → rollback toàn bộ group.
/// </summary>
public sealed class FootingDrawingOrchestrator
{
    private readonly FootingGeometryReader _geometryReader = new();
    private readonly PlanViewBuilder _viewBuilder = new();
    private readonly SheetBuilder _sheetBuilder = new();
    private readonly FootingAnnotationOrchestrator _annotation = new();
    private readonly ProjectResourceProvider _resources = new();

    public FootingDrawingResult Generate(Document doc, Element footing, FootingDrawingSetting setting)
    {
        var result = new FootingDrawingResult();
        var mark = FoundationInfo.GetMark(footing);

        if (!_geometryReader.TryRead(footing, null, out var geometry, out var geoError))
            throw new InvalidOperationException(geoError);

        using var group = new TransactionGroup(doc, "Bản Vẽ Móng");
        group.Start();
        try
        {
            View view;
            ViewSheet sheet;

            // ── T1: tạo plan view + sheet + viewport ──
            using (var t1 = new Transaction(doc, "Tạo view + sheet móng"))
            {
                t1.Start();
                var templateId = _resources.ResolveViewTemplate(doc, setting.ViewTemplateName, result.Warnings);
                var parentView = _resources.ResolveParentPlanView(doc, setting.ParentPlanViewName);
                var calloutTypeId = _resources.ResolveCalloutType(doc, setting.CalloutTypeName);
                var viewName = string.IsNullOrEmpty(mark)
                    ? $"{setting.TitlePrefix} {footing.Id.ToLong()}"
                    : $"{setting.TitlePrefix} {mark}";
                view = _viewBuilder.Create(doc, parentView, calloutTypeId, geometry, setting.Scale,
                    templateId, viewName, result.Warnings);
                result.ViewId = view.Id.ToLong();

                sheet = _sheetBuilder.ResolveSheet(doc, setting, result.Warnings);
                result.SheetId = sheet.Id.ToLong();

                var viewportTypeId = _resources.ResolveType(doc, BuiltInCategory.OST_Viewports,
                    setting.ViewportTypeName, result.Warnings);
                var center = (sheet.Outline.Max + sheet.Outline.Min) * 0.5;
                var placePoint = new XYZ(center.U, center.V, 0);
                var viewport = _sheetBuilder.PlaceView(doc, sheet, view.Id, placePoint, viewportTypeId, result.Warnings);
                if (viewport == null)
                    throw new InvalidOperationException("Không đặt được mặt bằng móng lên sheet; đã hoàn tác view vừa tạo.");
                result.ViewportId = viewport.Id.ToLong();

                t1.Commit(); // Commit tự regenerate — view có reference hợp lệ cho T2.
            }

            // ── T2: annotation ──
            using (var t2 = new Transaction(doc, "Ghi chú bản vẽ móng"))
            {
                t2.Start();
                var failureOptions = t2.GetFailureHandlingOptions();
                failureOptions.SetFailuresPreprocessor(new FailureMessageCollector(result.Warnings));
                t2.SetFailureHandlingOptions(failureOptions);

                _annotation.Annotate(doc, view, footing, geometry, setting, _resources, result);

                t2.Commit();
            }

            group.Assimilate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sinh bản vẽ móng thất bại — rollback");
            group.RollBack();
            throw;
        }

        return result;
    }
}
