using System.IO;
using System.Text.Json;
using BeamDrawing.Core.Models;
using Serilog;

namespace BeamDrawing.Addin.Services;

/// <summary>Wrapper versioned cho file JSON — cho phép migration nhẹ khi schema đổi.</summary>
public sealed record SettingsFile(int SchemaVersion, IReadOnlyList<BeamDrawingSetting> Settings)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
///     Lưu/đọc danh sách setting ra JSON tại %APPDATA%/BeamDrawing/settings.json.
///     File hỏng/thiếu → trả list rỗng + log warn (không crash). Bỏ qua field lạ khi deserialize.
/// </summary>
public sealed class JsonSettingStore : ISettingStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public JsonSettingStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BeamDrawing", "settings.json");
    }

    public IReadOnlyList<BeamDrawingSetting> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<SettingsFile>(json, Options);
            return file?.Settings ?? [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Không đọc được settings.json — dùng danh sách rỗng");
            return [];
        }
    }

    public void Save(IReadOnlyList<BeamDrawingSetting> settings)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        WriteTo(_path, settings);
    }

    public void Export(IReadOnlyList<BeamDrawingSetting> settings, string path) => WriteTo(path, settings);

    public IReadOnlyList<BeamDrawingSetting> Import(string path)
    {
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<SettingsFile>(json, Options);
        return file?.Settings ?? [];
    }

    private static void WriteTo(string path, IReadOnlyList<BeamDrawingSetting> settings)
    {
        var file = new SettingsFile(SettingsFile.CurrentSchemaVersion, settings);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }
}
