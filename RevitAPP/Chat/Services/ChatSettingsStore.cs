using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Services;

/// <summary>
///     Đọc/ghi cấu hình chat vào %APPDATA%\RevitAPP\chat-settings.dat. Ba API key được mã hóa bằng DPAPI
///     (ProtectedData, scope CurrentUser) — chỉ user + máy hiện tại giải mã được. Key không bao giờ ghi
///     plaintext ra đĩa và không log.
/// </summary>
public sealed class ChatSettingsStore
{
    private static readonly LlmProvider[] Providers =
        { LlmProvider.Anthropic, LlmProvider.OpenAi, LlmProvider.Gemini };

    private readonly string _filePath;

    public ChatSettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RevitAPP");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "chat-settings.dat");
    }

    public ChatSettings Load()
    {
        if (!File.Exists(_filePath)) return ChatSettings.Default;

        try
        {
            var dto = Deserialize(File.ReadAllBytes(_filePath));
            var keys = new Dictionary<LlmProvider, string>();
            var models = new Dictionary<LlmProvider, string>();
            foreach (var provider in Providers)
            {
                keys[provider] = Unprotect(dto.KeyFor(provider));
                var model = dto.ModelFor(provider);
                models[provider] = string.IsNullOrWhiteSpace(model)
                    ? ChatSettings.Default.Models[provider]
                    : model;
            }

            return new ChatSettings(
                ParseProvider(dto.ActiveProvider),
                keys,
                models,
                dto.MaxTokens > 0 ? dto.MaxTokens : ChatSettings.Default.MaxTokens);
        }
        catch
        {
            // File hỏng/không đọc được → về mặc định thay vì crash.
            return ChatSettings.Default;
        }
    }

    public void Save(ChatSettings settings)
    {
        var dto = new SettingsDto
        {
            ActiveProvider = settings.ActiveProvider.ToString(),
            MaxTokens = settings.MaxTokens,
            AnthropicModel = settings.ModelFor(LlmProvider.Anthropic),
            OpenAiModel = settings.ModelFor(LlmProvider.OpenAi),
            GeminiModel = settings.ModelFor(LlmProvider.Gemini),
            AnthropicKey = Protect(settings.ApiKeyFor(LlmProvider.Anthropic)),
            OpenAiKey = Protect(settings.ApiKeyFor(LlmProvider.OpenAi)),
            GeminiKey = Protect(settings.ApiKeyFor(LlmProvider.Gemini))
        };

        File.WriteAllBytes(_filePath, Serialize(dto));
    }

    private static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        try
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return string.Empty;
        try
        {
            var decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // Decrypt 1 key hỏng (đổi user/máy) → coi key đó rỗng, không làm hỏng cả file.
            return string.Empty;
        }
    }

    private static LlmProvider ParseProvider(string? value) =>
        Enum.TryParse<LlmProvider>(value, out var provider) ? provider : LlmProvider.Anthropic;

    private static byte[] Serialize(SettingsDto dto)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(SettingsDto)).WriteObject(stream, dto);
        return stream.ToArray();
    }

    private static SettingsDto Deserialize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return (SettingsDto)new DataContractJsonSerializer(typeof(SettingsDto)).ReadObject(stream)!;
    }

    [DataContract]
    private sealed class SettingsDto
    {
        [DataMember(Name = "activeProvider")] public string ActiveProvider { get; set; } = "Anthropic";
        [DataMember(Name = "maxTokens")] public int MaxTokens { get; set; } = 4096;
        [DataMember(Name = "anthropicModel")] public string AnthropicModel { get; set; } = string.Empty;
        [DataMember(Name = "openAiModel")] public string OpenAiModel { get; set; } = string.Empty;
        [DataMember(Name = "geminiModel")] public string GeminiModel { get; set; } = string.Empty;
        [DataMember(Name = "anthropicKey")] public string AnthropicKey { get; set; } = string.Empty;
        [DataMember(Name = "openAiKey")] public string OpenAiKey { get; set; } = string.Empty;
        [DataMember(Name = "geminiKey")] public string GeminiKey { get; set; } = string.Empty;

        public string KeyFor(LlmProvider provider) => provider switch
        {
            LlmProvider.Anthropic => AnthropicKey,
            LlmProvider.OpenAi => OpenAiKey,
            LlmProvider.Gemini => GeminiKey,
            _ => string.Empty
        };

        public string ModelFor(LlmProvider provider) => provider switch
        {
            LlmProvider.Anthropic => AnthropicModel,
            LlmProvider.OpenAi => OpenAiModel,
            LlmProvider.Gemini => GeminiModel,
            _ => string.Empty
        };
    }
}
