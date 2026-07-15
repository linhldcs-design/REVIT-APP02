using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public class BeamDrawingSettingTests
{
    [Fact]
    public void CreateDefault_IsValid()
    {
        var setting = BeamDrawingSettingFactory.CreateDefault();

        var errors = BeamDrawingSettingValidator.Validate(setting);

        Assert.Empty(errors);
        Assert.Equal(25, setting.Sectional.Scale);
        Assert.Equal(6, setting.Dim.SpacingFactor);
        Assert.True(setting.Flags.CrossSection);
        Assert.True(setting.Spot.Enabled);
        Assert.True(setting.Dim.Enabled);
    }

    [Fact]
    public void Validate_SectionalScaleZero_ReturnsError()
    {
        var setting = BeamDrawingSettingFactory.CreateDefault() with
        {
            Sectional = new PerViewConfig(0, null, null, null)
        };

        var errors = BeamDrawingSettingValidator.Validate(setting);

        Assert.Contains(errors, e => e.Contains("Tỉ lệ mặt cắt dọc"));
    }

    [Fact]
    public void Validate_EmptySheetNumber_ReturnsError()
    {
        var setting = BeamDrawingSettingFactory.CreateDefault() with
        {
            Sheet = new SheetConfig("  ", "TEN", null)
        };

        var errors = BeamDrawingSettingValidator.Validate(setting);

        Assert.Contains(errors, e => e.Contains("Số hiệu sheet"));
    }

    [Fact]
    public void Validate_SpacingFactorZero_ReturnsError()
    {
        var setting = BeamDrawingSettingFactory.CreateDefault() with
        {
            Dim = BeamDrawingSettingFactory.CreateDefault().Dim with { SpacingFactor = 0 }
        };

        var errors = BeamDrawingSettingValidator.Validate(setting);

        Assert.Contains(errors, e => e.Contains("spacing factor"));
    }

    [Fact]
    public void Validate_CrossSectionOffSkipsCrossScaleCheck()
    {
        var setting = BeamDrawingSettingFactory.CreateDefault() with
        {
            CrossSection = new PerViewConfig(0, null, null, null),
            Flags = BeamDrawingSettingFactory.CreateDefault().Flags with { CrossSection = false }
        };

        var errors = BeamDrawingSettingValidator.Validate(setting);

        Assert.Empty(errors);
    }

    [Fact]
    public void BeamGeometry_LengthFeet_MatchesEndpointDistance()
    {
        var geometry = new BeamGeometry(
            Start: new Point3(0, 0, 0),
            End: new Point3(3, 4, 0),
            WidthFeet: 1,
            HeightFeet: 2,
            TopZFeet: 2,
            BottomZFeet: 0);

        Assert.Equal(5, geometry.LengthFeet, 9);
    }
}
