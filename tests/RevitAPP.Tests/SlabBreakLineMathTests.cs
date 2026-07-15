using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class SlabBreakLineMathTests
{
    [Fact]
    public void Calculate_WideSlab_UsesPreferredStubOnBothSides()
    {
        var result = SlabBreakLineMath.Calculate(0, 1, -2, 3, 0.1, 0.01);
        Assert.NotNull(result.LeftX);
        Assert.NotNull(result.RightX);
        Assert.Equal(-0.1, result.LeftX.Value, 8);
        Assert.Equal(1.1, result.RightX.Value, 8);
    }

    [Fact]
    public void Calculate_ShortOverhang_ShrinksInsideActualSlab()
    {
        var result = SlabBreakLineMath.Calculate(0, 1, -0.04, 1.02, 0.1, 0.01);
        Assert.NotNull(result.LeftX);
        Assert.NotNull(result.RightX);
        Assert.Equal(-0.03, result.LeftX.Value, 8);
        Assert.Equal(1.015, result.RightX.Value, 8);
    }

    [Fact]
    public void Calculate_OneSidedSlab_PlacesOnlyExistingSide()
    {
        var result = SlabBreakLineMath.Calculate(0, 1, 0, 2, 0.1, 0.01);
        Assert.Null(result.LeftX);
        Assert.NotNull(result.RightX);
        Assert.Equal(1.1, result.RightX.Value, 8);
    }

    [Fact]
    public void Calculate_NoOverhang_PlacesNoBreakLine()
    {
        var result = SlabBreakLineMath.Calculate(0, 1, 0, 1, 0.1, 0.01);
        Assert.Null(result.LeftX);
        Assert.Null(result.RightX);
    }

    [Fact]
    public void Calculate_TwoOneSidedSlabs_CanSupplyIndependentLeftAndRightBreaks()
    {
        var leftSlab = SlabBreakLineMath.Calculate(0, 1, -2, 0.8, 0.1, 0.01);
        var rightSlab = SlabBreakLineMath.Calculate(0, 1, 0.2, 3, 0.1, 0.01);

        Assert.NotNull(leftSlab.LeftX);
        Assert.Null(leftSlab.RightX);
        Assert.Null(rightSlab.LeftX);
        Assert.NotNull(rightSlab.RightX);
    }
}
