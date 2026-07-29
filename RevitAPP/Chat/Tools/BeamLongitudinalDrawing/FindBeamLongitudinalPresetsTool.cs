using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Core.Chat.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;

namespace RevitAPP.Chat.Tools.BeamLongitudinalDrawing;

public sealed class FindBeamLongitudinalPresetsTool : IChatTool
{
    public string Name => "find_beam_longitudinal_presets";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;

    public ToolSchema Schema => new(Name,
        "Tìm cấu hình Mặt Cắt Dọc Dầm đã lưu. Tool chỉ đọc và không thay đổi mô hình.",
        new JsonSchemaBuilder()
            .Text("query", "Tên hoặc một phần tên cấu hình; để trống để liệt kê tất cả.")
            .Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var query = input.Value<string?>("query");
        var values = LongitudinalDrawingPresetFinder.Find(
            new LongitudinalDrawingPresetStore().Load(), query);
        return new
        {
            success = true,
            count = values.Count,
            presets = values.Select(value => new
            {
                name = value.SettingName,
                value.Scale,
                value.SheetNumber,
                value.ViewTemplateName,
                value.CrossViewTemplateName
            }).ToList()
        };
    }
}
