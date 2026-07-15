using System.IO;
using System.Text.Json;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services;

/// <summary>
///     Lưu/đọc cấu hình thép theo ID dầm ra file JSON. Lần sau pick dầm đã cấu hình → tự load lại để
///     khỏi nhập lại. Key = ElementId (long) của dầm; value = <see cref="QuickSettingModel"/>.
/// </summary>
public static class BeamConfigStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BeamRebarPro", "beam-configs.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = false
    };

    /// <summary>Lưu cấu hình cho danh sách dầm (mọi dầm đã chọn cùng lưu 1 config). Bỏ qua nếu lỗi I/O.</summary>
    public static void Save(IReadOnlyList<long> beamIds, QuickSettingModel model)
    {
        try
        {
            var map = LoadMap();
            // Không lưu InternalSupportPoints / SpanOverrides (phụ thuộc phiên chọn) — chỉ lưu cấu hình thép.
            var clean = model with { InternalSupportPoints = [], InternalSupports = [], SecondaryBeamPoints = [], SecondaryBeams = [], SpanOverrides = [] };
            foreach (var id in beamIds)
                map[id.ToString()] = clean;

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(map, Options));
        }
        catch
        {
            // Lưu cấu hình là tiện ích, lỗi I/O không được làm hỏng việc tạo thép.
        }
    }

    /// <summary>Đọc cấu hình của dầm đầu tiên trong danh sách có config đã lưu; null nếu không có.</summary>
    public static QuickSettingModel? Load(IReadOnlyList<long> beamIds)
    {
        try
        {
            var map = LoadMap();
            foreach (var id in beamIds)
                if (map.TryGetValue(id.ToString(), out var model))
                    return model;
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static Dictionary<string, QuickSettingModel> LoadMap()
    {
        if (!File.Exists(FilePath)) return new Dictionary<string, QuickSettingModel>();
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<Dictionary<string, QuickSettingModel>>(json, Options)
               ?? new Dictionary<string, QuickSettingModel>();
    }
}
