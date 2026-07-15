using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Tests;

public sealed class FootingRebarModelDefaultsTests
{
    [Fact]
    public void DefaultModel_MatchesIsolatedFootingDefaults()
    {
        var model = new FootingRebarModel();

        Assert.True(model.BottomEnabled);
        Assert.True(model.TopEnabled);
        Assert.False(model.MidEnabled);
        Assert.False(model.VerticalEnabled);
        Assert.True(model.HorizontalEnabled);
        Assert.Equal(185, model.Cover.BottomMm);
        Assert.Equal(35, model.Cover.TopMm);
        Assert.Equal(35, model.Cover.SideMm);
    }

    [Fact]
    public void DefaultLayerConfigs_MatchDiameterSpacingAndHookDefaults()
    {
        var model = new FootingRebarModel();

        AssertLayer(model.BottomX, expectedSpacing: 150, expectedHook: 600);
        AssertLayer(model.BottomY, expectedSpacing: 150, expectedHook: 600);
        AssertLayer(model.TopX, expectedSpacing: 150, expectedHook: 400);
        AssertLayer(model.TopY, expectedSpacing: 150, expectedHook: 400);
        AssertLayer(model.MidX, expectedSpacing: 200, expectedHook: 200);
        AssertLayer(model.MidY, expectedSpacing: 200, expectedHook: 200);
    }

    [Fact]
    public void DefaultVerticalAndHorizontalConfigs_MatchScreenshotStyleDefaults()
    {
        var model = new FootingRebarModel();

        Assert.Equal(new RebarDiameter(6), model.Vertical.Diameter);
        Assert.Equal(200, model.Vertical.SpacingXMm);
        Assert.Equal(200, model.Vertical.SpacingYMm);
        Assert.Equal(200, model.Vertical.HookLengthMm);
        Assert.Equal(new RebarDiameter(6), model.Horizontal.DiameterX);
        Assert.True(model.Horizontal.Closed);
        Assert.Equal(100, model.Horizontal.HookLengthMm);
        Assert.Equal(1, model.Horizontal.Layers);
    }

    private static void AssertLayer(LayerBarConfig config, double expectedSpacing, double expectedHook)
    {
        Assert.True(config.Enabled);
        Assert.Equal(new RebarDiameter(6), config.Diameter);
        Assert.True(config.UseSpacing);
        Assert.Equal(expectedSpacing, config.SpacingMm);
        Assert.True(config.HookEnabled);
        Assert.Equal(expectedHook, config.HookLengthMm);
    }
}
