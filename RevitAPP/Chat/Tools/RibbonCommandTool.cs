using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools;

/// <summary>Exposes a RevitAPP ribbon button command through the existing Chat ExternalEvent bridge.</summary>
public sealed class RibbonCommandTool : IChatTool
{
    private readonly Action _execute;

    public RibbonCommandTool(string name, string description, Action execute, bool requiresLicense = true)
    {
        Name = name;
        Schema = new ToolSchema(name, description, new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject()
        });
        _execute = execute;
        RequiresLicense = requiresLicense;
    }

    public string Name { get; }
    public ToolSchema Schema { get; }
    public bool RequiresTransaction => false;
    public bool RequiresLicense { get; }

    public object Execute(JObject input, ChatToolContext ctx)
    {
        _execute();
        return new { success = true, message = $"Đã chạy nút RevitAPP: {Name}." };
    }
}
