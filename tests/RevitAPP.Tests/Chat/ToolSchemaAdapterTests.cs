using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Tools;
using Xunit;

namespace RevitAPP.Tests.Chat;

public sealed class ToolSchemaAdapterTests
{
    private static ToolSchema CreateSchema() => new(
        "draw_column_rebar",
        "Draw column reinforcement",
        new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["mainBarDiameterMm"] = new JObject { ["type"] = "number" },
                ["barsX"] = new JObject { ["type"] = "integer" },
                ["barsY"] = new JObject { ["type"] = "integer" },
                ["coverMm"] = new JObject { ["type"] = "number" },
                ["lapFactor"] = new JObject { ["type"] = "number" }
            },
            ["required"] = new JArray("mainBarDiameterMm", "barsX", "barsY")
        });

    [Fact]
    public void ToAnthropic_ValidSchema_UsesInputSchemaEnvelope()
    {
        var wire = ToolSchemaAdapter.ToAnthropic(CreateSchema());
        Assert.Equal("draw_column_rebar", (string?)wire["name"]);
        Assert.Equal("object", (string?)wire["input_schema"]?["type"]);
    }

    [Fact]
    public void ToOpenAi_ValidSchema_UsesFunctionParametersEnvelope()
    {
        var wire = ToolSchemaAdapter.ToOpenAi(CreateSchema());
        Assert.Equal("function", (string?)wire["type"]);
        Assert.Contains("barsX", wire["function"]?["parameters"]?["required"]!.Values<string>()!);
    }

    [Fact]
    public void ToGemini_ValidSchema_UsesFunctionDeclarationsEnvelope()
    {
        var wire = ToolSchemaAdapter.ToGemini(new[] { CreateSchema() });
        var declaration = wire[0]?["functionDeclarations"]?[0];
        Assert.Equal("draw_column_rebar", (string?)declaration?["name"]);
        Assert.Equal("object", (string?)declaration?["parameters"]?["type"]);
    }

    [Fact]
    public void ToGemini_RemovesUnsupportedAdditionalPropertiesRecursively()
    {
        var schema = new ToolSchema(
            "operate_element",
            "Operate on an element",
            new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = true
                    }
                }
            });

        var wire = ToolSchemaAdapter.ToGemini(new[] { schema });
        var parameters = wire[0]?["functionDeclarations"]?[0]?["parameters"];

        Assert.Empty(parameters!.SelectTokens("$..additionalProperties"));
        Assert.NotNull(schema.ParametersJsonSchema["additionalProperties"]);
        Assert.NotNull(schema.ParametersJsonSchema["properties"]?["data"]?["additionalProperties"]);
    }

    [Fact]
    public void NeutralSchema_ColumnTool_ContainsBuildOptionKeys()
    {
        var properties = (JObject)CreateSchema().ParametersJsonSchema["properties"]!;
        foreach (var key in new[] { "mainBarDiameterMm", "barsX", "barsY", "coverMm", "lapFactor" })
            Assert.True(properties.ContainsKey(key), $"Missing schema property: {key}");
    }
}
