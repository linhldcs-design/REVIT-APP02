using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitAPP.Licensing;

/// <summary>
/// Noi dung cache luu tren dia (%AppData%\RevitAPP\license.json).
/// </summary>
public sealed class LicenseCacheData
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }

    /// <summary>Thoi diem verify online thanh cong gan nhat (UTC ISO 8601).</summary>
    [JsonPropertyName("lastVerifiedUtc")] public string? LastVerifiedUtc { get; set; }

    /// <summary>Ket qua verify gan nhat (server cho phep hay khong).</summary>
    [JsonPropertyName("allowed")] public bool Allowed { get; set; }
}

/// <summary>
/// Doc/ghi cache license. Ghi atomic (temp + move) de tranh corrupt khi 2 process (addin + MCP) cung cham.
/// </summary>
public sealed class LicenseCache
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public LicenseCache(string? path = null) => _path = path ?? LicenseConfig.CacheFile;

    public LicenseCacheData? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var raw = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<LicenseCacheData>(raw);
        }
        catch
        {
            // Cache hong -> coi nhu chua co, buoc dang nhap lai.
            return null;
        }
    }

    public void Write(LicenseCacheData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOpts));
        // Move de-len atomic tren cung volume.
        if (File.Exists(_path)) File.Delete(_path);
        File.Move(tmp, _path);
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* ignore */ }
    }
}
