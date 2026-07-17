using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools;

/// <summary>Schema adapter. ChatViewModel dispatches it directly to the in-process Revit command host.</summary>
public sealed class RevitMcpProxyTool : IChatTool
{
    public RevitMcpProxyTool(string name, string mcpMethod, string description, JObject? schema = null,
        bool requiresConfirmation = false, bool isDangerous = false)
    {
        Name = name;
        McpMethod = mcpMethod;
        RequiresConfirmation = requiresConfirmation;
        IsDangerous = isDangerous;
        Schema = new ToolSchema(name, description, schema ?? OpenObjectSchema());
    }

    public string Name { get; }
    public string McpMethod { get; }
    public bool RequiresConfirmation { get; }
    public bool IsDangerous { get; }
    public ToolSchema Schema { get; }
    public bool RequiresTransaction => false;
    public bool RequiresLicense => false;

    public object Execute(JObject input, ChatToolContext ctx) =>
        throw new NotSupportedException("Native Revit commands are dispatched by ChatViewModel.");

    private static JObject OpenObjectSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JObject(),
        ["additionalProperties"] = true
    };
}
