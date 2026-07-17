using Autodesk.Revit.DB;
using BeamRebarPro.Services;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using ElementIdCompat = RevitAPP.Chat.Tools.ChatElementIdCompat;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Tool vẽ thép dầm. <see cref="BeamRebarApi"/> (orchestrator) tự mở transaction → RequiresTransaction=false.
///     Mỗi dầm gọi riêng (list 1 phần tử) để thép không tràn sang dầm khác cùng trục.
/// </summary>
public sealed class DrawBeamRebarTool : IChatTool
{
    public string Name => "draw_beam_rebar";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(
        Name,
        "Vẽ thép dầm (thép chủ trên/dưới, gia cường, cốt đai, thép chống phình) cho các dầm chỉ định. " +
        "Đơn vị mm. Bỏ trống beamIds để dùng các dầm đang chọn trong Revit.",
        new JsonSchemaBuilder()
            .IntegerArray("beamIds", "Danh sách ElementId dầm; bỏ trống để dùng selection hiện tại.")
            .Integer("mainTopCount", "Số thép chủ trên.")
            .Integer("mainTopDiameterMm", "Đường kính thép chủ trên (mm).")
            .Integer("mainBottomCount", "Số thép chủ dưới.")
            .Integer("mainBottomDiameterMm", "Đường kính thép chủ dưới (mm).")
            .Number("mainAnchorLengthMm", "Chiều dài neo thép chủ (mm).")
            .Number("mainTopBendDownLengthMm", "Chiều dài bẻ xuống thép chủ trên (mm).")
            .Number("mainTopBendDownFromHeightMinusMm", "Bẻ xuống tính từ chiều cao trừ (mm).")
            .Bool("topAddEnabled", "Bật thép gia cường trên lớp 1.")
            .Integer("topAddCount", "Số thép gia cường trên lớp 1.")
            .Integer("topAddDiameterMm", "Đường kính gia cường trên lớp 1 (mm).")
            .Number("topAddLengthMm", "Chiều dài gia cường trên lớp 1 (mm).")
            .Number("topAddEdgeHookDownMm", "Móc bẻ xuống gối gia cường trên lớp 1 (mm).")
            .Bool("topAddL2Enabled", "Bật gia cường trên lớp 2.")
            .Integer("topAddL2Count", "Số gia cường trên lớp 2.")
            .Integer("topAddL2DiameterMm", "Đường kính gia cường trên lớp 2 (mm).")
            .Number("topAddL2LengthMm", "Chiều dài gia cường trên lớp 2 (mm).")
            .Number("topAddL2EdgeHookDownMm", "Móc bẻ xuống gia cường trên lớp 2 (mm).")
            .Bool("bottomAddEnabled", "Bật gia cường dưới lớp 1.")
            .Integer("bottomAddCount", "Số gia cường dưới lớp 1.")
            .Integer("bottomAddDiameterMm", "Đường kính gia cường dưới lớp 1 (mm).")
            .Number("bottomAddLengthMm", "Chiều dài gia cường dưới lớp 1 (mm).")
            .Bool("bottomAddL2Enabled", "Bật gia cường dưới lớp 2.")
            .Integer("bottomAddL2Count", "Số gia cường dưới lớp 2.")
            .Integer("bottomAddL2DiameterMm", "Đường kính gia cường dưới lớp 2 (mm).")
            .Number("bottomAddL2LengthMm", "Chiều dài gia cường dưới lớp 2 (mm).")
            .Integer("stirrupDiameterMm", "Đường kính đai (mm).")
            .Number("stirrupSpacingEndMm", "Khoảng cách đai vùng gối (mm).")
            .Number("stirrupSpacingMidMm", "Khoảng cách đai vùng nhịp (mm).")
            .Bool("antiBulgeEnabled", "Bật thép chống phình.")
            .Integer("antiBulgeDiameterMm", "Đường kính thép chống phình (mm).")
            .Number("antiBulgeSpacingMm", "Khoảng cách thép chống phình (mm).")
            .Number("antiBulgeHeightThresholdMm", "Ngưỡng chiều cao dầm bật chống phình (mm).")
            .Integer("coverMm", "Lớp bảo vệ (mm).")
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var idTokens = input["beamIds"] as JArray
                       ?? throw new ArgumentException("Thiếu 'beamIds' (mảng id dầm).");
        var beamIds = idTokens.Select(t => ElementIdCompat.Create(t.Value<long>())).ToList();
        if (beamIds.Count == 0) throw new ArgumentException("'beamIds' rỗng.");

        var idValues = beamIds.Select(ElementIdCompat.Value).ToList();
        var savedModel = HasExplicitParameters(input) ? null : BeamConfigStore.Load(idValues);
        var options = savedModel is null ? BuildOptions(input) : null;
        int longitudinal = 0, stirrup = 0, antiBulge = 0, ok = 0;
        var warnings = new List<string>();
        foreach (var id in beamIds)
        {
            // Mỗi dầm 1 lệnh riêng để orchestrator không gộp các dầm cùng trục thành 1 run nhiều nhịp.
            var result = savedModel is not null
                ? BeamRebarApi.DrawForBeams(ctx.Doc, new[] { id }, savedModel)
                : BeamRebarApi.DrawForBeams(ctx.Doc, new[] { id }, options);
            ok++;
            longitudinal += result.LongitudinalCount;
            stirrup += result.StirrupCount;
            antiBulge += result.AntiBulgeCount;
            warnings.AddRange(result.Warnings);
        }

        var message = $"Đã tạo {longitudinal} thanh thép dọc, {stirrup} đai, " +
                      $"{antiBulge} thép chống phình cho {ok}/{beamIds.Count} dầm.";
        if (warnings.Count > 0) message += " | " + string.Join("; ", warnings.Distinct());
        return new { success = true, message, usedSavedConfig = savedModel is not null };
    }

