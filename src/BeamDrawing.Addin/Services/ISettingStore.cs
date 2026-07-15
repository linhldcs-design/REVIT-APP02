using BeamDrawing.Core.Models;

namespace BeamDrawing.Addin.Services;

/// <summary>
///     Lưu/đọc danh sách <see cref="BeamDrawingSetting"/>. Phase 3 dùng bản in-memory;
///     Phase 6 hiện thực bản JSON persistent (%APPDATA%/BeamDrawing/settings.json).
/// </summary>
public interface ISettingStore
{
    IReadOnlyList<BeamDrawingSetting> Load();
    void Save(IReadOnlyList<BeamDrawingSetting> settings);
    void Export(IReadOnlyList<BeamDrawingSetting> settings, string path);
    IReadOnlyList<BeamDrawingSetting> Import(string path);
}

/// <summary>Bản in-memory tạm cho Phase 3 — không persist. Thay bằng JsonSettingStore ở Phase 6.</summary>
public sealed class InMemorySettingStore : ISettingStore
{
    private List<BeamDrawingSetting> _settings = [];

    public IReadOnlyList<BeamDrawingSetting> Load() => _settings;
    public void Save(IReadOnlyList<BeamDrawingSetting> settings) => _settings = [.. settings];
    public void Export(IReadOnlyList<BeamDrawingSetting> settings, string path) { }
    public IReadOnlyList<BeamDrawingSetting> Import(string path) => _settings;
}
