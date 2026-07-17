using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Đọc thông tin view đang mở (tên, loại, tỉ lệ, có phải sheet). Read-only — không cần license/transaction.
/// </summary>
public sealed class GetCurrentViewInfoTool : IChatTool
{
    public string Name => "get_current_view_info";
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;

    public ToolSchema Schema => new(
        Name,
        "Lấy thông tin view Revit đang mở: tên, loại view, tỉ lệ, có phải sheet không.",
        new JsonSchemaBuilder().Build());

    public object Execute(JObject input, ChatToolContext ctx)
    {
        var view = ctx.Doc.ActiveView;
        return new
        {
            success = true,
            viewName = view?.Name ?? string.Empty,
            viewType = view?.ViewType.ToString() ?? string.Empty,
            scale = view?.Scale ?? 0,
            isSheet = view is ViewSheet
        };
    }
}
