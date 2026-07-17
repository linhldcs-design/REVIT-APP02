using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Services;

/// <summary>
///     Tạo <see cref="ILlmClient"/> theo provider đang chọn trong settings, inject API key + model tương ứng.
/// </summary>
public static class LlmClientFactory
{
    public static ILlmClient Create(ChatSettings settings)
    {
        var apiKey = settings.ActiveApiKey();
        var model = settings.ActiveModel();

        return settings.ActiveProvider switch
        {
            LlmProvider.Anthropic => new AnthropicClient(apiKey, model, settings.MaxTokens),
            LlmProvider.OpenAi => new OpenAiClient(apiKey, model),
            LlmProvider.Gemini => new GeminiClient(apiKey, model),
            _ => throw new InvalidOperationException($"Provider không hỗ trợ: {settings.ActiveProvider}")
        };
    }
}
