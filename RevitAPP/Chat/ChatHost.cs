using RevitAPP.Chat.Services;
using RevitAPP.Chat.Tools;
using RevitAPP.Chat.ViewModels;
using Serilog;

namespace RevitAPP.Chat;

/// <summary>
///     Lightweight service host cho tính năng Chat AI. Tránh Microsoft.Extensions.DependencyInjection
///     để giảm xung đột version assembly bên trong Revit khi add-in khác preload DI version khác.
/// </summary>
public static class ChatHost
{
    private static ILogger? _logger;
    private static ChatSettingsStore? _settingsStore;
    private static ChatToolRegistry? _toolRegistry;
    private static ChatToolEventHandler? _bridge;
    private static ChatMemoryStore? _memoryStore;

    public static void Start()
    {
        _logger ??= Log.Logger;
        _settingsStore ??= new ChatSettingsStore();
        _toolRegistry ??= new ChatToolRegistry();
        _bridge ??= new ChatToolEventHandler(_toolRegistry);
        _memoryStore ??= new ChatMemoryStore();
    }

    /// <summary>Bridge singleton — ChatCommand gán ExternalEvent (tạo trong API context) cho nó.</summary>
    public static ChatToolEventHandler Bridge => _bridge ??= new ChatToolEventHandler(GetService<ChatToolRegistry>());

    public static T GetService<T>() where T : class
    {
        if (typeof(T) == typeof(ILogger))
            return (T)(_logger ??= Log.Logger);

        if (typeof(T) == typeof(ChatSettingsStore))
            return (T)(object)(_settingsStore ??= new ChatSettingsStore());

        if (typeof(T) == typeof(ChatToolRegistry))
            return (T)(object)(_toolRegistry ??= new ChatToolRegistry());

        if (typeof(T) == typeof(ChatMemoryStore))
            return (T)(object)(_memoryStore ??= new ChatMemoryStore());

        if (typeof(T) == typeof(ChatViewModel))
            return (T)(object)new ChatViewModel(
                GetService<ChatSettingsStore>(), GetService<ChatToolRegistry>(), Bridge,
                GetService<ChatMemoryStore>());

        throw new InvalidOperationException($"No service of type {typeof(T).FullName} is registered.");
    }
}
