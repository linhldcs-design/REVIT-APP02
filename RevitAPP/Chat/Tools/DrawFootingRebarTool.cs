using Newtonsoft.Json.Linq;
using IsolatedFootingRebar.Services;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Tool vẽ thép móng đơn. <see cref="FootingRebarApi.DrawForFooting"/> tự mở transaction → RequiresTransaction=false.
/// </summary>
public sealed class DrawFootingRebarTool : IChatTool
{
    public string Name => "draw_footing_rebar";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(
        Name,
        "Vẽ thép cho móng đơn (lưới đáy + trên tùy chọn). Đơn vị mm. Bỏ trống footingIds để dùng các móng đang chọn trong Revit.",
        new JsonSchemaBuilder()
            .IntegerArray("footingIds", "Danh sách ElementId móng; bỏ trống để dùng selection hiện tại.")
            .Text("presetName", "Tên preset móng đã lưu (ví dụ V1).")
            .Integer("meshDiameterMm", "Đường kính thép lưới (mm).")
            .Number("bottomSpacingMm", "Khoảng cách lưới đáy (mm).")
            .Number("topSpacingMm", "Khoảng cách lưới trên (mm).")
            .Number("bottomHookMm", "Móc lưới đáy (mm).")
            .Number("topHookMm", "Móc lưới trên (mm).")
            .Bool("drawTop", "Vẽ lưới trên.")
            .Number("bottomCoverMm", "Lớp bảo vệ đáy (mm).")
            .Number("topCoverMm", "Lớp bảo vệ trên (mm).")
            .Number("sideCoverMm", "Lớp bảo vệ cạnh (mm).")
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var footingIds = DrawWallRebarTool.ParseIds(input, "footingIds", "footingId", "móng");
        var presetName = input.Value<string?>("presetName")?.Trim();
        var actualName = string.IsNullOrWhiteSpace(presetName) ? null : FootingConfigStore.Names()
            .FirstOrDefault(n => string.Equals(n, presetName, StringComparison.OrdinalIgnoreCase));
        var preset = actualName is null ? null : FootingConfigStore.Load(actualName);
        if (!string.IsNullOrWhiteSpace(presetName) && preset is null)
            throw new ArgumentException($"Không tìm thấy preset móng '{presetName}'.");
        var options = preset is null ? BuildOptions(input) : null;

        int mesh = 0, vertical = 0, stirrup = 0, ok = 0;
        var warnings = new List<string>();
        foreach (var id in footingIds)
        {
            var result = preset is not null
                ? FootingRebarApi.DrawForFooting(ctx.Doc, id, preset)
                : FootingRebarApi.DrawForFooting(ctx.Doc, id, options);
            if (result.Succeeded) ok++;
            mesh += result.MeshCount;
            vertical += result.VerticalCount;
            stirrup += result.StirrupCount;
            warnings.AddRange(result.Warnings);
        }

        var message = $"Đã tạo {mesh} lưới, {vertical} thép kê, {stirrup} đai ngang cho {ok}/{footingIds.Count} móng.";
        if (warnings.Count > 0) message += " | " + string.Join("; ", warnings.Distinct());
        return new { success = ok > 0, message, presetName };
    }

    private static FootingRebarApiOptions BuildOptions(JObject p)
    {
        var d = new FootingRebarApiOptions();
        return d with
        {
            MeshDiameterMm = p.Value<int?>("meshDiameterMm") ?? d.MeshDiameterMm,
            BottomSpacingMm = p.Value<double?>("bottomSpacingMm") ?? d.BottomSpacingMm,
            TopSpacingMm = p.Value<double?>("topSpacingMm") ?? d.TopSpacingMm,
            BottomHookMm = p.Value<double?>("bottomHookMm") ?? d.BottomHookMm,
            TopHookMm = p.Value<double?>("topHookMm") ?? d.TopHookMm,
            DrawTop = p.Value<bool?>("drawTop") ?? d.DrawTop,
            BottomCoverMm = p.Value<double?>("bottomCoverMm") ?? d.BottomCoverMm,
            TopCoverMm = p.Value<double?>("topCoverMm") ?? d.TopCoverMm,
            SideCoverMm = p.Value<double?>("sideCoverMm") ?? d.SideCoverMm
        };
    }
}
