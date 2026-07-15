using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallRebar.Models;

namespace WallRebar.Services;

/// <summary>
///     Lưu/đọc preset cấu hình thép tường theo TÊN. File JSON ở
///     %AppData%\WallRebar\wall-presets.json = Dictionary&lt;tên, WallRebarModel&gt;.
///     Lỗi I/O bị nuốt — lưu preset là tiện ích, không được làm hỏng việc tạo thép.
/// </summary>
public static class WallConfigStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WallRebar", "wall-presets.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<string> Names()
    {
        try { return LoadMap().Keys.OrderBy(k => k).ToList(); }
        catch { return []; }
    }

    public static void Save(string name, WallRebarModel model)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var map = LoadMap();
            map[name] = model;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(map, Options));
        }
        catch { /* ignore I/O */ }
    }

    public static WallRebarModel? Load(string name)
    {
        try { return LoadMap().GetValueOrDefault(name); }
        catch { return null; }
    }

    public static void Delete(string name)
    {
        try
        {
            var map = LoadMap();
            if (map.Remove(name))
                File.WriteAllText(FilePath, JsonSerializer.Serialize(map, Options));
        }
        catch { /* ignore I/O */ }
    }

    private static Dictionary<string, WallRebarModel> LoadMap()
    {
        if (!File.Exists(FilePath)) return new Dictionary<string, WallRebarModel>();
        return JsonSerializer.Deserialize<Dictionary<string, WallRebarModel>>(File.ReadAllText(FilePath), Options)
               ?? new Dictionary<string, WallRebarModel>();
    }
}
