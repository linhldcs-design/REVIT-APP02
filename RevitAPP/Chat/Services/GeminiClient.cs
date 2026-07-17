using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Tools;

namespace RevitAPP.Chat.Services;

/// <summary>
///     Client cho Google Gemini API (:generateContent). Tool khai báo qua functionDeclarations, model trả
///     functionCall, kết quả trả lại bằng functionResponse. Role user/model; system → systemInstruction.
///     API key gửi qua header x-goog-api-key (KHÔNG dùng ?key= để tránh lộ key trong URL/log).
/// </summary>
public sealed class GeminiClient : LlmClientBase, ILlmClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    private readonly string _apiKey;
    private readonly string _model;

    public GeminiClient(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
    }

    protected override string ProviderName => "Gemini";

    public async Task<string> SendAsync(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolSchema> tools,
        ToolExecutor executor,
        CancellationToken ct)
    {
        var contents = new JArray(history.Select(ToWireContent));
        var url = $"{BaseUrl}/models/{Uri.EscapeDataString(_model)}:generateContent";

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var body = ChatWireProtocol.BuildGeminiRequest(ChatSystemPrompt.Text, contents, tools);

            var response = await PostJsonAsync(
                url,
                request => request.Headers.Add("x-goog-api-key", _apiKey),
                body,
                ct).ConfigureAwait(false);

            var parts = response["candidates"]?[0]?["content"]?["parts"] as JArray ?? new JArray();
            var functionCalls = ChatWireProtocol.ParseGeminiToolCalls(response);

            // Append nguyên model turn (giữ functionCall để pair với functionResponse).
            contents.Add(new JObject { ["role"] = "model", ["parts"] = parts });

            if (functionCalls.Count == 0)
                return ExtractText(parts);

            var responseParts = new JArray();
            foreach (var part in functionCalls)
            {
                var result = await executor(part.Name, part.Arguments, ct).ConfigureAwait(false);

                responseParts.Add(new JObject
                {
                    ["functionResponse"] = new JObject
                    {
                        ["name"] = part.Name,
                        ["response"] = WrapResult(result)
                    }
                });
            }

            contents.Add(new JObject { ["role"] = "user", ["parts"] = responseParts });
        }

        return "Đã đạt giới hạn số bước gọi công cụ. Vui lòng thử lại với yêu cầu cụ thể hơn.";
    }

    private static JObject WrapResult(string resultJson)
    {
        // Gemini yêu cầu response là object. Nếu tool trả JSON object thì dùng luôn, ngược lại bọc lại.
        try
        {
            var parsed = JToken.Parse(resultJson);
            if (parsed is JObject obj) return obj;
            return new JObject { ["result"] = parsed };
        }
        catch
        {
            return new JObject { ["result"] = resultJson };
        }
    }

    private static string ExtractText(JArray parts)
    {
        var texts = parts
            .Select(part => (string?)part["text"])
            .Where(text => !string.IsNullOrEmpty(text));
        return string.Join("\n", texts);
    }

    private static JObject ToWireContent(ChatMessage message)
    {
        var parts = new JArray();
        foreach (var block in message.Content)
        {
            switch (block.Kind)
            {
                case ContentKind.Text:
                    parts.Add(new JObject { ["text"] = block.Text ?? string.Empty });
                    break;
                case ContentKind.ToolCall:
                    parts.Add(new JObject
                    {
                        ["functionCall"] = new JObject
                        {
                            ["name"] = block.ToolName ?? string.Empty,
                            ["args"] = block.Arguments ?? new JObject()
                        }
                    });
                    break;
                case ContentKind.ToolResult:
                    parts.Add(new JObject
                    {
                        ["functionResponse"] = new JObject
                        {
                            ["name"] = block.ToolName ?? string.Empty,
                            ["response"] = WrapResult(block.ResultJson ?? string.Empty)
                        }
                    });
                    break;
            }
        }

        // Gemini dùng role "model" cho assistant, "user" cho user + function response.
        var role = message.Role == ChatMessage.Assistant ? "model" : "user";
        return new JObject { ["role"] = role, ["parts"] = parts };
    }
}
