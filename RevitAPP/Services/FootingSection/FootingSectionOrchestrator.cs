using Autodesk.Revit.DB;
using RevitAPP.Core.Models.FootingSection;
using RevitAPP.Helpers;
using RevitAPP.Services.BeamDrawing;
using Serilog;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Điều phối sinh mặt cắt móng. TransactionGroup bọc các Transaction con TÁCH BIỆT:
///     T1 tạo view + sheet + viewport (commit → tự regenerate) → T2 annotation (sau regenerate, reference hợp lệ).
///     Lỗi giữa chừng → rollback group.
/// </summary>
public sealed class FootingSectionOrchestrator
{
    private readonly ProjectResourceProvider _resources = new();
    private readonly FootingSectionViewBuilder _viewBuilder = new();
    private readonly SheetBuilder _sheetBuilder = new();

    /// <summary>Callback annotation chạy trong T2. Command gán; null = chỉ tạo view/sheet.</summary>
    public IFootingSectionAnnotator? Annotator { get; set; }

    public FootingSectionResult Generate(Document doc, Element footing, FootingSectionGeometry geometry,
        FootingSectionSetting setting)
    {
        var result = new FootingSectionResult();

        using var group = new TransactionGroup(doc, "Trien khai mat cat mong");
        group.Start();
        try
        {
            FootingViewContext? context = null;

            using (var t1 = new Transaction(doc, "Tao view + sheet mong"))
            {
                t1.Start();
                context = CreateViewAndSheet(doc, footing, geometry, setting, result);
                t1.Commit(); // Commit tự regenerate — view mới có reference hợp lệ cho T2.
            }

            var hasAnnotation = setting.Flags.TagEnabled || setting.Flags.DimEnabled || setting.Flags.BreakLineEnabled;
            if (Annotator != null && context != null && hasAnnotation)
            {
                using var t2 = new Transaction(doc, "Ghi chu mat cat mong");
                t2.Start();
                Annotator.Annotate(doc, context, setting, result);
                t2.Commit();

                if (Annotator is IFootingSectionPostCommitAnnotator postCommit && setting.Flags.DimEnabled)
                {
                    using (var t3 = new Transaction(doc, "Hop nhat dim chuoi mong"))
                    {
                        t3.Start();
                        postCommit.FinalizeAfterCommit(doc, context, result);
                        t3.Commit();
                    }

                    using var t4 = new Transaction(doc, "Don dim tam mong");
                    t4.Start();
                    postCommit.CleanupAfterFinalize(doc, context, result);
                    t4.Commit();
                }
            }

            group.Assimilate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sinh mat cat mong that bai — rollback");
            group.RollBack();
            throw;
        }

        return result;
    }

    private FootingViewContext CreateViewAndSheet(Document doc, Element footing, FootingSectionGeometry geometry,
        FootingSectionSetting setting, FootingSectionResult result)
    {
        var sectionTypeId = _resources.ResolveSectionType(doc, setting.SectionTypeName, result.Warnings);
        var templateId = _resources.ResolveViewTemplate(doc, setting.ViewTemplateName, result.Warnings);
        var viewportTypeId = _resources.ResolveViewportType(doc, setting.ViewportTypeName, result.Warnings);

        var viewName = string.IsNullOrWhiteSpace(setting.Flags.ViewName)
            ? $"MC-{geometry.Mark}"
            : setting.Flags.ViewName;

        var view = _viewBuilder.Create(doc, geometry, sectionTypeId, setting.Scale, templateId, viewName);
        result.SectionViewId = view.Id.ToValue();

        var sheet = _sheetBuilder.ResolveSheet(doc, setting.Sheet, result.Warnings);
        result.SheetId = sheet.Id.ToValue();

        // Đặt viewport ở giữa vùng vẽ (trái khung tên). Grid nhiều mặt cắt để follow-up.
        var point = new XYZ(0.5, 0.8, 0);
        var viewport = _sheetBuilder.PlaceView(doc, sheet, view.Id, point, viewportTypeId, result.Warnings);
        if (viewport != null) result.ViewportId = viewport.Id.ToValue();

        return new FootingViewContext(view, footing, geometry);
    }
}
