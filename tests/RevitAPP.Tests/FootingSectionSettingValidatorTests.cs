using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class FootingSectionSettingValidatorTests
{
    [Fact]
    public void Default_WithSheet_IsValid()
    {
        var setting = FootingSectionSettingFactory.CreateDefault();
        Assert.Empty(FootingSectionSettingValidator.Validate(setting));
    }

    [Fact]
    public void MissingSheetNumber_ReportsError()
    {
        var setting = FootingSectionSettingFactory.CreateDefault() with
        {
            Sheet = new SheetConfig(Number: "", Name: "M3", TitleBlockName: null)
        };
        Assert.Contains(FootingSectionSettingValidator.Validate(setting),
            e => e.Contains("Số hiệu sheet"));
    }

    [Fact]
    public void NonPositiveScale_ReportsError()
    {
        var setting = FootingSectionSettingFactory.CreateDefault() with { Scale = 0 };
        Assert.Contains(FootingSectionSettingValidator.Validate(setting),
            e => e.Contains("Tỉ lệ"));
    }
}
