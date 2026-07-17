using Newtonsoft.Json.Linq;

namespace RevitAPP.Chat.Models;

/// <summary>Provider-neutral tool declaration backed by a JSON Schema object.</summary>
public sealed record ToolSchema(string Name, string Description, JObject ParametersJsonSchema);
