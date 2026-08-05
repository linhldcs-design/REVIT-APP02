using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Mcp;
using RevitAPP.Chat.Services;
using RevitAPP.Chat.Tools;

namespace RevitAPP.Chat.Mcp;

/// <summary>Projects the Chat AI registry into MCP without duplicating tool schemas or execution logic.</summary>
public sealed class McpToolSurface
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        "get_selected_elements",
        "get_current_view_info",
        "get_viewport_text_notes",
        "get_open_excel_workbooks",
        "find_excel_files",
        "inspect_excel_file",
        "read_excel_table",
        "find_beam_longitudinal_presets",
        "get_available_family_types",
        "get_current_view_elements",
        "ai_element_filter",
        "export_room_data",
        "get_material_quantities",
        "analyze_model_statistics"
    };

    private readonly ChatToolRegistry _registry;
    private readonly ChatToolEventHandler _bridge;

    public McpToolSurface(ChatToolRegistry registry, ChatToolEventHandler bridge)
    {
        _registry = registry;
        _bridge = bridge;
        Tools = registry.Tools.Select(ToDescriptor).ToList();
    }

    public IReadOnlyList<McpToolDescriptor> Tools { get; }

    public Task<string> ExecuteAsync(string name, JObject input)
    {
        var tool = _registry.Get(name);
        var readOnly = ReadOnlyTools.Contains(name);
        if (tool is IBackgroundChatTool backgroundTool)
        {
            if (!readOnly || tool.RequiresLicense)
                return Task.FromResult(JsonConvert.SerializeObject(new
                {
                    success = false,
                    message = $"Background MCP tool '{name}' must be read-only and license-free."
                }));
            try
            {
                return Task.FromResult(JsonConvert.SerializeObject(backgroundTool.Execute(input, null!)));
            }
            catch (Exception exception)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new
                {
                    success = false,
                    message = exception.Message
                }));
            }
        }

        return Task.FromResult(_bridge.ExecuteToolOnRevitThread(
            name,
            input,
            requireUserConfirmation: !readOnly));
    }

    private static McpToolDescriptor ToDescriptor(IChatTool tool)
    {
        var readOnly = ReadOnlyTools.Contains(tool.Name);
        var confirmable = tool as IConfirmableChatTool;
        return new McpToolDescriptor(
            tool.Schema,
            readOnly,
            confirmable?.IsDangerous == true,
            readOnly,
            tool is IBackgroundChatTool);
    }
}
