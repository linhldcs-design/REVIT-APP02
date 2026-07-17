using Newtonsoft.Json.Linq;

namespace RevitAPP.Chat.Models;

public enum ContentKind
{
    Text,
    ToolCall,
    ToolResult
}

public sealed record ContentBlock(
    ContentKind Kind,
    string? Text = null,
    string? CallId = null,
    string? ToolName = null,
    JObject? Arguments = null,
    string? ResultJson = null)
{
    public static ContentBlock FromText(string text) => new(ContentKind.Text, Text: text);

    public static ContentBlock ToolCall(string? callId, string toolName, JObject arguments) =>
        new(ContentKind.ToolCall, CallId: callId, ToolName: toolName, Arguments: arguments);

    public static ContentBlock ToolResult(string? callId, string toolName, string resultJson) =>
        new(ContentKind.ToolResult, CallId: callId, ToolName: toolName, ResultJson: resultJson);
}

public sealed record ChatMessage(string Role, IReadOnlyList<ContentBlock> Content)
{
    public const string User = "user";
    public const string Assistant = "assistant";

    public static ChatMessage FromUserText(string text) => new(User, new[] { ContentBlock.FromText(text) });

    public static ChatMessage FromAssistantText(string text) => new(Assistant, new[] { ContentBlock.FromText(text) });

    public string PlainText() => string.Join("\n", Content
        .Where(block => block.Kind == ContentKind.Text && !string.IsNullOrEmpty(block.Text))
        .Select(block => block.Text!));
}
