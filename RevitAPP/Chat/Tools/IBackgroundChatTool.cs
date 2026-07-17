namespace RevitAPP.Chat.Tools;

/// <summary>Marker for tools that do not access Revit API and are safe on the Chat worker thread.</summary>
public interface IBackgroundChatTool : IChatTool
{
}