    private static bool HasExplicitParameters(JObject input) =>
        input.Properties().Any(p => !string.Equals(p.Name, "beamIds", StringComparison.OrdinalIgnoreCase));

    private static BeamRebarApiOptions BuildOptions(JObject p)
    {
        var d = new BeamRebarApiOptions();
        return d with
        {
            MainTopCount = p.Value<int?>("mainTopCount") ?? d.MainTopCount,
            MainTopDiameterMm = p.Value<int?>("mainTopDiameterMm") ?? d.MainTopDiameterMm,
            MainBottomCount = p.Value<int?>("mainBottomCount") ?? d.MainBottomCount,
            MainBottomDiameterMm = p.Value<int?>("mainBottomDiameterMm") ?? d.MainBottomDiameterMm,
            MainAnchorLengthMm = p.Value<double?>("mainAnchorLengthMm") ?? d.MainAnchorLengthMm,
            MainTopBendDownLengthMm = p.Value<double?>("mainTopBendDownLengthMm") ?? d.MainTopBendDownLengthMm,
            MainTopBendDownFromHeightMinusMm =
                p.Value<double?>("mainTopBendDownFromHeightMinusMm") ?? d.MainTopBendDownFromHeightMinusMm,
            TopAddEnabled = p.Value<bool?>("topAddEnabled") ?? d.TopAddEnabled,
            TopAddCount = p.Value<int?>("topAddCount") ?? d.TopAddCount,
            TopAddDiameterMm = p.Value<int?>("topAddDiameterMm") ?? d.TopAddDiameterMm,
            TopAddLengthMm = p.Value<double?>("topAddLengthMm") ?? d.TopAddLengthMm,
            TopAddEdgeHookDownMm = p.Value<double?>("topAddEdgeHookDownMm") ?? d.TopAddEdgeHookDownMm,
            TopAddL2Enabled = p.Value<bool?>("topAddL2Enabled") ?? d.TopAddL2Enabled,
            TopAddL2Count = p.Value<int?>("topAddL2Count") ?? d.TopAddL2Count,
            TopAddL2DiameterMm = p.Value<int?>("topAddL2DiameterMm") ?? d.TopAddL2DiameterMm,
            TopAddL2LengthMm = p.Value<double?>("topAddL2LengthMm") ?? d.TopAddL2LengthMm,
            TopAddL2EdgeHookDownMm = p.Value<double?>("topAddL2EdgeHookDownMm") ?? d.TopAddL2EdgeHookDownMm,
            BottomAddEnabled = p.Value<bool?>("bottomAddEnabled") ?? d.BottomAddEnabled,
            BottomAddCount = p.Value<int?>("bottomAddCount") ?? d.BottomAddCount,
            BottomAddDiameterMm = p.Value<int?>("bottomAddDiameterMm") ?? d.BottomAddDiameterMm,
            BottomAddLengthMm = p.Value<double?>("bottomAddLengthMm") ?? d.BottomAddLengthMm,
            BottomAddL2Enabled = p.Value<bool?>("bottomAddL2Enabled") ?? d.BottomAddL2Enabled,
            BottomAddL2Count = p.Value<int?>("bottomAddL2Count") ?? d.BottomAddL2Count,
            BottomAddL2DiameterMm = p.Value<int?>("bottomAddL2DiameterMm") ?? d.BottomAddL2DiameterMm,
            BottomAddL2LengthMm = p.Value<double?>("bottomAddL2LengthMm") ?? d.BottomAddL2LengthMm,
            StirrupDiameterMm = p.Value<int?>("stirrupDiameterMm") ?? d.StirrupDiameterMm,
            StirrupSpacingEndMm = p.Value<double?>("stirrupSpacingEndMm") ?? d.StirrupSpacingEndMm,
            StirrupSpacingMidMm = p.Value<double?>("stirrupSpacingMidMm") ?? d.StirrupSpacingMidMm,
            AntiBulgeEnabled = p.Value<bool?>("antiBulgeEnabled") ?? d.AntiBulgeEnabled,
            AntiBulgeDiameterMm = p.Value<int?>("antiBulgeDiameterMm") ?? d.AntiBulgeDiameterMm,
            AntiBulgeSpacingMm = p.Value<double?>("antiBulgeSpacingMm") ?? d.AntiBulgeSpacingMm,
            AntiBulgeHeightThresholdMm =
                p.Value<double?>("antiBulgeHeightThresholdMm") ?? d.AntiBulgeHeightThresholdMm,
            CoverMm = p.Value<int?>("coverMm") ?? d.CoverMm
        };
    }
}
