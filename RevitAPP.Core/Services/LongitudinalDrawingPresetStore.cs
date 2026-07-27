using System.Text.Json;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Core.Services;

public sealed class LongitudinalDrawingPresetStore
{
    public const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly string _path;
    public LongitudinalDrawingPresetStore(string? path = null) =>
        _path = string.IsNullOrWhiteSpace(path) ? DefaultPath() : path!;
    public IReadOnlyList<LongitudinalDrawingSetting> Load() => Read(_path);
    public void Save(IEnumerable<LongitudinalDrawingSetting> values) => Write(_path, values);
    public IReadOnlyList<LongitudinalDrawingSetting> Import(string path) => Read(path);
    public bool TryImport(string path, out IReadOnlyList<LongitudinalDrawingSetting> values, out string error)
    {
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy file preset.", path);
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), JsonOptions);
            if (envelope is not { Version: CurrentVersion, Presets: not null })
                throw new InvalidDataException($"Preset không đúng version {CurrentVersion}.");
            values = envelope.Presets;
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            values = [];
            error = exception.Message;
            return false;
        }
    }
    public void Export(string path, IEnumerable<LongitudinalDrawingSetting> values) => Write(path, values);

    private static IReadOnlyList<LongitudinalDrawingSetting> Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), JsonOptions);
            return envelope is { Version: CurrentVersion, Presets: not null } ? envelope.Presets : [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void Write(string path, IEnumerable<LongitudinalDrawingSetting> values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(full, JsonSerializer.Serialize(new Envelope(CurrentVersion, values.ToList()), JsonOptions));
    }

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RevitAPP", "beam-longitudinal-drawing-presets.json");
    private sealed record Envelope(int Version, IReadOnlyList<LongitudinalDrawingSetting> Presets);
}
