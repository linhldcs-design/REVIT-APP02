namespace RevitAPP.Chat.Mcp;

/// <summary>HTTP-neutral result returned by the MCP JSON-RPC dispatcher.</summary>
public sealed record McpDispatchResult(int StatusCode, string? Body);
