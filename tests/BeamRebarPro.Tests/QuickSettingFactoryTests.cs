using BeamRebarPro.Models;
using BeamRebarPro.Services;
using Xunit;

namespace BeamRebarPro.Tests;

public class QuickSettingFactoryTests
{
    [Fact]
    public void Default_HasExpectedMainBars()
    {
        var model = QuickSettingFactory.CreateDefault();
        Assert.Equal(3, model.MainTop.Count);
        Assert.Equal(16, model.MainTop.Diameter.Millimeters);
        Assert.Equal(StirrupMode.TwoEnds, model.Stirrup.Mode);
    }

    [Fact]
    public void Default_BottomAdditionalIsMidspanSide()
    {
        var model = QuickSettingFactory.CreateDefault();
        Assert.Equal(AdditionalBarSide.BottomAtMidspan, model.BottomAdditional.Side);
        Assert.Equal(AdditionalBarSide.TopAtSupport, model.TopAdditional.Side);
    }

    [Fact]
    public void ResolveForSpan_NoOverride_ReturnsBaseModel()
    {
        var model = QuickSettingFactory.CreateDefault();
        var resolved = QuickSettingFactory.ResolveForSpan(model, 0);
        Assert.Same(model, resolved);
    }

    [Fact]
    public void ResolveForSpan_OverridesOnlySpecifiedFields()
    {
        var model = QuickSettingFactory.CreateDefault() with
        {
            SpanOverrides = [new SpanRebarOverride
            {
                SpanIndex = 1,
                MainTop = new MainBarConfig { Count = 5, Diameter = new RebarDiameter(20) }
            }]
        };

        var resolved = QuickSettingFactory.ResolveForSpan(model, 1);

        // Field override đổi.
        Assert.Equal(5, resolved.MainTop.Count);
        Assert.Equal(20, resolved.MainTop.Diameter.Millimeters);
        // Field không override giữ nguyên base.
        Assert.Equal(model.MainBottom.Count, resolved.MainBottom.Count);
        Assert.Equal(model.Stirrup.SpacingEndMm, resolved.Stirrup.SpacingEndMm);
    }

    [Fact]
    public void ResolveForSpan_UnmatchedIndex_ReturnsBase()
    {
        var model = QuickSettingFactory.CreateDefault() with
        {
            SpanOverrides = [new SpanRebarOverride { SpanIndex = 1, MainTop = new MainBarConfig { Count = 9 } }]
        };

        var resolved = QuickSettingFactory.ResolveForSpan(model, 0);
        Assert.Equal(model.MainTop.Count, resolved.MainTop.Count);
    }
}
