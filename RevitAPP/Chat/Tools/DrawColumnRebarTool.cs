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
        "Đơn vị mm. Có thể truyền columnMark để tự dò toàn bộ Structural Column theo Instance Mark trong dự án, " +
        "hoặc truyền columnIds; bỏ trống cả hai để dùng các cột đang chọn trong Revit. " +
        "Khi người dùng nhắc cấu hình/preset add-in đã lưu, phải truyền presetName để dùng nguyên cấu hình theo từng tầng.",
        new JsonSchemaBuilder()
            .IntegerArray("columnIds", "Danh sách ElementId cột; bỏ trống để dùng selection hiện tại.")
            .Text("columnMark", "Instance Mark của hệ cột cần tự dò trong toàn dự án, ví dụ C7. Không phải Type Mark.")
            .Text("presetName", "Tên cấu hình thép cột đã lưu trong add-in, ví dụ C1. Khi có giá trị này, dùng nguyên preset thay cho các thông số rời.")
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
        var requestedMark = input.Value<string?>("columnMark")?.Trim();
        var columnIds = !string.IsNullOrWhiteSpace(requestedMark)
            ? FindColumnsByInstanceMark(ctx.Doc, requestedMark)
            : ParseColumnIds(input);
        if (columnIds.Count == 0)
            throw new ArgumentException(!string.IsNullOrWhiteSpace(requestedMark)
                ? $"Không tìm thấy Structural Column nào có Instance Mark = '{requestedMark}' trong dự án."
                : "Không có cột hợp lệ trong 'columnIds' hoặc selection hiện tại.");

        // Với câu lệnh "vẽ hệ cột C7", mặc định cấu hình đã lưu cũng là C7.
        // presetName tường minh vẫn được ưu tiên khi user muốn dùng cấu hình khác Mark.
        var requestedPreset = input.Value<string?>("presetName")?.Trim();
        if (string.IsNullOrWhiteSpace(requestedPreset) && !string.IsNullOrWhiteSpace(requestedMark))
            requestedPreset = requestedMark;
        var preset = string.IsNullOrWhiteSpace(requestedPreset)
            ? null
            : new ColumnRebarConfigStore().LoadAll(ctx.Doc)
                .FirstOrDefault(item => string.Equals(item.Name, requestedPreset, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(requestedPreset) && preset is null)
            throw new ArgumentException($"Không tìm thấy cấu hình thép cột '{requestedPreset}' đã lưu trong dự án.");

        var result = preset is null
            ? ColumnRebarApi.DrawForColumns(ctx.Doc, columnIds, BuildOptions(input))
            : ColumnRebarApi.DrawForColumns(ctx.Doc, columnIds, preset);
        var message = $"Đã tạo {result.MainBarCount} thanh thép chủ, {result.StirrupSetCount} bộ đai, " +
                      $"{result.StarterBarCount} thép chờ cho {columnIds.Count} cột.";
        if (!string.IsNullOrWhiteSpace(requestedMark)) message += $" Instance Mark: {requestedMark}.";
        if (preset is not null) message += $" Đã dùng cấu hình đã lưu '{preset.Name}'.";
        if (result.Warnings.Count > 0) message += " | " + string.Join("; ", result.Warnings);
        var succeeded = result.MainBarCount + result.StirrupSetCount + result.StarterBarCount > 0;
        return new { success = succeeded, columnMark = requestedMark, presetName = preset?.Name, columnCount = columnIds.Count, message };
    }

    private static List<ElementId> ParseColumnIds(JObject input) =>
        (input["columnIds"] as JArray)?.Select(token => ElementIdCompat.Create(token.Value<long>())).ToList()
        ?? new List<ElementId>();

    private static List<ElementId> FindColumnsByInstanceMark(Document document, string mark) =>
        new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Where(element => string.Equals(
                element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()?.Trim(),
                mark,
                StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Id)
            .ToList();

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
