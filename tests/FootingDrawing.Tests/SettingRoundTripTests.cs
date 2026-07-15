using System.Text.Json;
using FootingDrawing.Core.Models;
using Xunit;

namespace FootingDrawing.Tests;

public class SettingRoundTripTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Setting_JsonRoundTrip_PreservesAllFields()
    {
        var original = new FootingDrawingSetting
        {
            Name = "MB-M3",
            DimensionTypeName = "@BS-Dim A1",
            FootingRebarTagTypeName = "A3_P_RT_DK&KC_MID",
            ColumnRebarTagTypeName = "A3_P_RT_DK&KC_MID",
            BendingDetailTypeName = "Bending Detail",
            ParentPlanViewName = "MÓNG(1)",
            CalloutTypeName = "Detail View",
            ViewTemplateName = "STRUCT-PLAN",
            ViewportTypeName = "No Title",
            TitleBlockName = "A1",
            SheetNumber = "KC-01",
            SheetName = "MẶT BẰNG THÉP MÓNG",
            FootingTagEnabled = true,
            ColumnTagEnabled = false,
            BendingDetailEnabled = true,
            TitleEnabled = true,
            DimOverallEnabled = true,
            DimBaseEnabled = false,
            DimPedestalEnabled = true,
            Scale = 25
        };

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<FootingDrawingSetting>(json, Options);

        Assert.Equal(original, restored); // record value equality
    }

    [Fact]
    public void Setting_Defaults_AreSensible()
    {
        var setting = new FootingDrawingSetting { Name = "x" };
        Assert.Equal(25, setting.Scale);
        Assert.True(setting.FootingTagEnabled);
        Assert.True(setting.DimBaseEnabled);
        Assert.Equal("MẶT BẰNG THÉP MÓNG", setting.TitlePrefix);
    }
}
