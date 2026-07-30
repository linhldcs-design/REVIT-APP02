using RevitAPP.Chat.Mcp;
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
    private static ChatImageService? _imageService;
    private static Autodesk.Revit.UI.ExternalEvent? _externalEvent;
    private static RevitMcpHttpServer? _mcpServer;

    public static void Start()
    {
        _logger ??= Log.Logger;
        _settingsStore ??= new ChatSettingsStore();
        _toolRegistry ??= new ChatToolRegistry();
        _bridge ??= new ChatToolEventHandler(_toolRegistry);
        _memoryStore ??= new ChatMemoryStore();
        _imageService ??= new ChatImageService();
    }

    /// <summary>Bridge singleton — ChatCommand gán ExternalEvent (tạo trong API context) cho nó.</summary>
    public static ChatToolEventHandler Bridge => _bridge ??= new ChatToolEventHandler(GetService<ChatToolRegistry>());

    /// <summary>Tạo ExternalEvent dùng chung cho Chat AI và MCP trong Revit API context.</summary>
    public static void BindRevitBridge()
    {
        Start();
        _externalEvent ??= Autodesk.Revit.UI.ExternalEvent.Create(Bridge);
        Bridge.Bind(_externalEvent);
    }

    public static void StartMcpServer()
    {
        Start();
        if (_mcpServer is not null) return;

        try
        {
            var registry = GetService<ChatToolRegistry>();
            var surface = new McpToolSurface(registry, Bridge);
            var version = typeof(ChatHost).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            var dispatcher = new McpProtocolDispatcher(surface.Tools, surface.ExecuteAsync, version);
            var accessToken = McpAccessTokenStore.Resolve();
            var server = new RevitMcpHttpServer(
                dispatcher, surface.Tools.Count, accessToken, ResolveMcpPort());
            if (server.Start()) _mcpServer = server;
            else server.Dispose();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "RevitAPP MCP did not start because secure configuration failed");
        }
    }

    public static void Stop()
    {
        _mcpServer?.Dispose();
        _mcpServer = null;
        _externalEvent?.Dispose();
        _externalEvent = null;
    }

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

        if (typeof(T) == typeof(ChatImageService))
            return (T)(object)(_imageService ??= new ChatImageService());

        if (typeof(T) == typeof(ChatViewModel))
            return (T)(object)new ChatViewModel(
                GetService<ChatSettingsStore>(), GetService<ChatToolRegistry>(), Bridge,
                GetService<ChatMemoryStore>(), GetService<ChatImageService>());

        throw new InvalidOperationException($"No service of type {typeof(T).FullName} is registered.");
    }

    private static int ResolveMcpPort()
    {
        var value = Environment.GetEnvironmentVariable("REVITAPP_MCP_PORT");
        return int.TryParse(value, out var port) && port is >= 1024 and <= 65535
            ? port
            : RevitMcpHttpServer.DefaultPort;
    }
}
