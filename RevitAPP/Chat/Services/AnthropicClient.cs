using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Tools;

namespace RevitAPP.Chat.Services;

/// <summary>
///     Client cho Anthropic Messages API (/v1/messages). Tool ra dạng content block tool_use, kết quả trả
///     lại bằng user message chứa block tool_result.
/// </summary>
public sealed class AnthropicClient : LlmClientBase, ILlmClient
{
    private const string DefaultBaseUrl = "https://api.anthropic.com";
    private const string ApiVersion = "2023-06-01";

    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxTokens;

    public AnthropicClient(string apiKey, string model, int maxTokens)
    {
        _apiKey = apiKey;
        _model = model;
        _maxTokens = maxTokens;
    }

    protected override string ProviderName => "Claude";

    public async Task<string> SendAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolSchema> tools,
        ToolExecutor executor,
        CancellationToken ct)
    {
        var messages = new JArray(history.Select(ToWireMessage));
        for (var round = 0; round < MaxToolRounds; round++)
        {
            var body = ChatWireProtocol.BuildAnthropicRequest(
                _model, _maxTokens, ChatSystemPrompt.Text, messages, tools);

            var response = await PostJsonAsync(
                $"{DefaultBaseUrl}/v1/messages",
                request =>
                {
                    request.Headers.Add("x-api-key", _apiKey);
                    request.Headers.Add("anthropic-version", ApiVersion);
                },
                body,
                ct).ConfigureAwait(false);

            var content = response["content"] as JArray ?? new JArray();
            var toolUses = ChatWireProtocol.ParseAnthropicToolCalls(response);

            // Append nguyên assistant message (giữ tool_use để pair với tool_result).
            messages.Add(new JObject { ["role"] = "assistant", ["content"] = content });

            if (toolUses.Count == 0)
                return ExtractText(content);

            var resultBlocks = new JArray();
            foreach (var toolUse in toolUses)
            {
                var result = await executor(toolUse.Name, toolUse.Arguments, ct).ConfigureAwait(false);

                resultBlocks.Add(new JObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = toolUse.Id ?? string.Empty,
                    ["content"] = result
                });
            }

            messages.Add(new JObject { ["role"] = "user", ["content"] = resultBlocks });
        }

        return "Đã đạt giới hạn số bước gọi công cụ. Vui lòng thử lại với yêu cầu cụ thể hơn.";
    }

    private static string ExtractText(JArray content)
    {
        var parts = content
            .Where(block => (string?)block["type"] == "text")
            .Select(block => (string?)block["text"])
            .Where(text => !string.IsNullOrEmpty(text));
        return string.Join("\n", parts);
    }

    private static JObject ToWireMessage(ChatMessage message)
    {
        var blocks = new JArray();
        foreach (var block in message.Content)
        {
            switch (block.Kind)
            {
                case ContentKind.Text:
                    blocks.Add(new JObject { ["type"] = "text", ["text"] = block.Text ?? string.Empty });
                    break;
                case ContentKind.ToolResult:
                    blocks.Add(new JObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = block.CallId ?? string.Empty,
                        ["content"] = block.ResultJson ?? string.Empty
                    });
                    break;
                case ContentKind.ToolCall:
                    blocks.Add(new JObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = block.CallId ?? string.Empty,
                        ["name"] = block.ToolName ?? string.Empty,
                        ["input"] = block.Arguments ?? new JObject()
                    });
                    break;
            }
        }

        return new JObject { ["role"] = message.Role, ["content"] = blocks };
    }
}
