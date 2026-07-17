using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevitAPP.Chat.Models;
using RevitAPP.Chat.Services;

namespace RevitAPP.Chat.ViewModels;

/// <summary>
///     ViewModel cho dialog Settings: chọn provider + model, nhập 3 API key. Lưu qua ChatSettingsStore
///     (mã hóa DPAPI). Đổi provider chỉ đổi model hiển thị, không mất key provider khác.
/// </summary>
public sealed partial class ChatSettingsViewModel : ObservableObject
{
    private readonly ChatSettingsStore _store;

    [ObservableProperty] private LlmProvider _selectedProvider;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _anthropicKey = string.Empty;
    [ObservableProperty] private string _openAiKey = string.Empty;
    [ObservableProperty] private string _geminiKey = string.Empty;
    [ObservableProperty] private int _maxTokens;
    [ObservableProperty] private bool _saved;
    [ObservableProperty] private string _testResult = string.Empty;

    public ChatSettingsViewModel(ChatSettingsStore store)
    {
        _store = store;
        var settings = store.Load();
        _selectedProvider = settings.ActiveProvider;
        _anthropicKey = settings.ApiKeyFor(LlmProvider.Anthropic);
        _openAiKey = settings.ApiKeyFor(LlmProvider.OpenAi);
        _geminiKey = settings.ApiKeyFor(LlmProvider.Gemini);
        _maxTokens = settings.MaxTokens;
        _model = settings.ModelFor(settings.ActiveProvider);
    }

    public IReadOnlyList<LlmProvider> ProviderOptions { get; } =
        new[] { LlmProvider.Anthropic, LlmProvider.OpenAi, LlmProvider.Gemini };

    partial void OnSelectedProviderChanged(LlmProvider value)
    {
        // Khi đổi provider, hiển thị model mặc định của provider đó.
        Model = ChatSettings.Default.Models[value];
    }

    [RelayCommand]
    private void Save()
    {
        _store.Save(BuildSettings());
        Saved = true;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var settings = BuildSettings();
        if (!settings.HasKeyFor(settings.ActiveProvider))
        {
            TestResult = $"Chưa nhập API key cho {settings.ActiveProvider}.";
            return;
        }

        TestResult = "Đang kiểm tra…";
        try
        {
            var client = LlmClientFactory.Create(settings);
            var reply = await client.SendAsync(
                new[] { ChatMessage.FromUserText("ping") },
                Array.Empty<ToolSchema>(),
                (_, _, _) => Task.FromResult("{}"),
                CancellationToken.None);
            TestResult = string.IsNullOrWhiteSpace(reply)
                ? "Kết nối OK (không có nội dung trả về)."
                : "Kết nối OK.";
        }
        catch (LlmClientException ex)
        {
            TestResult = ex.Message;
        }
        catch (Exception ex)
        {
            TestResult = $"Lỗi không xác định: {ex.Message}";
        }
    }

    private ChatSettings BuildSettings()
    {
        var keys = new Dictionary<LlmProvider, string>
        {
            [LlmProvider.Anthropic] = AnthropicKey?.Trim() ?? string.Empty,
            [LlmProvider.OpenAi] = OpenAiKey?.Trim() ?? string.Empty,
            [LlmProvider.Gemini] = GeminiKey?.Trim() ?? string.Empty
        };

        // Copy thủ công: constructor Dictionary(IReadOnlyDictionary) không có trên net48 (R23/R24).
        var models = new Dictionary<LlmProvider, string>();
        foreach (var pair in ChatSettings.Default.Models) models[pair.Key] = pair.Value;
        models[SelectedProvider] = string.IsNullOrWhiteSpace(Model)
            ? ChatSettings.Default.Models[SelectedProvider]
            : Model.Trim();

        return new ChatSettings(
            SelectedProvider,
            keys,
            models,
            MaxTokens > 0 ? MaxTokens : ChatSettings.Default.MaxTokens);
    }
}
