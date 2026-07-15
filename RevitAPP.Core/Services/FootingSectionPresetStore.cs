using System.Text.Json;
using RevitAPP.Core.Models.FootingSection;

namespace RevitAPP.Core.Services;

/// <summary>
///     Lưu các preset mặt cắt móng dưới dạng JSON versioned. Store thuần .NET, không phụ thuộc Revit API.
/// </summary>
public sealed class FootingSectionPresetStore
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _storagePath;

    public FootingSectionPresetStore(string? storagePath = null)
    {
        _storagePath = string.IsNullOrWhiteSpace(storagePath) ? DefaultStoragePath() : storagePath;
    }

    public string StoragePath => _storagePath;

    public IReadOnlyList<FootingSectionSetting> Load() => Read(_storagePath);

    public void Save(IEnumerable<FootingSectionSetting> presets) => Write(_storagePath, presets);

    public IReadOnlyList<FootingSectionSetting> Import(string path) => Read(path);

    public void Export(string path, IEnumerable<FootingSectionSetting> presets) => Write(path, presets);

    private static IReadOnlyList<FootingSectionSetting> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return Array.Empty<FootingSectionSetting>();

            var json = File.ReadAllText(path);
            var envelope = JsonSerializer.Deserialize<PresetEnvelope>(json, JsonOptions);
            if (envelope is null || envelope.Version != CurrentVersion || envelope.Presets is null)
                return Array.Empty<FootingSectionSetting>();

            return envelope.Presets;
        }
        catch (JsonException)
        {
            return Array.Empty<FootingSectionSetting>();
        }
        catch (IOException)
        {
            return Array.Empty<FootingSectionSetting>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<FootingSectionSetting>();
        }
    }

    private static void Write(string path, IEnumerable<FootingSectionSetting> presets)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path rong.", nameof(path));
        if (presets is null) throw new ArgumentNullException(nameof(presets));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var envelope = new PresetEnvelope(CurrentVersion, presets.ToList());
        File.WriteAllText(fullPath, JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static string DefaultStoragePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RevitAPP", "footing-section-presets.json");
    }

    private sealed record PresetEnvelope(int Version, IReadOnlyList<FootingSectionSetting> Presets);
}
