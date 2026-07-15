using System.Text.Json;
using BeamDrawing.Core.Models;
using BeamDrawing.Core.Services;
using Xunit;

namespace BeamDrawing.Core.Tests;

/// <summary>
///     Roundtrip serialize/deserialize cho BeamDrawingSetting — bảo vệ chống schema drift (rủi ro #5).
///     Test trực tiếp trên record bằng System.Text.Json (không phụ thuộc Revit/WPF nên chạy out-of-process).
/// </summary>
public class SettingSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Default_setting_roundtrips_unchanged()
    {
        var original = SettingFactory.CreateDefault();

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<BeamDrawingSetting>(json, Options);

        Assert.NotNull(restored);
        Assert.Equal(original, restored); // record value equality so sánh toàn bộ field lồng nhau.
    }

    [Fact]
    public void List_of_settings_roundtrips()
    {
        var list = new List<BeamDrawingSetting>
        {
            SettingFactory.CreateDefault() with { Name = "BEAM-DX" },
            SettingFactory.CreateDefault() with { Name = "BEAM-DY", SheetName = "DẦM TRỤC Y" }
        };

        var json = JsonSerializer.Serialize(list, Options);
        var restored = JsonSerializer.Deserialize<List<BeamDrawingSetting>>(json, Options);

        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        Assert.Equal("BEAM-DX", restored[0].Name);
        Assert.Equal("DẦM TRỤC Y", restored[1].SheetName);
    }

    [Fact]
    public void Unknown_fields_are_ignored_on_deserialize()
    {
        // Schema cũ/mới có field lạ → vẫn deserialize được phần biết.
        var json = """
        { "Name": "BEAM-OLD", "UnknownLegacyField": 123, "Sectional": { "Scale": 50 } }
        """;

        var restored = JsonSerializer.Deserialize<BeamDrawingSetting>(json, Options);

        Assert.NotNull(restored);
        Assert.Equal("BEAM-OLD", restored!.Name);
        Assert.Equal(50, restored.Sectional.Scale);
    }

    [Fact]
    public void Nested_tag_mapping_survives_roundtrip()
    {
        var original = SettingFactory.CreateDefault() with
        {
            TagMapping = new RebarTagMapping
            {
                T1TagTypeName = "BSA1-RT",
                D0TagTypeName = "BS-A1-SL",
                RebarBreakSymbol = true
            }
        };

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<BeamDrawingSetting>(json, Options);

        Assert.Equal("BSA1-RT", restored!.TagMapping.T1TagTypeName);
        Assert.Equal("BS-A1-SL", restored.TagMapping.D0TagTypeName);
        Assert.True(restored.TagMapping.RebarBreakSymbol);
    }
}
