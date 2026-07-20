using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools.Native;

public sealed class FocusChatTool : IChatTool
{
    public string Name => "focus_chat_ai";
    public ToolSchema Schema => new(Name, "Cửa sổ Chat AI hiện tại đã được mở và đưa lên trước.",
        new JObject { ["type"] = "object", ["properties"] = new JObject() });
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;

    public object Execute(JObject input, ChatToolContext context) => new
    {
        success = true,
        message = "Cửa sổ Chat AI hiện đã được mở và ở phía trước."
    };
}
