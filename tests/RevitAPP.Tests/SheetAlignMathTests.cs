using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public class SheetAlignMathTests
{
    [Fact]
    public void IntersectLines_Perpendicular_ReturnsCrossing()
    {
        // Trục đứng x=3 (qua (3,0) hướng Y) giao trục ngang y=5 (qua (0,5) hướng X).
        var hit = SheetAlignMath.IntersectLines(
            (3, 0), (0, 1),
            (0, 5), (1, 0));

        Assert.NotNull(hit);
        Assert.Equal(3, hit!.Value.X, 9);
        Assert.Equal(5, hit.Value.Y, 9);
    }

    [Fact]
    public void IntersectLines_Oblique_ReturnsCrossing()
    {
        // y=x (qua gốc, hướng (1,1)) giao y=-x+4 (qua (0,4), hướng (1,-1)) tại (2,2).
        var hit = SheetAlignMath.IntersectLines(
            (0, 0), (1, 1),
            (0, 4), (1, -1));

        Assert.NotNull(hit);
        Assert.Equal(2, hit!.Value.X, 9);
        Assert.Equal(2, hit.Value.Y, 9);
    }

    [Fact]
    public void IntersectLines_Parallel_ReturnsNull()
    {
        var hit = SheetAlignMath.IntersectLines(
            (0, 0), (1, 0),
            (0, 5), (2, 0));

        Assert.Null(hit);
    }

    [Fact]
    public void ComputeDelta_ReturnsMasterMinusCurrent()
    {
        var delta = SheetAlignMath.ComputeDelta((10, 20), (4, 5));

        Assert.Equal(6, delta.X, 9);
        Assert.Equal(15, delta.Y, 9);
    }

    [Fact]
    public void ComputeDelta_AlreadyAligned_ReturnsZero()
    {
        var delta = SheetAlignMath.ComputeDelta((7, 7), (7, 7));

        Assert.Equal(0, delta.X, 9);
        Assert.Equal(0, delta.Y, 9);
    }
}
