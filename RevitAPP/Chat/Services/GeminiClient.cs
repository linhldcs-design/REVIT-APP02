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
        var contents = new JArray(history.Select(ChatWireProtocol.ToGeminiContent));
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
                        ["response"] = ChatWireProtocol.WrapGeminiResult(result)
                    }
                });
            }

            contents.Add(new JObject { ["role"] = "user", ["parts"] = responseParts });
        }

        return "Đã đạt giới hạn số bước gọi công cụ. Vui lòng thử lại với yêu cầu cụ thể hơn.";
    }

    private static string ExtractText(JArray parts)
    {
        var texts = parts
            .Select(part => (string?)part["text"])
            .Where(text => !string.IsNullOrEmpty(text));
        return string.Join("\n", texts);
    }

}
