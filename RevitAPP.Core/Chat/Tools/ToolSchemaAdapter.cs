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
            ["parameters"] = ToGeminiSchema(tool.ParametersJsonSchema)
        }));
        return new JArray { new JObject { ["functionDeclarations"] = declarations } };
    }

    private static JToken ToGeminiSchema(JToken schema)
    {
        var clone = schema.DeepClone();
        foreach (var value in clone.SelectTokens("$..additionalProperties").ToList())
            value.Parent?.Remove();
        return clone;
    }
}
