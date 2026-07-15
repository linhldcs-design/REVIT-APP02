using BeamRebar.Core.Models;
using BeamRebar.Core.Services;
using Xunit;

namespace BeamRebar.Core.Tests;

public class QuickSettingValidatorTests
{
    [Fact]
    public void Default_config_is_valid()
    {
        var result = QuickSettingValidator.Validate(QuickSettingFactory.CreateDefault());
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Zero_main_count_is_rejected()
    {
        var model = QuickSettingFactory.CreateDefault() with
        {
            MainTop = new MainBarConfig { Count = 0, Diameter = new RebarDiameter(16) }
        };

        var result = QuickSettingValidator.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Thép chủ trên"));
    }

    [Fact]
    public void Negative_cover_is_rejected()
    {
        var model = QuickSettingFactory.CreateDefault() with
        {
            Cover = new CoverSettings { TopMm = -5 }
        };

        var result = QuickSettingValidator.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Lớp bảo vệ"));
    }

    [Fact]
    public void TwoEnds_requires_positive_mid_spacing()
    {
        var model = QuickSettingFactory.CreateDefault() with
        {
            Stirrup = new StirrupConfig { Mode = StirrupMode.TwoEnds, SpacingEndMm = 150, SpacingMidMm = 0 }
        };

        var result = QuickSettingValidator.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("A2"));
    }

    [Fact]
    public void Disabled_additional_bar_skips_validation()
    {
        var model = QuickSettingFactory.CreateDefault() with
        {
            TopAdditional = new AdditionalBarConfig { Enabled = false, Count = 0 }
        };

        var result = QuickSettingValidator.Validate(model);
        Assert.True(result.IsValid);
    }
}
