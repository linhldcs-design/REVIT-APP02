using BeamDrawing.Core.Models;
using BeamDrawing.Core.Services;
using Xunit;

namespace BeamDrawing.Core.Tests;

public class SettingValidatorTests
{
    [Fact]
    public void Default_setting_is_valid()
    {
        var result = SettingValidator.Validate(SettingFactory.CreateDefault());
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_name_is_invalid(string name)
    {
        var setting = SettingFactory.CreateDefault() with { Name = name };
        var result = SettingValidator.Validate(setting);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Tên setting"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public void Non_positive_sectional_scale_is_invalid(int scale)
    {
        var setting = SettingFactory.CreateDefault() with
        {
            Sectional = SettingFactory.CreateDefault().Sectional with { Scale = scale }
        };
        var result = SettingValidator.Validate(setting);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Sectional Elevation"));
    }

    [Fact]
    public void Non_positive_cross_section_scale_is_invalid()
    {
        var setting = SettingFactory.CreateDefault() with
        {
            CrossSection = new ViewConfig { Scale = 0 }
        };
        var result = SettingValidator.Validate(setting);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Cross Section"));
    }

    [Fact]
    public void Non_positive_spacing_factor_is_invalid()
    {
        var setting = SettingFactory.CreateDefault() with
        {
            Dimension = SettingFactory.CreateDefault().Dimension with { SpacingFactor = 0 }
        };
        var result = SettingValidator.Validate(setting);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("spacing factor"));
    }

    [Fact]
    public void Negative_distance_to_side_beam_is_invalid()
    {
        var setting = SettingFactory.CreateDefault() with
        {
            Dimension = SettingFactory.CreateDefault().Dimension with { DistanceToSideBeam = -1 }
        };
        var result = SettingValidator.Validate(setting);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("side beam"));
    }

    [Fact]
    public void Negative_distance_to_bot_face_is_invalid()
    {
        var setting = SettingFactory.CreateDefault() with
        {
            Dimension = SettingFactory.CreateDefault().Dimension with { DistanceToBotFace = -1 }
        };
        var result = SettingValidator.Validate(setting);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("bot face"));
    }

    [Fact]
    public void Multiple_errors_are_all_collected()
    {
        var setting = new BeamDrawingSetting
        {
            Name = "",
            Sectional = new ViewConfig { Scale = 0 },
            CrossSection = new ViewConfig { Scale = 0 }
        };
        var result = SettingValidator.Validate(setting);
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public void Default_factory_matches_reference_values()
    {
        var setting = SettingFactory.CreateDefault();
        Assert.Equal(25, setting.Sectional.Scale);
        Assert.Equal(25, setting.CrossSection.Scale);
        Assert.Equal(6, setting.Dimension.SpacingFactor);
        Assert.Equal(200, setting.Dimension.DistanceToSideBeam);
        Assert.True(setting.TagMapping.RebarBreakSymbol);
        Assert.True(setting.Flags.CrossSection);
    }
}
