using FootingDrawing.Core.Models;

namespace FootingDrawing.Addin.Services;

/// <summary>Lưu/đọc danh sách <see cref="FootingDrawingSetting"/> (persistent + import/export).</summary>
public interface ISettingStore
{
    IReadOnlyList<FootingDrawingSetting> Load();
    void Save(IReadOnlyList<FootingDrawingSetting> settings);
    void Export(IReadOnlyList<FootingDrawingSetting> settings, string path);
    IReadOnlyList<FootingDrawingSetting> Import(string path);
}
