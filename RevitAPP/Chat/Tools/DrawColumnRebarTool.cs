using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Core.Models;
using RevitAPP.Services.ColumnRebar;
using ElementIdCompat = RevitAPP.Chat.Tools.ChatElementIdCompat;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Tool vẽ thép cột. Gọi thẳng <see cref="ColumnRebarApi.DrawForColumns"/> — engine yêu cầu caller đang
///     mở Transaction nên <see cref="RequiresTransaction"/> = true (bridge phase 4 mở transaction).
/// </summary>
public sealed class DrawColumnRebarTool : IChatTool
{
    public string Name => "draw_column_rebar";
    public bool RequiresTransaction => true;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(
        Name,
        "Vẽ thép cột (thép chủ + đai, nối so le, tùy chọn thép chờ móng/móc đỉnh) cho các cột chỉ định. " +
        "Đơn vị mm. Bỏ trống columnIds để dùng các cột đang chọn trong Revit.",
        new JsonSchemaBuilder()
            .IntegerArray("columnIds", "Danh sách ElementId cột; bỏ trống để dùng selection hiện tại.")
            .Number("mainBarDiameterMm", "Đường kính thép chủ (mm).")
            .Number("stirrupDiameterMm", "Đường kính đai (mm).")
            .Integer("barsX", "Số thanh thép chủ theo phương X.")
            .Integer("barsY", "Số thanh thép chủ theo phương Y.")
            .Number("coverMm", "Lớp bảo vệ (mm).")
            .Number("lapFactor", "Hệ số chiều dài nối.")
            .Bool("staggerLap", "Nối so le 50/50.")
            .Enum("lapPosition", "Vị trí nối.", new[] { "end", "middle" })
            .Bool("crankAtLap", "Uốn tại vị trí nối.")
            .Bool("useDistributionBar", "Dùng thép phân bố.")
            .Number("distributionBarDiameterMm", "Đường kính thép phân bố (mm).")
            .Number("stirrupSpacingEndMm", "Khoảng cách đai vùng đầu (mm).")
            .Number("stirrupSpacingMidMm", "Khoảng cách đai vùng thân (mm).")
            .Number("confineZoneLenMm", "Chiều dài vùng gia cường l0 (mm).")
            .Text("stirrupSectionType", "Kiểu đai mặt cắt.")
            .Bool("addPartition", "Thêm vách phân chia.")
            .Bool("topHookBending", "Bẻ móc đỉnh cột tầng trên cùng.")
            .Number("topHookLengthMm", "Chiều dài móc đỉnh (mm).")
            .Bool("foundationStarter", "Vẽ thép chờ móng (starter) bẻ chữ L.")
            .Number("foundationHmMm", "Chiều cao Hm thép chờ (mm).")
            .Number("foundationLbMm", "Chiều dài Lb thép chờ (mm).")
            .Text("foundationDirection", "Hướng bẻ thép chờ.")
            .Bool("foundationSplitBothSides", "Chẽ thép chờ về 2 nhánh.")
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var idTokens = input["columnIds"] as JArray
                       ?? throw new ArgumentException("Thiếu 'columnIds' (mảng id cột).");
        var columnIds = idTokens.Select(t => ElementIdCompat.Create(t.Value<long>())).ToList();
        if (columnIds.Count == 0) throw new ArgumentException("'columnIds' rỗng.");

        var result = ColumnRebarApi.DrawForColumns(ctx.Doc, columnIds, BuildOptions(input));
        var message = $"Đã tạo {result.MainBarCount} thanh thép chủ, {result.StirrupSetCount} bộ đai, " +
                      $"{result.StarterBarCount} thép chờ cho {columnIds.Count} cột.";
        if (result.Warnings.Count > 0) message += " | " + string.Join("; ", result.Warnings);
        return new { success = true, message };
    }

    private static ColumnRebarApiOptions BuildOptions(JObject p)
    {
        var d = new ColumnRebarApiOptions();
        return d with
        {
            MainBarDiameterMm = p.Value<double?>("mainBarDiameterMm") ?? d.MainBarDiameterMm,
            StirrupDiameterMm = p.Value<double?>("stirrupDiameterMm") ?? d.StirrupDiameterMm,
            BarsX = p.Value<int?>("barsX") ?? d.BarsX,
            BarsY = p.Value<int?>("barsY") ?? d.BarsY,
            CoverMm = p.Value<double?>("coverMm") ?? d.CoverMm,
            LapFactor = p.Value<double?>("lapFactor") ?? d.LapFactor,
            StaggerLap = p.Value<bool?>("staggerLap") ?? d.StaggerLap,
            LapPosition = (p.Value<string?>("lapPosition")?.ToLowerInvariant() == "middle")
                ? LapPosition.Middle
                : d.LapPosition,
            CrankAtLap = p.Value<bool?>("crankAtLap") ?? d.CrankAtLap,
            UseDistributionBar = p.Value<bool?>("useDistributionBar") ?? d.UseDistributionBar,
            DistributionBarDiameterMm = p.Value<double?>("distributionBarDiameterMm") ?? d.DistributionBarDiameterMm,
            SpacingEndMm = p.Value<double?>("stirrupSpacingEndMm") ?? d.SpacingEndMm,
            SpacingMidMm = p.Value<double?>("stirrupSpacingMidMm") ?? d.SpacingMidMm,
            ConfineZoneLenMm = p.Value<double?>("confineZoneLenMm") ?? d.ConfineZoneLenMm,
            StirrupSectionType = ParseStirrupType(p.Value<string?>("stirrupSectionType"), d.StirrupSectionType),
            AddPartition = p.Value<bool?>("addPartition") ?? d.AddPartition,
            TopHookBending = p.Value<bool?>("topHookBending") ?? d.TopHookBending,
            TopHookLengthMm = p.Value<double?>("topHookLengthMm") ?? d.TopHookLengthMm,
            FoundationStarter = p.Value<bool?>("foundationStarter") ?? d.FoundationStarter,
            FoundationHmMm = p.Value<double?>("foundationHmMm") ?? d.FoundationHmMm,
            FoundationLbMm = p.Value<double?>("foundationLbMm") ?? d.FoundationLbMm,
            FoundationDirection = ParseBendDirection(p.Value<string?>("foundationDirection"), d.FoundationDirection),
            FoundationSplitBothSides = p.Value<bool?>("foundationSplitBothSides") ?? d.FoundationSplitBothSides
        };
    }

    private static SectionStirrupType ParseStirrupType(string? value, SectionStirrupType fallback) =>
        Enum.TryParse<SectionStirrupType>(value, true, out var parsed) ? parsed : fallback;

    private static StarterBendDirection ParseBendDirection(string? value, StarterBendDirection fallback) =>
        Enum.TryParse<StarterBendDirection>(value, true, out var parsed) ? parsed : fallback;
}
