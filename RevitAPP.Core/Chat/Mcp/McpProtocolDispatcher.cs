using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitAPP.Chat.Mcp;

/// <summary>
/// Handles the stateless MCP JSON-RPC surface. Transport and Revit-thread dispatch stay outside this class.
/// </summary>
public sealed class McpProtocolDispatcher
{
    public const string CurrentProtocolVersion = "2025-11-25";
    private const int MaxToolOutputCharacters = 25_000;

    private static readonly string[] SupportedVersions =
    {
        CurrentProtocolVersion
    };

    private readonly IReadOnlyList<McpToolDescriptor> _tools;
    private readonly Func<string, JObject, Task<string>> _executeTool;
    private readonly string _serverVersion;

    public McpProtocolDispatcher(
        IReadOnlyList<McpToolDescriptor> tools,
        Func<string, JObject, Task<string>> executeTool,
        string serverVersion)
    {
        _tools = tools;
        _executeTool = executeTool;
        _serverVersion = serverVersion;
    }

    public async Task<McpDispatchResult> DispatchAsync(string requestJson)
    {
        JObject request;
        try
        {
            request = JObject.Parse(requestJson);
        }
        catch (JsonException exception)
        {
            return Error(null, -32700, "Parse error: " + exception.Message, 400);
        }

        var id = request["id"]?.DeepClone();
        var method = request.Value<string?>("method");
        if (request.Value<string?>("jsonrpc") != "2.0" || string.IsNullOrWhiteSpace(method))
            return Error(id, -32600, "Invalid JSON-RPC request.", 400);

        if (id is null)
            return new McpDispatchResult(202, null);

        return method switch
        {
            "server/discover" => Success(id, DiscoverResult()),
            "initialize" => Success(id, InitializeResult(request["params"] as JObject)),
            "ping" => Success(id, new JObject()),
            "tools/list" => Success(id, new JObject { ["tools"] = BuildTools() }),
            "tools/call" => await CallToolAsync(id, request["params"] as JObject).ConfigureAwait(false),
            _ => Error(id, -32601, $"Method not found: {method}", 404)
        };
    }

    public static bool IsSupportedProtocolVersion(string? version) =>
        SupportedVersions.Contains(version, StringComparer.Ordinal);

    public static bool CanOmitProtocolVersionHeader(string requestJson)
    {
        try
        {
            var method = JObject.Parse(requestJson).Value<string?>("method");
            return method is "initialize" or "server/discover";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private JObject DiscoverResult() => new()
    {
        ["supportedVersions"] = new JArray(SupportedVersions),
        ["capabilities"] = Capabilities(),
        ["serverInfo"] = ServerInfo(),
        ["instructions"] =
            "RevitAPP exposes the same tools as Chat AI. Revit must be open. " +
            "Tools that change the model may require confirmation inside Revit."
    };

    private JObject InitializeResult(JObject? parameters)
    {
        var requested = parameters?.Value<string?>("protocolVersion");
        var selected = SupportedVersions.Contains(requested, StringComparer.Ordinal)
            ? requested!
            : CurrentProtocolVersion;
        return new JObject
        {
            ["protocolVersion"] = selected,
            ["capabilities"] = Capabilities(),
            ["serverInfo"] = ServerInfo(),
            ["instructions"] =
                "RevitAPP MCP shares the Chat AI tool registry and executes Revit API work on the Revit main thread."
        };
    }

    private static JObject Capabilities() => new()
    {
        ["tools"] = new JObject { ["listChanged"] = false }
    };

    private JObject ServerInfo() => new()
    {
        ["name"] = "revitapp",
        ["title"] = "RevitAPP",
        ["version"] = _serverVersion
    };

    private JArray BuildTools()
    {
        var result = new JArray();
        foreach (var descriptor in _tools)
        {
            result.Add(new JObject
            {
                ["name"] = descriptor.Schema.Name,
                ["title"] = descriptor.Schema.Name,
                ["description"] = descriptor.Schema.Description,
                ["inputSchema"] = descriptor.Schema.ParametersJsonSchema.DeepClone(),
                ["annotations"] = new JObject
                {
                    ["readOnlyHint"] = descriptor.ReadOnly,
                    ["destructiveHint"] = descriptor.Destructive,
                    ["idempotentHint"] = descriptor.Idempotent,
                    ["openWorldHint"] = descriptor.OpenWorld
                }
            });
        }
        return result;
    }

    private async Task<McpDispatchResult> CallToolAsync(JToken id, JObject? parameters)
    {
        var name = parameters?.Value<string?>("name");
        if (string.IsNullOrWhiteSpace(name))
            return Error(id, -32602, "tools/call requires params.name.", 400);
        if (_tools.All(tool => !string.Equals(tool.Schema.Name, name, StringComparison.Ordinal)))
            return ToolResult(id, JsonConvert.SerializeObject(new
            {
                success = false,
                message = $"MCP tool không tồn tại: {name}"
            }), true);

        var argumentsToken = parameters?["arguments"];
        if (argumentsToken is not null && argumentsToken.Type != JTokenType.Null &&
            argumentsToken is not JObject)
            return Error(id, -32602, "tools/call params.arguments must be an object.", 400);
        var arguments = argumentsToken as JObject ?? new JObject();
        try
        {
            var output = await _executeTool(name!, (JObject)arguments.DeepClone()).ConfigureAwait(false);
            var isFailure = IsFailure(output);
            if (output.Length > MaxToolOutputCharacters)
                output = output[..MaxToolOutputCharacters] +
                         "\n… MCP response truncated. Add filters or a smaller limit and retry.";
            return ToolResult(id, output, isFailure);
        }
        catch (Exception exception)
        {
            var output = JsonConvert.SerializeObject(new
            {
                success = false,
                message = $"Tool '{name}' lỗi: {exception.Message}"
            });
            return ToolResult(id, output, true);
        }
    }

    private static bool IsFailure(string output)
    {
        try
        {
            return JObject.Parse(output).Value<bool?>("success") == false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static McpDispatchResult ToolResult(JToken id, string text, bool isError)
    {
        var result = new JObject
        {
            ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError
        };
        try
        {
            if (JToken.Parse(text) is JObject structured) result["structuredContent"] = structured;
        }
        catch (JsonException)
        {
            // Text-only output remains valid MCP content.
        }
        return Success(id, result);
    }

    private static McpDispatchResult Success(JToken id, JObject result) =>
        Json(200, new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        });

    private static McpDispatchResult Error(JToken? id, int code, string message, int statusCode) =>
        Json(statusCode, new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id ?? JValue.CreateNull(),
            ["error"] = new JObject { ["code"] = code, ["message"] = message }
        });

    private static McpDispatchResult Json(int statusCode, JObject value) =>
        new(statusCode, value.ToString(Formatting.None));
}
