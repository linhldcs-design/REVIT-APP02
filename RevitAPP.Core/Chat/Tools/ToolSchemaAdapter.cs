using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Tools;

public static class ToolSchemaAdapter
{
    public static JObject ToAnthropic(ToolSchema tool) => new()
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["input_schema"] = tool.ParametersJsonSchema.DeepClone()
    };

    public static JObject ToOpenAi(ToolSchema tool) => new()
    {
        ["type"] = "function",
        ["function"] = new JObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = tool.ParametersJsonSchema.DeepClone()
        }
    };

    public static JArray ToGemini(IEnumerable<ToolSchema> tools)
    {
        var declarations = new JArray(tools.Select(tool => new JObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = tool.ParametersJsonSchema.DeepClone()
        }));
        return new JArray { new JObject { ["functionDeclarations"] = declarations } };
    }
}
