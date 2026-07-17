using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Tools;

namespace RevitAPP.Chat.Services;

/// <summary>
///     Client cho OpenAI Chat Completions API (/v1/chat/completions). Tool ra dạng tool_calls (arguments là
///     chuỗi JSON phải parse), kết quả trả lại bằng message role:"tool".
/// </summary>
public sealed class OpenAiClient : LlmClientBase, ILlmClient
{
    private const string DefaultBaseUrl = "https://api.openai.com";

    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiClient(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
    }

    protected override string ProviderName => "OpenAI";

    public async Task<string> SendAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolSchema> tools,
        ToolExecutor executor,
        CancellationToken ct)
    {
        var messages = new JArray();
        foreach (var message in history)
        foreach (var wireMessage in ChatWireProtocol.ToOpenAiMessages(message))
            messages.Add(wireMessage);

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var body = ChatWireProtocol.BuildOpenAiRequest(_model, ChatSystemPrompt.Text, messages, tools);

            var response = await PostJsonAsync(
                $"{DefaultBaseUrl}/v1/chat/completions",
                request => request.Headers.Add("Authorization", $"Bearer {_apiKey}"),
                body,
                ct).ConfigureAwait(false);

            var assistantMessage = response["choices"]?[0]?["message"] as JObject ?? new JObject();
            messages.Add(assistantMessage);

            var toolCalls = ChatWireProtocol.ParseOpenAiToolCalls(response);
            if (toolCalls.Count == 0)
                return (string?)assistantMessage["content"] ?? string.Empty;

            foreach (var toolCall in toolCalls)
            {
                var result = toolCall.ArgumentsValid
                    ? await executor(toolCall.Name, toolCall.Arguments, ct).ConfigureAwait(false)
                    : "{\"success\":false,\"message\":\"Tham số tool không hợp lệ (JSON lỗi).\"}";

                messages.Add(new JObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = toolCall.Id ?? string.Empty,
                    ["content"] = result
                });
            }
        }

        return "Đã đạt giới hạn số bước gọi công cụ. Vui lòng thử lại với yêu cầu cụ thể hơn.";
    }

}
