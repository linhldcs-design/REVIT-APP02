namespace RevitAPP.Chat.Models;

/// <summary>
///     1 bong bóng hội thoại hiển thị trên UI. Role: "user" | "assistant" | "tool".
/// </summary>
public sealed record ChatBubble(string Role, string Text, bool IsError = false)
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}
