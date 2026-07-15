using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;
using Serilog;
using RevitAPP.Helpers;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Điều phối sinh bản vẽ dầm. TransactionGroup bọc các Transaction con TÁCH BIỆT:
///     T1 tạo view + sheet + viewport (commit → tự regenerate) → T2 annotation (Phase 5, sau regenerate).
///     Tag/dimension cần view đã commit + regenerate mới có reference hợp lệ. Lỗi giữa chừng → rollback group.
/// </summary>
public sealed class BeamDrawingOrchestrator
{
    private readonly ProjectResourceProvider _resources = new();
    private readonly BeamGeometryReader _geometryReader = new();
    private readonly SectionViewBuilder _viewBuilder = new();
    private readonly SheetBuilder _sheetBuilder = new();
    private readonly BeamSupportFinder _supportFinder = new();

    /// <summary>
    ///     Callback annotation chạy trong T2 (sau khi view commit + regenerate). Phase 5 gán;
    ///     null = chưa annotate (chỉ tạo view/sheet).
    /// </summary>
    public IBeamAnnotator? Annotator { get; set; }

    public BeamDrawingResult Generate(Document doc, IReadOnlyList<FamilyInstance> beams, BeamDrawingSetting setting)
    {
        var result = new BeamDrawingResult();
        var context = new List<ViewBeamPair>();
        // Mỗi dầm → nhóm viewport cross (GỐI, NHỊP) để căn lại cho xếp gọn sau khi annotate.
        var crossViewportGroups = new List<List<ElementId>>();

        using var group = new TransactionGroup(doc, "Trien khai ban ve dam");
        group.Start();
        try
        {
            using (var t1 = new Transaction(doc, "Tao view + sheet dam"))
            {
                t1.Start();
                CreateViewsAndSheet(doc, beams, setting, result, context, crossViewportGroups);
                t1.Commit(); // Commit tự regenerate — view mới có reference hợp lệ cho T2.
            }

            var hasAnnotation = setting.Tags != RebarTagSet.Empty || setting.Dim.Enabled || setting.Spot.Enabled ||
                                setting.BreakLine;
            if (Annotator != null && hasAnnotation)
            {
                // T1 commit đã tự regenerate → view mới có reference hợp lệ. KHÔNG gọi doc.Regenerate()
                // ngoài transaction (Add-In Manager chặn mọi sửa đổi khi không có transaction mở).
                using var t2 = new Transaction(doc, "Ghi chu ban ve dam");
                t2.Start();
                Annotator.Annotate(doc, context, setting, result);
                t2.Commit();
            }

            // Căn lại vị trí viewport cross cho XẾP GỌN (sau annotate — box đã ổn định).
            using (var t3 = new Transaction(doc, "Xep gon viewport dam"))
            {
                t3.Start();
                _sheetBuilder.ArrangeCrossViewports(doc, crossViewportGroups, result.Warnings);
                t3.Commit();
            }

            group.Assimilate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sinh ban ve dam that bai — rollback");
            group.RollBack();
            throw;
        }

        return result;
    }

