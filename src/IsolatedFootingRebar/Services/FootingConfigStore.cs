using System.IO;
using System.Text.Json;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services;

/// <summary>
///     Lưu/đọc preset cấu hình thép móng theo TÊN (khác BeamConfigStore lưu theo id dầm). File JSON ở
///     %AppData%\IsolatedFootingRebar\footing-presets.json = Dictionary&lt;tên, FootingRebarModel&gt;.
///     Lỗi I/O bị nuốt — lưu preset là tiện ích, không được làm hỏng việc tạo thép.
/// </summary>
public static class FootingConfigStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IsolatedFootingRebar", "footing-presets.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static IReadOnlyList<string> Names()
    {
        try { return LoadMap().Keys.OrderBy(k => k).ToList(); }
        catch { return []; }
    }

    public static void Save(string name, FootingRebarModel model)
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

    public static FootingRebarModel? Load(string name)
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

    private static Dictionary<string, FootingRebarModel> LoadMap()
    {
        if (!File.Exists(FilePath)) return new Dictionary<string, FootingRebarModel>();
        return JsonSerializer.Deserialize<Dictionary<string, FootingRebarModel>>(File.ReadAllText(FilePath), Options)
               ?? new Dictionary<string, FootingRebarModel>();
    }
}
