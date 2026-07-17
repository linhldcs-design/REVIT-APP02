using System.Runtime.Serialization;

namespace RevitAPP.Chat.Models;

[DataContract]
public sealed class ChatMemoryEntry
{
    [DataMember(Name = "id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [DataMember(Name = "createdAt")] public DateTime CreatedAt { get; set; } = DateTime.Now;
    [DataMember(Name = "project")] public string Project { get; set; } = string.Empty;
    [DataMember(Name = "userText")] public string UserText { get; set; } = string.Empty;
    [DataMember(Name = "assistantText")] public string AssistantText { get; set; } = string.Empty;
    [DataMember(Name = "toolName")] public string ToolName { get; set; } = string.Empty;
    [DataMember(Name = "argumentsJson")] public string ArgumentsJson { get; set; } = string.Empty;
    [DataMember(Name = "resultJson")] public string ResultJson { get; set; } = string.Empty;
    [DataMember(Name = "success")] public bool Success { get; set; }
    [DataMember(Name = "pinned")] public bool Pinned { get; set; }
    [DataMember(Name = "kind")] public string Kind { get; set; } = "conversation";
}

[DataContract]
internal sealed class ChatMemoryDocument
{
    [DataMember(Name = "version")] public int Version { get; set; } = 1;
    [DataMember(Name = "entries")] public List<ChatMemoryEntry> Entries { get; set; } = new();
}
