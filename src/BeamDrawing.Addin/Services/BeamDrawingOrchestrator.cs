using Autodesk.Revit.DB;
using BeamDrawing.Addin.Models;
using BeamDrawing.Addin.Services.Annotation;
using BeamDrawing.Core.Models;
using Serilog;

namespace BeamDrawing.Addin.Services;

/// <summary>
///     Điều phối toàn bộ quá trình sinh bản vẽ cho các dầm đã chọn.
///     Dùng TransactionGroup với các Transaction con TÁCH BIỆT: tạo view (commit) → Regenerate →
///     annotate (commit) — vì tag/dimension cần view đã commit + regenerate mới có reference hợp lệ.
///     Lỗi giữa chừng → rollback toàn bộ group.
/// </summary>
public sealed class BeamDrawingOrchestrator
{
    private readonly ProjectResourceProvider _resources = new();
    private readonly BeamGeometryReader _geometryReader = new();
    private readonly SectionViewBuilder _viewBuilder = new();
    private readonly SheetBuilder _sheetBuilder = new();
    private readonly RequiredFamilyValidator _familyValidator = new();
    private readonly AnnotationOrchestrator _annotation = new();

    public BeamDrawingResult Generate(Document doc, IReadOnlyList<FamilyInstance> beams, BeamDrawingSetting setting)
    {
        var result = new BeamDrawingResult();

        // Validate family bắt buộc trước (chỉ warn, không chặn).
        foreach (var missing in _familyValidator.FindMissing(doc, setting))
            result.Warnings.Add(missing);

        using var group = new TransactionGroup(doc, "Beam Drawing");
        group.Start();
        try
        {
            // ── T1: tạo view + sheet + viewport ──
            using (var t1 = new Transaction(doc, "Tạo view dầm"))
            {
                t1.Start();
                CreateViewsAndSheet(doc, beams, setting, result);
                t1.Commit(); // Commit tự regenerate — view mới có reference hợp lệ cho T2.
            }

            // ── T2: annotation (Phase 5 hiện thực) ──
            using (var t2 = new Transaction(doc, "Ghi chú bản vẽ dầm"))
            {
                t2.Start();
                _annotation.Annotate(doc, beams, setting, result);
                t2.Commit();
            }

            group.Assimilate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sinh bản vẽ dầm thất bại — rollback");
            group.RollBack();
            throw;
        }

        return result;
    }

    private void CreateViewsAndSheet(Document doc, IReadOnlyList<FamilyInstance> beams,
        BeamDrawingSetting setting, BeamDrawingResult result)
    {
        var sectionTypeId = _resources.ResolveSectionType(doc, setting.Sectional.SectionTypeName, result.Warnings);
        var crossSectionTypeId = _resources.ResolveSectionType(doc, setting.CrossSection.SectionTypeName, result.Warnings);
        var sectionalTemplateId = _resources.ResolveViewTemplate(doc, setting.Sectional.ViewTemplateName, result.Warnings);
        var crossTemplateId = _resources.ResolveViewTemplate(doc, setting.CrossSection.ViewTemplateName, result.Warnings);

        var sheet = _sheetBuilder.ResolveSheet(doc, setting, result.Warnings);
        result.SheetId = sheet.Id;

        var index = 0;
        foreach (var beam in beams)
        {
            if (!_geometryReader.TryRead(doc, beam, out var geometry, out var error))
            {
                result.Warnings.Add(error);
                continue;
            }

            var sectional = _viewBuilder.CreateSectional(doc, geometry, sectionTypeId,
                setting.Sectional, sectionalTemplateId, $"MCD-Dầm-{beam.Id}");
            result.SectionViewIds.Add(sectional.Id);
            PlaceOnSheet(doc, sheet, sectional.Id, index, 0, result);

            if (setting.Flags.CrossSection)
            {
                // 3 mặt cắt ngang: đầu / giữa / cuối.
                var ci = 0;
                foreach (var t in new[] { 0.1, 0.5, 0.9 })
                {
                    var cross = _viewBuilder.CreateCrossSection(doc, geometry, t, crossSectionTypeId,
                        setting.CrossSection, crossTemplateId, $"MCN-Dầm-{beam.Id}-{ci}");
                    result.CrossSectionViewIds.Add(cross.Id);
                    PlaceOnSheet(doc, sheet, cross.Id, index, ci + 1, result);
                    ci++;
                }
            }

            index++;
        }
    }

    /// <summary>Lưới đặt viewport đơn giản trên sheet: cột theo dầm, hàng theo loại view.</summary>
    private void PlaceOnSheet(Document doc, ViewSheet sheet, ElementId viewId, int col, int row,
        BeamDrawingResult result)
    {
        var point = new XYZ(0.5 + col * 0.7, 1.2 - row * 0.4, 0);
        _sheetBuilder.PlaceView(doc, sheet, viewId, point, result.Warnings);
    }
}
