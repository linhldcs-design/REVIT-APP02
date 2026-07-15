using System.Text.Json;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class BeamDrawingPresetStoreTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "RevitAPP.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoad_RoundTripsFullSetting()
    {
        var path = Path.Combine(_tempDirectory, "presets.json");
        var store = new BeamDrawingPresetStore(path);
        var expected = BeamDrawingSettingFactory.CreateDefault() with { SettingName = "BS-A1-25-BEAM-DX" };

        store.Save([expected]);
        var actual = store.Load();

        Assert.Equal([expected], actual);
    }

    [Fact]
    public void ExportThenImport_RoundTripsPresets()
    {
        var exportPath = Path.Combine(_tempDirectory, "export", "beam-presets.json");
        var store = new BeamDrawingPresetStore(Path.Combine(_tempDirectory, "local.json"));
        var expected = BeamDrawingSettingFactory.CreateDefault() with { SettingName = "Preset export" };

        store.Export(exportPath, [expected]);
        var actual = store.Import(exportPath);

        Assert.Equal([expected], actual);
    }

    [Fact]
    public void Save_WritesCurrentSchemaVersion()
    {
        var path = Path.Combine(_tempDirectory, "presets.json");
        var store = new BeamDrawingPresetStore(path);

        store.Save([BeamDrawingSettingFactory.CreateDefault()]);

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(BeamDrawingPresetStore.CurrentVersion, json.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("presets").ValueKind);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmptyList()
    {
        var path = Path.Combine(_tempDirectory, "presets.json");
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(path, "{ not-json }");

        var actual = new BeamDrawingPresetStore(path).Load();

        Assert.Empty(actual);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }
}