    private void CreateViewsAndSheet(Document doc, IReadOnlyList<FamilyInstance> beams,
        BeamDrawingSetting setting, BeamDrawingResult result, List<ViewBeamPair> context,
        List<List<ElementId>> crossViewportGroups)
    {
        var sectionTypeId = _resources.ResolveSectionType(doc, setting.Sectional.SectionTypeName, result.Warnings);
        var crossTypeId = _resources.ResolveSectionType(doc, setting.CrossSection.SectionTypeName, result.Warnings);
        var sectionalTemplateId = _resources.ResolveViewTemplate(doc, setting.Sectional.ViewTemplateName, result.Warnings);
        var crossTemplateId = _resources.ResolveViewTemplate(doc, setting.CrossSection.ViewTemplateName, result.Warnings);
        var sectionalViewportId = _resources.ResolveViewportType(doc, setting.Sectional.ViewportTypeName, result.Warnings);
        var crossViewportId = _resources.ResolveViewportType(doc, setting.CrossSection.ViewportTypeName, result.Warnings);

        var sheet = _sheetBuilder.ResolveSheet(doc, setting.Sheet, result.Warnings);
        result.SheetId = sheet.Id.ToValue();

        var col = 0;
        foreach (var beam in beams)
        {
            if (!_geometryReader.TryRead(doc, beam, out var geometry, out var error))
            {
                result.Warnings.Add(error);
                continue;
            }

            var mark = BeamMark(beam);

            if (setting.Flags.LongSection)
            {
                var sectionalBaseName = string.IsNullOrWhiteSpace(setting.Flags.LongSectionViewName)
                    ? $"MCD-{mark}"
                    : setting.Flags.LongSectionViewName;
                var sectional = _viewBuilder.CreateSectional(doc, geometry, sectionTypeId,
                    setting.Sectional.Scale, sectionalTemplateId, sectionalBaseName);
                result.SectionViewIds.Add(sectional.Id.ToValue());
                context.Add(new ViewBeamPair(sectional, beam, geometry, IsCross: false));
                PlaceOnSheet(doc, sheet, sectional.Id, col, 0, sectionalViewportId, result);
            }

            if (setting.Flags.CrossSection)
            {
                // Cắt ĐÚNG vị trí: GỐI (cạnh cột) + NHỊP (giữa nhịp) — dò cột giao dầm, kể cả dầm nhiều nhịp.
                var (supportT, midSpanT) = _supportFinder.FindStations(doc, beam, geometry);
                var stations = new (double T, bool IsSupport)[] { (supportT, true), (midSpanT, false) };
                var ci = 0;
                var crossGroup = new List<ElementId>();
                crossViewportGroups.Add(crossGroup);
                foreach (var (t, isSupport) in stations)
                {
                    var crossBaseName = string.IsNullOrWhiteSpace(setting.Flags.CrossSectionViewName)
                        ? $"MCN-{mark}"
                        : setting.Flags.CrossSectionViewName;
                    var stationName = isSupport ? "GOI" : "NHIP";
                    var cross = _viewBuilder.CreateCrossSection(doc, geometry, t, crossTypeId,
                        setting.CrossSection.Scale, crossTemplateId, $"{crossBaseName}-{stationName}");
                    var zone = isSupport ? "GỐI" : "NHỊP";
                    TrySetTitleOnSheet(cross, $"DẦM {mark} - {zone}");
                    result.CrossSectionViewIds.Add(cross.Id.ToValue());
                    // Station = t THẬT (để lọc rebar + cắt đúng chỗ); IsSupportZone = cờ GỐI/NHỊP (phân vùng tag).
                    context.Add(new ViewBeamPair(cross, beam, geometry, IsCross: true, Station: t, IsSupportZone: isSupport));
                    var vp = setting.Flags.LongSection
                        ? PlaceOnSheet(doc, sheet, cross.Id, col, ci + 1, crossViewportId, result)
                        : PlaceOnSheet(doc, sheet, cross.Id, col * stations.Length + ci, 0,
                            crossViewportId, result);
                    if (vp != null) crossGroup.Add(vp.Id);
                    ci++;
                }
            }

            col++;
        }
    }

    /// <summary>Lưới đặt viewport: cột theo dầm, hàng theo loại view (sectional row 0, cross row 1..3).</summary>
    private Viewport? PlaceOnSheet(Document doc, ViewSheet sheet, ElementId viewId, int col, int row,
        ElementId? viewportTypeId, BeamDrawingResult result)
    {
        var point = new XYZ(0.5 + col * 0.9, 1.3 - row * 0.45, 0);
        return _sheetBuilder.PlaceView(doc, sheet, viewId, point, viewportTypeId, result.Warnings);
    }

    private static string BeamMark(FamilyInstance beam)
    {
        var mark = beam.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        return string.IsNullOrWhiteSpace(mark) ? $"Dam-{beam.Id.ToValue()}" : mark;
    }

    private static void TrySetTitleOnSheet(View view, string title)
    {
        try
        {
            var parameter = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
            if (parameter is { IsReadOnly: false }) parameter.Set(title);
        }
        catch
        {
            // View type/template có thể khóa Title on Sheet; giữ tên view làm fallback.
        }
    }
}
