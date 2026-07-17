using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Tools;

namespace RevitAPP.Chat.Services;

public sealed record WireToolCall(string? Id, string Name, JObject Arguments, bool ArgumentsValid = true);

/// <summary>Pure request/response mapping for the supported LLM providers.</summary>
public static class ChatWireProtocol
{
    public static JObject BuildAnthropicRequest(
        string model, int maxTokens, string systemPrompt, JArray messages, IReadOnlyList<ToolSchema> tools)
    {
        var body = new JObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["system"] = systemPrompt,
            ["messages"] = messages
        };
        if (tools.Count > 0)
            body["tools"] = new JArray(tools.Select(ToolSchemaAdapter.ToAnthropic));
        return body;
    }

    public static IReadOnlyList<WireToolCall> ParseAnthropicToolCalls(JObject response) =>
        (response["content"] as JArray ?? new JArray())
        .Where(block => (string?)block["type"] == "tool_use")
        .Select(block => new WireToolCall(
            (string?)block["id"],
            (string?)block["name"] ?? string.Empty,
            block["input"] as JObject ?? new JObject()))
        .ToList();

    public static JObject BuildOpenAiRequest(
        string model, string systemPrompt, JArray messages, IReadOnlyList<ToolSchema> tools)
    {
        var wireMessages = new JArray { new JObject { ["role"] = "system", ["content"] = systemPrompt } };
        foreach (var message in messages) wireMessages.Add(message.DeepClone());
        var body = new JObject { ["model"] = model, ["messages"] = wireMessages };
        if (tools.Count > 0)
        {
            body["tools"] = new JArray(tools.Select(ToolSchemaAdapter.ToOpenAi));
            body["tool_choice"] = "auto";
        }
        return body;
    }

    public static IReadOnlyList<JObject> ToOpenAiMessages(ChatMessage message)
    {
        var results = message.Content.Where(block => block.Kind == ContentKind.ToolResult).ToList();
        if (results.Count > 0)
        {
            return results.Select(result => new JObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = result.CallId ?? string.Empty,
                ["content"] = result.ResultJson ?? string.Empty
            }).ToList();
        }

        var wire = new JObject { ["role"] = message.Role };
        var text = message.PlainText();
        wire["content"] = string.IsNullOrEmpty(text) ? null : text;
        var calls = message.Content.Where(block => block.Kind == ContentKind.ToolCall).ToList();
        if (calls.Count > 0)
        {
            wire["tool_calls"] = new JArray(calls.Select(call => new JObject
            {
                ["id"] = call.CallId ?? string.Empty,
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = call.ToolName ?? string.Empty,
                    ["arguments"] = (call.Arguments ?? new JObject()).ToString()
                }
            }));
        }
        return new[] { wire };
    }

    public static IReadOnlyList<WireToolCall> ParseOpenAiToolCalls(JObject response)
    {
        var calls = response["choices"]?[0]?["message"]?["tool_calls"] as JArray ?? new JArray();
        return calls.Select(call =>
        {
            var function = call["function"] as JObject ?? new JObject();
            var arguments = ParseArguments((string?)function["arguments"]);
            return new WireToolCall(
                (string?)call["id"],
                (string?)function["name"] ?? string.Empty,
                arguments.Value,
                arguments.IsValid);
        }).ToList();
    }

    public static JObject BuildGeminiRequest(
        string systemPrompt, JArray contents, IReadOnlyList<ToolSchema> tools)
    {
        var body = new JObject
        {
            ["contents"] = contents,
            ["systemInstruction"] = new JObject
            {
                ["parts"] = new JArray { new JObject { ["text"] = systemPrompt } }
            }
        };
        if (tools.Count > 0) body["tools"] = ToolSchemaAdapter.ToGemini(tools);
        return body;
    }

    public static IReadOnlyList<WireToolCall> ParseGeminiToolCalls(JObject response)
    {
        var parts = response["candidates"]?[0]?["content"]?["parts"] as JArray ?? new JArray();
        return parts.Where(part => part["functionCall"] != null).Select(part =>
        {
            var call = part["functionCall"] as JObject ?? new JObject();
            return new WireToolCall(
                null,
                (string?)call["name"] ?? string.Empty,
                call["args"] as JObject ?? new JObject());
        }).ToList();
    }

    private static (JObject Value, bool IsValid) ParseArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (new JObject(), true);
        try { return (JObject.Parse(json!), true); }
        catch { return (new JObject(), false); }
    }
}
