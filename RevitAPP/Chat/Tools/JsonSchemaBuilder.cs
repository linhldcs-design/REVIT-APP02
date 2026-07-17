using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RevitAPP.Chat.Tools;

/// <summary>
///     Hỗ trợ dựng JSON Schema object (subset an toàn cả 3 provider) cho tool params. Fluent, gọn.
/// </summary>
public sealed class JsonSchemaBuilder
{
    private readonly JObject _properties = new();
    private readonly List<string> _required = new();

    public JsonSchemaBuilder Number(string name, string description, bool required = false) =>
        Add(name, "number", description, required);

    public JsonSchemaBuilder Integer(string name, string description, bool required = false) =>
        Add(name, "integer", description, required);

    public JsonSchemaBuilder Bool(string name, string description, bool required = false) =>
        Add(name, "boolean", description, required);

    public JsonSchemaBuilder Text(string name, string description, bool required = false) =>
        Add(name, "string", description, required);

    public JsonSchemaBuilder Enum(string name, string description, string[] values, bool required = false)
    {
        var prop = new JObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = new JArray(values)
        };
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>Mảng số nguyên (dùng cho *Ids element id).</summary>
    public JsonSchemaBuilder IntegerArray(string name, string description, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = new JObject { ["type"] = "integer" }
        };
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build() => new()
    {
        ["type"] = "object",
        ["properties"] = _properties,
        ["required"] = new JArray(_required)
    };

    private JsonSchemaBuilder Add(string name, string type, string description, bool required)
    {
        _properties[name] = new JObject { ["type"] = type, ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }
}
