using System.Text.Json;
using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Core.Services;

/// <summary>
///     Lưu các preset Beam Drawing dưới dạng JSON versioned. Store thuần .NET, không phụ thuộc Revit API.
/// </summary>
public sealed class BeamDrawingPresetStore
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _storagePath;

    public BeamDrawingPresetStore(string? storagePath = null)
    {
        _storagePath = string.IsNullOrWhiteSpace(storagePath) ? DefaultStoragePath() : storagePath;
    }

    public string StoragePath => _storagePath;

    public IReadOnlyList<BeamDrawingSetting> Load() => Read(_storagePath);

    public void Save(IEnumerable<BeamDrawingSetting> presets) => Write(_storagePath, presets);

    public IReadOnlyList<BeamDrawingSetting> Import(string path) => Read(path);

    public void Export(string path, IEnumerable<BeamDrawingSetting> presets) => Write(path, presets);

    private static IReadOnlyList<BeamDrawingSetting> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return Array.Empty<BeamDrawingSetting>();

            var json = File.ReadAllText(path);
            var envelope = JsonSerializer.Deserialize<PresetEnvelope>(json, JsonOptions);
            if (envelope is null || envelope.Version != CurrentVersion || envelope.Presets is null)
                return Array.Empty<BeamDrawingSetting>();

            return envelope.Presets;
        }
        catch (JsonException)
        {
            return Array.Empty<BeamDrawingSetting>();
        }
        catch (IOException)
        {
            return Array.Empty<BeamDrawingSetting>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<BeamDrawingSetting>();
        }
    }

    private static void Write(string path, IEnumerable<BeamDrawingSetting> presets)
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
        return Path.Combine(appData, "RevitAPP", "beam-drawing-presets.json");
    }

    private sealed record PresetEnvelope(int Version, IReadOnlyList<BeamDrawingSetting> Presets);
}
