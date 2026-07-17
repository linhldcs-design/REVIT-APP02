using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Services;
using Xunit;

namespace RevitAPP.Tests.Chat;

public sealed class ChatWireProtocolTests
{
    private static readonly ToolSchema Tool = new(
        "draw_column_rebar", "Draw", new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["barsX"] = new JObject { ["type"] = "integer" } }
        });

    [Fact]
    public void Anthropic_BuildAndParse_UsesMessagesToolUseFormat()
    {
        var body = ChatWireProtocol.BuildAnthropicRequest("claude-test", 1024, "system", new JArray(), new[] { Tool });
        Assert.Equal("object", (string?)body["tools"]?[0]?["input_schema"]?["type"]);

        var calls = ChatWireProtocol.ParseAnthropicToolCalls(JObject.Parse(
            "{\"content\":[{\"type\":\"tool_use\",\"id\":\"tool-1\",\"name\":\"draw_column_rebar\",\"input\":{\"barsX\":4}}]}"));
        Assert.Equal("tool-1", calls.Single().Id);
        Assert.Equal(4, (int?)calls.Single().Arguments["barsX"]);
    }

    [Fact]
    public void OpenAi_BuildAndParse_UsesFunctionToolCallsFormat()
    {
        var body = ChatWireProtocol.BuildOpenAiRequest("gpt-test", "system", new JArray(), new[] { Tool });
        Assert.Equal("auto", (string?)body["tool_choice"]);
        Assert.Equal("object", (string?)body["tools"]?[0]?["function"]?["parameters"]?["type"]);

        var calls = ChatWireProtocol.ParseOpenAiToolCalls(JObject.Parse(
            "{\"choices\":[{\"message\":{\"tool_calls\":[{\"id\":\"call-1\",\"function\":{\"name\":\"draw_column_rebar\",\"arguments\":\"{\\\"barsX\\\":4}\"}}]}}]}"));
        Assert.Equal("call-1", calls.Single().Id);
        Assert.Equal(4, (int?)calls.Single().Arguments["barsX"]);
    }

    [Fact]
    public void OpenAi_ParseMalformedArguments_MarksCallInvalid()
    {
        var calls = ChatWireProtocol.ParseOpenAiToolCalls(JObject.Parse(
            "{\"choices\":[{\"message\":{\"tool_calls\":[{\"id\":\"call-1\",\"function\":{\"name\":\"draw_column_rebar\",\"arguments\":\"{bad json\"}}]}}]}"));

        Assert.False(calls.Single().ArgumentsValid);
    }

    [Fact]
    public void OpenAi_MultipleToolResults_EmitsOneMessagePerCall()
    {
        var message = new ChatMessage(ChatMessage.User, new[]
        {
            ContentBlock.ToolResult("call-1", "first", "{\"ok\":true}"),
            ContentBlock.ToolResult("call-2", "second", "{\"ok\":true}")
        });

        var wire = ChatWireProtocol.ToOpenAiMessages(message);

        Assert.Equal(2, wire.Count);
        Assert.Equal(new[] { "call-1", "call-2" }, wire.Select(item => (string?)item["tool_call_id"]));
    }

    [Fact]
    public void Gemini_BuildAndParse_UsesFunctionDeclarationsFormat()
    {
        var body = ChatWireProtocol.BuildGeminiRequest("system", new JArray(), new[] { Tool });
        Assert.Equal("draw_column_rebar", (string?)body["tools"]?[0]?["functionDeclarations"]?[0]?["name"]);

        var calls = ChatWireProtocol.ParseGeminiToolCalls(JObject.Parse(
            "{\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"draw_column_rebar\",\"args\":{\"barsX\":4}}}]}}]}"));
        Assert.Null(calls.Single().Id);
        Assert.Equal(4, (int?)calls.Single().Arguments["barsX"]);
    }

    [Fact]
    public void VisionImage_IsMappedForAllThreeProviders()
    {
        var message = ChatMessage.FromUser("Kiểm tra ảnh", new[]
        {
            ContentBlock.FromImage("image/png", "YWJj", "view.png")
        });

        var openAi = ChatWireProtocol.ToOpenAiMessages(message).Single();
        Assert.Equal("image_url", (string?)openAi["content"]?[1]?["type"]);
        Assert.Equal("data:image/png;base64,YWJj", (string?)openAi["content"]?[1]?["image_url"]?["url"]);

        var anthropic = ChatWireProtocol.ToAnthropicMessage(message);
        Assert.Equal("image", (string?)anthropic["content"]?[1]?["type"]);
        Assert.Equal("YWJj", (string?)anthropic["content"]?[1]?["source"]?["data"]);

        var gemini = ChatWireProtocol.ToGeminiContent(message);
        Assert.Equal("image/png", (string?)gemini["parts"]?[1]?["inlineData"]?["mimeType"]);
        Assert.Equal("YWJj", (string?)gemini["parts"]?[1]?["inlineData"]?["data"]);
    }
}
