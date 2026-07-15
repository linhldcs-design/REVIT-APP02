using RevitAPP.Core.Models;
using Xunit;

namespace RevitAPP.Tests;

public class PointCloudRenderStateTests
{
    private const uint OpaqueRed = 0xFF0000FF; // A=FF, B=00, G=00, R=FF

    [Fact]
    public void Default_KeepsOriginalColorOpaque()
    {
        var result = PointCloudRenderState.Default.ResolveColor(OpaqueRed);
        Assert.Equal(OpaqueRed, result);
    }

    [Fact]
    public void Transparency100_MakesFullyTransparent()
    {
        var state = PointCloudRenderState.Default with { Transparency = 100 };
        var alpha = state.ResolveColor(OpaqueRed) >> 24;
        Assert.Equal(0u, alpha);
    }

    [Fact]
    public void Transparency50_HalfAlpha()
    {
        var state = PointCloudRenderState.Default with { Transparency = 50 };
        var alpha = (int)(state.ResolveColor(OpaqueRed) >> 24);
        Assert.InRange(alpha, 126, 129); // ~127
    }

    [Fact]
    public void PositiveBrightness_IncreasesChannels()
    {
        var state = PointCloudRenderState.Default with { Brightness = 50 };
        var result = state.ResolveColor(0xFF000000); // black
        var r = result & 0xFF;
        Assert.True(r > 0, "Brightness dương phải làm sáng kênh màu");
    }

    [Fact]
    public void Brightness_ClampsAtMax()
    {
        var state = PointCloudRenderState.Default with { Brightness = 100 };
        var result = state.ResolveColor(OpaqueRed);
        Assert.Equal(255u, result & 0xFF);        // R clamp 255
        Assert.Equal(255u, (result >> 8) & 0xFF); // G lên 255
    }

    [Fact]
    public void NegativeBrightness_DarkensAndClampsAtZero()
    {
        var state = PointCloudRenderState.Default with { Brightness = -100 };
        var result = state.ResolveColor(OpaqueRed);
        Assert.Equal(0u, result & 0xFF); // R về 0
    }

    [Fact]
    public void FixedColor_UsedWhenNotOriginal()
    {
        var state = PointCloudRenderState.Default with { UseOriginalColor = false, FixedColor = 0xFF00FF00 };
        var result = state.ResolveColor(OpaqueRed);
        Assert.Equal(0xFFu, (result >> 8) & 0xFF); // G channel = FF (xanh lá)
        Assert.Equal(0x00u, result & 0xFF);        // R = 0
    }

    [Fact]
    public void PointSizeChange_RequiresGeometryRebuildOnly()
    {
        var previous = PointCloudRenderState.Default;
        var next = previous with { PointSizeFeet = previous.PointSizeFeet + 0.01 };

        Assert.True(next.RequiresGeometryRebuild(previous));
        Assert.False(next.RequiresColorRebuild(previous));
        Assert.False(next.RequiresEffectUpdate(previous));
    }

    [Fact]
    public void BrightnessChange_RequiresColorRebuildOnly()
    {
        var previous = PointCloudRenderState.Default;
        var next = previous with { Brightness = 25 };

        Assert.False(next.RequiresGeometryRebuild(previous));
        Assert.True(next.RequiresColorRebuild(previous));
        Assert.False(next.RequiresEffectUpdate(previous));
    }

    [Fact]
    public void TransparencyChange_RequiresEffectUpdateOnly()
    {
        var previous = PointCloudRenderState.Default;
        var next = previous with { Transparency = 40 };

        Assert.False(next.RequiresGeometryRebuild(previous));
        Assert.False(next.RequiresColorRebuild(previous));
        Assert.True(next.RequiresEffectUpdate(previous));
    }

    [Fact]
    public void ResolveOpaqueVertexColor_KeepsAlphaOpaque()
    {
        var state = PointCloudRenderState.Default with { Transparency = 80 };
        var result = state.ResolveOpaqueVertexColor(OpaqueRed);

        Assert.Equal(0xFFu, result >> 24);
    }
}
