using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Ngữ cảnh Revit cấp cho tool khi execute (trên UI thread qua bridge phase 4).
/// </summary>
public sealed record ChatToolContext(Document Doc, UIDocument UiDoc);

/// <summary>
///     1 tool mà LLM có thể gọi. Registry gom schema (gửi cho LLM) và dispatch execute.
///     <see cref="RequiresTransaction"/>: engine cần caller mở Transaction (chỉ đúng với vẽ thép cột).
///     <see cref="RequiresLicense"/>: gate license trước khi chạy (các tool ghi model).
/// </summary>
public interface IChatTool
{
    string Name { get; }
    ToolSchema Schema { get; }
    bool RequiresTransaction { get; }
    bool RequiresLicense { get; }

    object Execute(JObject input, ChatToolContext ctx);
}

/// <summary>Tool thay đổi model hoặc chạy mã tùy ý cần xác nhận trước khi marshal vào Revit.</summary>
public interface IConfirmableChatTool
{
    bool RequiresConfirmation { get; }
    bool IsDangerous { get; }
}
