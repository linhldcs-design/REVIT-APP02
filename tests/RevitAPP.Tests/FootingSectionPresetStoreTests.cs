using System.Text.Json;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class FootingSectionPresetStoreTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "RevitAPP.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoad_RoundTripsFullSetting()
    {
        var path = Path.Combine(_tempDirectory, "footing-presets.json");
        var store = new FootingSectionPresetStore(path);
        var expected = FootingSectionSettingFactory.CreateDefault() with { SettingName = "MONG-M3-25" };

        store.Save([expected]);
        var actual = store.Load();

        Assert.Equal([expected], actual);
    }

    [Fact]
    public void ExportThenImport_RoundTripsPresets()
    {
        var exportPath = Path.Combine(_tempDirectory, "export", "footing-presets.json");
        var store = new FootingSectionPresetStore(Path.Combine(_tempDirectory, "local.json"));
        var expected = FootingSectionSettingFactory.CreateDefault() with { SettingName = "Preset export" };

        store.Export(exportPath, [expected]);
        var actual = store.Import(exportPath);

        Assert.Equal([expected], actual);
    }

    [Fact]
    public void Save_WritesCurrentSchemaVersion()
    {
        var path = Path.Combine(_tempDirectory, "footing-presets.json");
        var store = new FootingSectionPresetStore(path);

        store.Save([FootingSectionSettingFactory.CreateDefault()]);

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(FootingSectionPresetStore.CurrentVersion, json.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("presets").ValueKind);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmptyList()
    {
        var path = Path.Combine(_tempDirectory, "footing-presets.json");
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(path, "{ not-json }");

        var actual = new FootingSectionPresetStore(path).Load();

        Assert.Empty(actual);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }
}
