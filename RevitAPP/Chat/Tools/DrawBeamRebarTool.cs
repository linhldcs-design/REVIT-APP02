using Autodesk.Revit.DB;
using BeamRebarPro.Services;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Services;
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
        "Đơn vị mm. Bỏ trống beamIds để dùng các dầm đang chọn trong Revit. " +
        "Khi người dùng yêu cầu vẽ theo bảng Excel, truyền excelFilePath để parser áp cấu hình theo Mark; không tự suy diễn thông số từ bảng.",
        new JsonSchemaBuilder()
            .IntegerArray("beamIds", "Danh sách ElementId dầm; bỏ trống để dùng selection hiện tại.")
            .Text("excelFilePath", "Đường dẫn đầy đủ file Excel chứa bảng thép dầm theo Mark.")
            .Text("excelSheetName", "Tên sheet Excel; bỏ trống để dùng sheet đầu tiên.")
            .Integer("excelHeaderRow", "Số dòng tiêu đề, đếm từ 1; mặc định 1.")
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

        var excelPath = input.Value<string?>("excelFilePath")?.Trim();
        Dictionary<string, BeamRebarScheduleRow>? schedule = null;
        if (input["excelHeaders"] is JArray liveHeaders && input["excelRows"] is JArray liveRows)
        {
            schedule = BeamRebarScheduleParser.Parse(
                    liveHeaders.Values<string>().Where(value => value is not null).Cast<string>().ToList(),
                    liveRows.Select(row => (IReadOnlyList<object?>)((JArray)row).Select(value =>
                        value.Type == JTokenType.Null ? null : (object?)value.ToString()).ToList()).ToList())
                .ToDictionary(row => row.Mark, StringComparer.OrdinalIgnoreCase);
        }
        else if (!string.IsNullOrWhiteSpace(excelPath))
        {
            var headerRow = input.Value<int?>("excelHeaderRow") ?? 1;
            var table = ExcelWorkbookReader.Read(excelPath, input.Value<string?>("excelSheetName"),
                headerRow, headerRow + 1, 1000, 100);
            schedule = BeamRebarScheduleParser.Parse(table.Headers, table.Rows)
                .GroupBy(row => row.Mark, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group =>
                {
                    if (group.Count() > 1) throw new FormatException($"Mark '{group.Key}' bị lặp trong bảng Excel.");
                    return group.Single();
                }, StringComparer.OrdinalIgnoreCase);
        }

        var idValues = beamIds.Select(ElementIdCompat.Value).ToList();
        var savedModel = schedule is null && !HasExplicitParameters(input) ? BeamConfigStore.Load(idValues) : null;
        var options = schedule is null && savedModel is null ? BuildOptions(input) : null;
        int longitudinal = 0, stirrup = 0, antiBulge = 0, ok = 0;
        var warnings = new List<string>();
        var appliedMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in beamIds)
        {
            BeamRebarApiOptions? beamOptions = options;
            if (schedule is not null)
            {
                var beam = ctx.Doc.GetElement(id);
                var mark = beam?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()?.Trim();
                if (string.IsNullOrWhiteSpace(mark) || !schedule.TryGetValue(mark, out var row))
                {
                    warnings.Add($"Dầm {ElementIdCompat.Value(id)} có Mark '{mark ?? "(trống)"}' không tồn tại trong bảng Excel; đã bỏ qua.");
                    continue;
                }
                beamOptions = BuildOptions(row);
                appliedMarks.Add(mark);
            }

            // Mỗi dầm 1 lệnh riêng để orchestrator không gộp các dầm cùng trục thành 1 run nhiều nhịp.
            var result = savedModel is not null
                ? BeamRebarApi.DrawForBeams(ctx.Doc, new[] { id }, savedModel)
                : BeamRebarApi.DrawForBeams(ctx.Doc, new[] { id }, beamOptions);
            var created = result.LongitudinalCount + result.StirrupCount + result.AntiBulgeCount;
            if (created > 0) ok++;
            else warnings.Add($"Dầm {ElementIdCompat.Value(id)} không tạo được thanh thép nào.");
            longitudinal += result.LongitudinalCount;
            stirrup += result.StirrupCount;
            antiBulge += result.AntiBulgeCount;
            warnings.AddRange(result.Warnings);
        }

        var message = $"Đã tạo {longitudinal} thanh thép dọc, {stirrup} đai, " +
                      $"{antiBulge} thép chống phình cho {ok}/{beamIds.Count} dầm.";
        if (schedule is not null) message += $" Đã áp bảng Excel theo Mark: {string.Join(", ", appliedMarks.OrderBy(value => value))}.";
        if (warnings.Count > 0) message += " | " + string.Join("; ", warnings.Distinct());
        return new { success = ok > 0, message, usedSavedConfig = savedModel is not null,
            usedExcelSchedule = schedule is not null, appliedMarks = appliedMarks.OrderBy(value => value).ToArray() };
    }

    private static bool HasExplicitParameters(JObject input) =>
        input.Properties().Any(p => !string.Equals(p.Name, "beamIds", StringComparison.OrdinalIgnoreCase));

    private static BeamRebarApiOptions BuildOptions(BeamRebarScheduleRow row)
    {
        var options = new BeamRebarApiOptions
        {
            MainTopCount = row.MainTop.Count,
            MainTopDiameterMm = row.MainTop.DiameterMm,
            MainTopBendDownFromHeightMinusMm = row.MainTop.BendDownFromHeightMinusMm,
            MainBottomCount = row.MainBottom.Count,
            MainBottomDiameterMm = row.MainBottom.DiameterMm,
            StirrupDiameterMm = row.Stirrup.DiameterMm,
            StirrupSpacingEndMm = row.Stirrup.EndSpacingMm,
            StirrupSpacingMidMm = row.Stirrup.MidSpacingMm
        };

        if (row.Support.Layer == 2)
            options = options with { TopAddL2Enabled = row.Support.Enabled, TopAddL2Count = row.Support.Count,
                TopAddL2DiameterMm = row.Support.DiameterMm,
                TopAddL2EdgeHookDownFromHeightMinusMm = row.Support.BendDownFromHeightMinusMm };
        else
            options = options with { TopAddEnabled = row.Support.Enabled, TopAddCount = row.Support.Count,
                TopAddDiameterMm = row.Support.DiameterMm,
                TopAddEdgeHookDownFromHeightMinusMm = row.Support.BendDownFromHeightMinusMm };

        if (row.Midspan.Layer == 2)
            options = options with { BottomAddL2Enabled = row.Midspan.Enabled, BottomAddL2Count = row.Midspan.Count,
                BottomAddL2DiameterMm = row.Midspan.DiameterMm };
        else
            options = options with { BottomAddEnabled = row.Midspan.Enabled, BottomAddCount = row.Midspan.Count,
                BottomAddDiameterMm = row.Midspan.DiameterMm };
        return options;
    }

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

