using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Services.FootingDrawing;

namespace RevitAPP.Chat.Tools;

/// <summary>Sắp các cặp mặt bằng/mặt cắt móng bằng đúng viewport ids do hai tool tạo bản vẽ trả về.</summary>
public sealed class ArrangeFootingSheetTool : IChatTool
{
    public string Name => "arrange_footing_sheet";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => true;

    public ToolSchema Schema => new(Name,
        "Sắp viewport móng trên một sheet: mặt bằng ở hàng trên, mặt cắt đúng cặp ở hàng dưới, các cặp đi từ trái sang phải. Chỉ dùng viewportId thực tế do draw_footing_drawing và draw_footing_section trả về.",
        new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["sheetId"] = new JObject { ["type"] = "integer" },
                ["pairs"] = new JObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["items"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["mark"] = new JObject { ["type"] = "string" },
                            ["planFootingId"] = new JObject { ["type"] = "integer" },
                            ["sectionFootingId"] = new JObject { ["type"] = "integer" },
                            ["planViewportId"] = new JObject { ["type"] = "integer" },
                            ["sectionViewportId"] = new JObject { ["type"] = "integer" }
                        },
                        ["required"] = new JArray("planFootingId", "sectionFootingId", "planViewportId", "sectionViewportId")
                    }
                },
                ["titleBlockReserveRatio"] = new JObject { ["type"] = "number", ["minimum"] = 0, ["exclusiveMaximum"] = 0.5, ["default"] = 0.22 },
                ["gapMm"] = new JObject { ["type"] = "number", ["minimum"] = 0, ["default"] = 20 }
            },
            ["required"] = new JArray("sheetId", "pairs")
        });

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var sheetId = input.Value<long?>("sheetId") ?? throw new ArgumentException("Thiếu sheetId.");
        var pairTokens = input["pairs"] as JArray ?? throw new ArgumentException("Thiếu pairs.");
        var pairs = pairTokens.OfType<JObject>().Select(value => new FootingViewportPair(
            value.Value<string?>("mark")?.Trim() ?? string.Empty,
            value.Value<long?>("planFootingId") ?? throw new ArgumentException("Thiếu planFootingId."),
            value.Value<long?>("sectionFootingId") ?? throw new ArgumentException("Thiếu sectionFootingId."),
            value.Value<long?>("planViewportId") ?? throw new ArgumentException("Thiếu planViewportId."),
            value.Value<long?>("sectionViewportId") ?? throw new ArgumentException("Thiếu sectionViewportId."))).ToList();
        var reserve = input.Value<double?>("titleBlockReserveRatio") ?? 0.22;
        var gap = input.Value<double?>("gapMm") ?? 20;

        new FootingSheetLayoutService().Arrange(ctx.Doc, sheetId, pairs, reserve, gap);
        return new
        {
            success = true,
            message = $"Đã sắp {pairs.Count} cặp móng: mặt bằng trên, mặt cắt dưới.",
            sheetId,
            arranged = pairs.Count
        };
    }
}
