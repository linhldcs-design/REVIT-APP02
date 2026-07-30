using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Mcp;

/// <summary>Provider-neutral metadata used to expose one Chat AI tool through MCP.</summary>
public sealed record McpToolDescriptor(
    ToolSchema Schema,
    bool ReadOnly,
    bool Destructive,
    bool Idempotent,
    bool OpenWorld);
