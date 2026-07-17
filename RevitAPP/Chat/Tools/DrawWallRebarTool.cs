using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using WallRebar.Services;
using ElementIdCompat = RevitAPP.Chat.Tools.ChatElementIdCompat;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Tool vẽ thép tường. <see cref="WallRebarApi.DrawForWall"/> tự mở transaction → RequiresTransaction=false.
/// </summary>
public sealed class DrawWallRebarTool : IChatTool
{
    public string Name => "draw_wall_rebar";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(
        Name,
        "Vẽ thép cho tường (2 lưới dọc + ngang 2 mặt + thép giằng) cho các tường chỉ định. " +
        "Đơn vị mm. Bỏ trống wallIds để dùng các tường đang chọn trong Revit.",
        new JsonSchemaBuilder()
            .IntegerArray("wallIds", "Danh sách ElementId tường; bỏ trống để dùng selection hiện tại.")
            .Text("presetName", "Tên cấu hình đã lưu trong WallRebar add-in, ví dụ V1. Khi có giá trị này, dùng nguyên preset thay cho các thông số rời.")
            .Integer("verticalDiameterMm", "Đường kính thép dọc (mm).")
            .Number("verticalSpacingMm", "Khoảng cách thép dọc (mm).")
            .Integer("horizontalDiameterMm", "Đường kính thép ngang (mm).")
            .Number("horizontalSpacingMm", "Khoảng cách thép ngang (mm).")
            .Integer("tieDiameterMm", "Đường kính thép giằng (mm).")
            .Number("tieSpacingMm", "Khoảng cách thép giằng (mm).")
            .Bool("tieEnabled", "Bật thép giằng.")
            .Number("coverTopBottomMm", "Lớp bảo vệ trên/dưới (mm).")
            .Number("coverLeftRightMm", "Lớp bảo vệ trái/phải (mm).")
            .Number("coverStartEndMm", "Lớp bảo vệ đầu/cuối (mm).")
            .Text("topHookType", "Kiểu móc trên.")
            .Number("topHookLengthMm", "Chiều dài móc trên (mm).")
            .Text("bottomHookType", "Kiểu móc dưới.")
            .Number("bottomHookLengthMm", "Chiều dài móc dưới (mm).")
            .Number("topOffsetMm", "Offset trên (mm).")
            .Number("bottomOffsetMm", "Offset dưới (mm).")
            .Number("horizontalOffsetStartMm", "Offset ngang đầu (mm).")
            .Number("horizontalOffsetEndMm", "Offset ngang cuối (mm).")
            .Bool("drawAdditionalRebar", "Vẽ thép bổ sung.")
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var wallIds = ParseIds(input, "wallIds", "wallId", "tường");
        var requestedPreset = input.Value<string?>("presetName")?.Trim();
        var presetName = string.IsNullOrEmpty(requestedPreset)
            ? null
            : WallConfigStore.Names().FirstOrDefault(name =>
                string.Equals(name, requestedPreset, StringComparison.OrdinalIgnoreCase));
        var preset = presetName == null ? null : WallConfigStore.Load(presetName);
        if (!string.IsNullOrEmpty(requestedPreset) && preset == null)
            throw new ArgumentException($"Không tìm thấy cấu hình '{requestedPreset}'. Các cấu hình hiện có: {string.Join(", ", WallConfigStore.Names())}.");

        var options = preset == null ? BuildOptions(input) : null;

        int vert = 0, horiz = 0, tie = 0, ok = 0;
        var warnings = new List<string>();
        foreach (var id in wallIds)
        {
            var result = preset == null
                ? WallRebarApi.DrawForWall(ctx.Doc, id, options)
                : WallRebarApi.DrawForWall(ctx.Doc, id, preset);
            if (result.Succeeded) ok++;
            vert += result.VerticalBarCount;
            horiz += result.HorizontalBarCount;
            tie += result.TieCount;
            warnings.AddRange(result.Warnings);
        }

        var message = $"Đã tạo {vert} set thép dọc, {horiz} set thép ngang, {tie} thép giằng cho {ok}/{wallIds.Count} tường.";
        if (warnings.Count > 0) message += " | " + string.Join("; ", warnings.Distinct());
        return new { success = ok > 0, preset = presetName, message };
    }

    internal static List<ElementId> ParseIds(JObject p, string arrayKey, string singleKey, string label)
    {
        var ids = new List<ElementId>();
        if (p[arrayKey] is JArray arr)
            ids.AddRange(arr.Select(t => ElementIdCompat.Create(t.Value<long>())));
        else if (p[singleKey] != null)
            ids.Add(ElementIdCompat.Create(p[singleKey]!.Value<long>()));

        if (ids.Count == 0)
            throw new ArgumentException($"Thiếu '{arrayKey}' (mảng) hoặc '{singleKey}' (id {label}).");
        return ids;
    }

    private static WallRebarApiOptions BuildOptions(JObject p)
    {
        var d = new WallRebarApiOptions();
        return d with
        {
            VerticalDiameterMm = p.Value<int?>("verticalDiameterMm") ?? d.VerticalDiameterMm,
            VerticalSpacingMm = p.Value<double?>("verticalSpacingMm") ?? d.VerticalSpacingMm,
            HorizontalDiameterMm = p.Value<int?>("horizontalDiameterMm") ?? d.HorizontalDiameterMm,
            HorizontalSpacingMm = p.Value<double?>("horizontalSpacingMm") ?? d.HorizontalSpacingMm,
            TieDiameterMm = p.Value<int?>("tieDiameterMm") ?? d.TieDiameterMm,
            TieSpacingMm = p.Value<double?>("tieSpacingMm") ?? d.TieSpacingMm,
            TieEnabled = p.Value<bool?>("tieEnabled") ?? d.TieEnabled,
            CoverTopBottomMm = p.Value<double?>("coverTopBottomMm") ?? d.CoverTopBottomMm,
            CoverLeftRightMm = p.Value<double?>("coverLeftRightMm") ?? d.CoverLeftRightMm,
            CoverStartEndMm = p.Value<double?>("coverStartEndMm") ?? d.CoverStartEndMm,
            TopHookType = p.Value<string?>("topHookType") ?? d.TopHookType,
            TopHookLengthMm = p.Value<double?>("topHookLengthMm") ?? d.TopHookLengthMm,
            BottomHookType = p.Value<string?>("bottomHookType") ?? d.BottomHookType,
            BottomHookLengthMm = p.Value<double?>("bottomHookLengthMm") ?? d.BottomHookLengthMm,
            TopOffsetMm = p.Value<double?>("topOffsetMm") ?? d.TopOffsetMm,
            BottomOffsetMm = p.Value<double?>("bottomOffsetMm") ?? d.BottomOffsetMm,
            HorizontalOffsetStartMm = p.Value<double?>("horizontalOffsetStartMm") ?? d.HorizontalOffsetStartMm,
            HorizontalOffsetEndMm = p.Value<double?>("horizontalOffsetEndMm") ?? d.HorizontalOffsetEndMm,
            DrawAdditionalRebar = p.Value<bool?>("drawAdditionalRebar") ?? d.DrawAdditionalRebar
        };
    }
}
