using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;
using RevitAPP.Services.BeamDrawing;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Tool sinh bản vẽ triển khai dầm (view mặt cắt + tag + dim + sheet). Orchestrator tự bọc TransactionGroup
///     → RequiresTransaction=false. Chỉ port cấu hình từ JObject; logic sinh bản vẽ giữ nguyên trong engine.
/// </summary>
public sealed class DrawBeamDrawingTool : IChatTool
{
    public string Name => "draw_beam_drawing";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(
        Name,
        "Sinh bản vẽ triển khai dầm (mặt cắt dọc/ngang + rebar tag + dimension + đặt lên sheet) cho các dầm " +
        "đã có thép. Bỏ trống beamIds để dùng dầm đang chọn; có thể dùng preset đã lưu.",
        new JsonSchemaBuilder()
            .IntegerArray("beamIds", "Danh sách ElementId dầm đã có thép.")
            .Text("presetName", "Tên preset bản vẽ dầm đã lưu.")
            .Integer("scale", "Tỉ lệ chung (vd 15 cho 1:15).")
            .Text("sheetNumber", "Số hiệu sheet có sẵn để đặt bản vẽ.")
            .Text("sheetName", "Tên sheet.")
            .Bool("longSection", "Vẽ mặt cắt dọc.")
            .Bool("crossSection", "Vẽ mặt cắt ngang.")
            .Bool("spotEnabled", "Bật spot elevation.")
            .Bool("dimEnabled", "Bật dimension.")
            .Bool("breakLine", "Vẽ break-line.")
            .Bool("createView3D", "Tạo view 3D.")
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var idsToken = input["beamIds"] as JArray
                       ?? throw new ArgumentException("Thiếu 'beamIds' (mảng id dầm).");
        var beamIds = idsToken.Select(t => t.Value<long>()).ToList();
        if (beamIds.Count == 0) throw new ArgumentException("'beamIds' rỗng.");

        var beams = new List<FamilyInstance>();
        foreach (var id in beamIds)
        {
            if (ctx.Doc.GetElement(ChatElementIdCompat.Create(id)) is FamilyInstance fi)
                beams.Add(fi);
        }
        if (beams.Count == 0)
            throw new ArgumentException("Không tìm thấy dầm hợp lệ từ beamIds đã cho.");

        var orchestrator = new BeamDrawingOrchestrator { Annotator = new BeamAnnotator() };
        var presetName = input.Value<string?>("presetName")?.Trim();
        var preset = string.IsNullOrWhiteSpace(presetName) ? null : new BeamDrawingPresetStore().Load()
            .FirstOrDefault(p => string.Equals(p.SettingName, presetName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(presetName) && preset is null)
            throw new ArgumentException($"Không tìm thấy preset bản vẽ dầm '{presetName}'.");
        var result = orchestrator.Generate(ctx.Doc, beams, preset ?? BuildSetting(input));

        var message = $"Đã tạo {result.TotalViews} view ({result.SectionViewIds.Count} mặt cắt dọc, " +
                      $"{result.CrossSectionViewIds.Count} mặt cắt ngang) cho {beams.Count} dầm.";
        if (result.Warnings.Count > 0)
            message += " | Cảnh báo: " + string.Join("; ", result.Warnings.Distinct());
        return new { success = true, message, sheetId = result.SheetId, presetName };
    }

    private static BeamDrawingSetting BuildSetting(JObject p)
    {
        var d = BeamDrawingSettingFactory.CreateDefault();

        var scale = p.Value<int?>("scale") ?? BeamDrawingSettingFactory.DefaultScale;
        var sectional = new PerViewConfig(
            p.Value<int?>("sectionalScale") ?? scale,
            Str(p, "sectionalSectionType") ?? d.Sectional.SectionTypeName,
            Str(p, "sectionalViewTemplate") ?? d.Sectional.ViewTemplateName,
            Str(p, "sectionalViewport") ?? d.Sectional.ViewportTypeName);
        var cross = new PerViewConfig(
            p.Value<int?>("crossScale") ?? scale,
            Str(p, "crossSectionType") ?? d.CrossSection.SectionTypeName,
            Str(p, "crossViewTemplate") ?? d.CrossSection.ViewTemplateName,
            Str(p, "crossViewport") ?? d.CrossSection.ViewportTypeName);

        var tags = new RebarTagSet(
            Str(p, "tagT1"), Str(p, "tagT2"), Str(p, "tagMidItem"),
            Str(p, "tagD0"), Str(p, "tagD1"), Str(p, "tagD2"),
            Str(p, "tagD3"), Str(p, "tagD4"), Str(p, "tagD5"),
            p.Value<bool?>("rebarBreakSymbol") ?? d.Tags.RebarBreakSymbol);

        var spot = new SpotElevationConfig(
            p.Value<bool?>("spotEnabled") ?? d.Spot.Enabled,
            Str(p, "spotType") ?? d.Spot.TypeName,
            p.Value<double?>("spotOffsetMm") ?? d.Spot.OffsetMm);

        var dim = new DimensionConfig(
            p.Value<bool?>("dimEnabled") ?? d.Dim.Enabled,
            Str(p, "sectionalDimType") ?? d.Dim.SectionalDimTypeName,
            Str(p, "crossDimType") ?? d.Dim.CrossDimTypeName,
            p.Value<int?>("dimSpacingFactor") ?? d.Dim.SpacingFactor,
            p.Value<double?>("distanceToSideBeamMm") ?? d.Dim.DistanceToSideBeamMm,
            p.Value<double?>("distanceToBotFaceMm") ?? d.Dim.DistanceToBotFaceMm);

        var sheet = new SheetConfig(
            Str(p, "sheetNumber") ?? d.Sheet.Number,
            Str(p, "sheetName") ?? d.Sheet.Name,
            Str(p, "titleBlock") ?? d.Sheet.TitleBlockName);

        var flags = new DrawingFlags(
            p.Value<bool?>("longSection") ?? d.Flags.LongSection,
            p.Value<bool?>("crossSection") ?? d.Flags.CrossSection,
            p.Value<bool?>("crossSectionForMultiBeam") ?? d.Flags.CrossSectionForMultiBeam,
            p.Value<bool?>("pickPillowToDim") ?? d.Flags.PickPillowToDim,
            p.Value<bool?>("createView3D") ?? d.Flags.CreateView3D,
            Str(p, "longSectionViewName"),
            Str(p, "crossSectionViewName"));

        var crossAnn = new CrossAnnotationConfig(
            Str(p, "endLongitudinalMraType"),
            Str(p, "endStirrupTagType"),
            Str(p, "midLongitudinalMraType"),
            Str(p, "midStirrupTagType"),
            Str(p, "endReinforceL1MraType"),
            Str(p, "midReinforceL1MraType"),
            Str(p, "endReinforceL2MraType"),
            Str(p, "midReinforceL2MraType"));

        return new BeamDrawingSetting(
            SettingName: Str(p, "settingName"),
            Sectional: sectional,
            CrossSection: cross,
            Tags: tags,
            Spot: spot,
            Dim: dim,
            BreakLine: p.Value<bool?>("breakLine") ?? d.BreakLine,
            BreakLineFamilyName: Str(p, "breakLineFamily"),
            Sheet: sheet,
            Flags: flags,
            CrossAnnotation: crossAnn);
    }

    private static string? Str(JObject p, string key)
    {
        var v = p.Value<string?>(key);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
