using BeamRebarPro.Models;
using BeamRebarPro.Services;
using Xunit;

namespace BeamRebarPro.Tests;

public class QuickSettingValidatorTests
{
    [Fact]
    public void Default_IsValid()
    {
        var result = QuickSettingValidator.Validate(QuickSettingFactory.CreateDefault());
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MainBarZeroCount_IsInvalid()
    {
        var model = QuickSettingFactory.CreateDefault() with { MainTop = new MainBarConfig { Count = 0 } };
        var result = QuickSettingValidator.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("số thanh"));
    }

    [Fact]
    public void AdditionalPercentOutOfRange_IsInvalid()
    {
        var model = QuickSettingFactory.CreateDefault() with
        {
            TopAdditional = new AdditionalBarConfig { Enabled = true, Count = 2, LengthPercent = 150 }
        };
        var result = QuickSettingValidator.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("phần trăm"));
    }

    [Fact]
    public void NegativeHookLength_IsInvalid()
    {
        var model = QuickSettingFactory.CreateDefault() with
        {
            MainTop = new MainBarConfig { HookStart = new HookConfig { Enabled = true, LengthMm = -5 } }
        };
        var result = QuickSettingValidator.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("móc neo"));
    }

    [Fact]
    public void DisabledAdditional_SkipsValidation()
    {
        // Enabled=false → không validate dù count 0.
        var model = QuickSettingFactory.CreateDefault() with
        {
            TopAdditional = new AdditionalBarConfig { Enabled = false, Count = 0, LengthPercent = 999 }
        };
        var result = QuickSettingValidator.Validate(model);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void InvalidSpanOverride_IsReportedWithSpanIndex()
    {
        var model = QuickSettingFactory.CreateDefault() with
        {
            SpanOverrides = [new SpanRebarOverride { SpanIndex = 2, MainTop = new MainBarConfig { Count = -1 } }]
        };
        var result = QuickSettingValidator.Validate(model);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Nhịp 2"));
    }
}
