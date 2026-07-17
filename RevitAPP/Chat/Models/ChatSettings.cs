using System.Collections.Generic;

namespace RevitAPP.Chat.Models;

/// <summary>
///     Cấu hình chat: provider đang chọn, API key + model theo từng provider (giữ riêng để đổi provider
///     không mất key/model provider khác). ApiKeys ở dạng plaintext trong memory; store chịu trách nhiệm
///     mã hóa DPAPI khi ghi đĩa.
/// </summary>
public sealed record ChatSettings(
    LlmProvider ActiveProvider,
    IReadOnlyDictionary<LlmProvider, string> ApiKeys,
    IReadOnlyDictionary<LlmProvider, string> Models,
    int MaxTokens)
{
    public static ChatSettings Default { get; } = new(
        LlmProvider.Anthropic,
        new Dictionary<LlmProvider, string>
        {
            [LlmProvider.Anthropic] = string.Empty,
            [LlmProvider.OpenAi] = string.Empty,
            [LlmProvider.Gemini] = string.Empty
        },
        new Dictionary<LlmProvider, string>
        {
            [LlmProvider.Anthropic] = "claude-opus-4-8",
            [LlmProvider.OpenAi] = "gpt-4o",
            [LlmProvider.Gemini] = "gemini-2.5-pro"
        },
        MaxTokens: 4096);

    public string ActiveApiKey() => ApiKeyFor(ActiveProvider);

    public string ActiveModel() => ModelFor(ActiveProvider);

    public string ApiKeyFor(LlmProvider provider) =>
        ApiKeys.TryGetValue(provider, out var key) ? key : string.Empty;

    public string ModelFor(LlmProvider provider) =>
        Models.TryGetValue(provider, out var model) && !string.IsNullOrWhiteSpace(model)
            ? model
            : Default.Models[provider];

    public bool HasKeyFor(LlmProvider provider) => !string.IsNullOrWhiteSpace(ApiKeyFor(provider));
}
