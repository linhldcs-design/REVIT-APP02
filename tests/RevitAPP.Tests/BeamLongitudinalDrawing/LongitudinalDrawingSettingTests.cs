using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.BeamLongitudinalDrawing;

public sealed class LongitudinalDrawingSettingTests
{
    [Fact]
    public void Validate_DefaultWithoutDocumentResources_ReturnsRequiredResourceErrors()
    {
        var errors = LongitudinalDrawingSettingValidator.Validate(LongitudinalDrawingSettingFactory.CreateDefault());
        Assert.Contains(errors, value => value.Contains("Dimension Type"));
        Assert.Contains(errors, value => value.Contains("Section Type dọc"));
    }

    [Fact]
    public void PresetStore_RoundTrip_PreservesEveryField()
    {
        var path = Path.Combine(Path.GetTempPath(), $"longitudinal-{Guid.NewGuid():N}.json");
        try
        {
            var value = LongitudinalDrawingSettingFactory.CreateDefault() with
            {
                SettingName = "A1", DimensionTypeName = "Dim", LongitudinalRebarTagTypeName = "Tag L",
                StirrupTagTypeName = "Tag D", DetailComponentTypeName = "Detail", ViewportTypeName = "VP",
                LongitudinalSectionTypeName = "Doc", CrossSectionTypeName = "Ngang",
                SpotElevationTypeName = "Spot", TitleBlockName = "A1"
            };
            var store = new LongitudinalDrawingPresetStore(path);
            store.Save([value]);
            Assert.Equal(value, Assert.Single(store.Load()));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TryImport_CorruptFile_ReturnsFailureWithoutValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"longitudinal-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not-json");
            var store = new LongitudinalDrawingPresetStore();
            Assert.False(store.TryImport(path, out var values, out var error));
            Assert.Empty(values);
            Assert.NotEmpty(error);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TryImport_WrongVersion_ReturnsFailureWithoutValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"longitudinal-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"version\":999,\"presets\":[]}");
            var store = new LongitudinalDrawingPresetStore();
            Assert.False(store.TryImport(path, out var values, out var error));
            Assert.Empty(values);
            Assert.Contains("version", error, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
