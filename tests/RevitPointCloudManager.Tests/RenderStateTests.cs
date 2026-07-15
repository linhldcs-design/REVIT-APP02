using Xunit;
using RevitPointCloudManager.Models;

namespace RevitPointCloudManager.Tests;

public class RenderStateTests
{
    [Fact]
    public void ResolveColor_WithDefaultState_ShouldReturnOriginalColor()
    {
        // Arrange
        var state = PointCloudRenderState.Default;
        uint originalColor = 0xFF00FF00; // màu xanh đục (Alpha=255, Blue=0, Green=255, Red=0)

        // Act
        var result = state.ResolveColor(originalColor);

        // Assert
        Assert.Equal(originalColor, result);
    }

    [Fact]
    public void ResolveColor_WithFixedColorMode_ShouldReturnFixedColor()
    {
        // Arrange
        uint fixedColor = 0xFFFF0050; // màu hồng đục
        var state = new PointCloudRenderState
        {
            UseOriginalColor = false,
            FixedColor = fixedColor
        };
        uint originalColor = 0xFF00FF00;

        // Act
        var result = state.ResolveColor(originalColor);

        // Assert
        Assert.Equal(fixedColor, result);
    }

    [Fact]
    public void ResolveColor_WithBrightnessChange_ShouldScaleRGB()
    {
        // Arrange
        var state = new PointCloudRenderState
        {
            Brightness = 50 // Tăng độ sáng 50%
        };
        uint originalColor = 0xFF102030; // R=16, G=32, B=48, A=255
        // delta = 50/100 * 255 = 127
        // New R = 16 + 127 = 143 (0x8F)
        // New G = 32 + 127 = 159 (0x9F)
        // New B = 48 + 127 = 175 (0xAF)
        // Alpha = 255 (0xFF)
        // Expected: 0xFFAF9F8F

        // Act
        var result = state.ResolveColor(originalColor);

        // Assert
        var r = result & 0xFF;
        var g = (result >> 8) & 0xFF;
        var b = (result >> 16) & 0xFF;
        var alpha = (result >> 24) & 0xFF;

        Assert.Equal(143u, r);
        Assert.Equal(159u, g);
        Assert.Equal(175u, b);
        Assert.Equal(255u, alpha);
    }

    [Fact]
    public void ResolveColor_WithTransparencyChange_ShouldReduceAlpha()
    {
        // Arrange
        var state = new PointCloudRenderState
        {
            Transparency = 30 // Trong suốt 30% -> Alpha = 70%
        };
        uint originalColor = 0xFF102030; // Alpha = 255 (100%)
        // New Alpha = 70% of 255 = 178 (0xB2)

        // Act
        var result = state.ResolveColor(originalColor);

        // Assert
        var alpha = (result >> 24) & 0xFF;
        Assert.Equal(179u, alpha); // 70/100 * 255 = 178.5 -> Round = 179
    }
}