/// <summary>Đường gọi xác định cho yêu cầu “vẽ theo Excel đang mở”; tự nối workbook với tool dầm.</summary>
public sealed class DrawBeamRebarFromOpenExcelTool : IChatTool
{
    public string Name => "draw_beam_rebar_from_open_excel";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(
        Name,
        "Vẽ thép các dầm đang chọn theo bảng Excel hiện đang mở. Tự tìm workbook và áp từng dòng theo Instance Mark; dùng tool này thay cho read_excel_table + tự suy diễn.",
        new JsonSchemaBuilder()
            .IntegerArray("beamIds", "Danh sách ElementId dầm; bỏ trống để dùng dầm đang chọn.")
            .Text("sheetName", "Tên sheet; bỏ trống để dùng sheet đầu tiên.")
            .Integer("headerRow", "Dòng tiêu đề, mặc định 1.")
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var files = OpenExcelWorkbookFinder.Find();
        if (files.Count == 0) throw new InvalidOperationException("Không tìm thấy workbook Excel đã lưu đang mở.");
        if (files.Count > 1)
            throw new InvalidOperationException("Đang mở nhiều workbook Excel; hãy đóng bớt hoặc dùng draw_beam_rebar với excelFilePath cụ thể. " +
                                                string.Join("; ", files));

        var forwarded = (JObject)input.DeepClone();
        var table = OpenExcelTableReader.Read(files[0], input.Value<string?>("sheetName"),
            input.Value<int?>("headerRow") ?? 1);
        forwarded["excelFilePath"] = files[0];
        forwarded["excelHeaders"] = new JArray(table.Headers);
        forwarded["excelRows"] = new JArray(table.Rows.Select(row => new JArray(row)));
        forwarded.Remove("sheetName");
        forwarded.Remove("headerRow");
        return new DrawBeamRebarTool().Execute(forwarded, ctx);
    }
}
