using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public class BillboardMathTests
{
    [Fact]
    public void QuadCorners_ProducesFourCornersAroundCenter()
    {
        var right = (1.0, 0.0, 0.0);
        var up = (0.0, 1.0, 0.0);
        var corners = BillboardMath.QuadCorners(0, 0, 0, right, up, half: 1.0);

        Assert.Equal(4, corners.Length);
        Assert.Equal((-1.0, -1.0, 0.0), corners[0]); // BL
        Assert.Equal((1.0, -1.0, 0.0), corners[1]);  // BR
        Assert.Equal((1.0, 1.0, 0.0), corners[2]);   // TR
        Assert.Equal((-1.0, 1.0, 0.0), corners[3]);  // TL
    }

    [Fact]
    public void QuadCorners_RespectsHalfSize()
    {
        var corners = BillboardMath.QuadCorners(0, 0, 0, (1, 0, 0), (0, 1, 0), half: 2.5);
        Assert.Equal(2.5, corners[1].X, 6);
    }

    [Fact]
    public void QuadCorners_OffsetByCenter()
    {
        var corners = BillboardMath.QuadCorners(10, 20, 30, (1, 0, 0), (0, 1, 0), half: 1.0);
        Assert.Equal((9.0, 19.0, 30.0), corners[0]);
    }

    [Fact]
    public void Cross_ComputesPerpendicular()
    {
        var c = BillboardMath.Cross((1, 0, 0), (0, 1, 0));
        Assert.Equal((0.0, 0.0, 1.0), c);
    }

    [Fact]
    public void Normalize_UnitLength()
    {
        var n = BillboardMath.Normalize((3, 0, 4));
        Assert.Equal(0.6, n.X, 6);
        Assert.Equal(0.8, n.Z, 6);
    }

    [Fact]
    public void Normalize_ZeroVector_ReturnsZero()
    {
        var n = BillboardMath.Normalize((0, 0, 0));
        Assert.Equal((0.0, 0.0, 0.0), n);
    }

    [Fact]
    public void PolygonFan_HasCenterPlusNRimVertices()
    {
        var fan = BillboardMath.PolygonFan(0, 0, 0, (1, 0, 0), (0, 1, 0), r: 1.0, sides: 8);
        Assert.Equal(9, fan.Length);          // 1 tâm + 8 viền
        Assert.Equal((0.0, 0.0, 0.0), fan[0]); // tâm
    }

    [Fact]
    public void PolygonFan_RimVerticesAtRadius()
    {
        var fan = BillboardMath.PolygonFan(0, 0, 0, (1, 0, 0), (0, 1, 0), r: 2.0, sides: 8);
        // Mỗi đỉnh viền cách tâm đúng bán kính r
        for (var i = 1; i < fan.Length; i++)
        {
            var d = Math.Sqrt(fan[i].X * fan[i].X + fan[i].Y * fan[i].Y + fan[i].Z * fan[i].Z);
            Assert.Equal(2.0, d, 6);
        }
    }

    [Fact]
    public void PolygonFan_FirstRimAlongRight()
    {
        var fan = BillboardMath.PolygonFan(5, 5, 0, (1, 0, 0), (0, 1, 0), r: 1.0, sides: 8);
        // Đỉnh đầu (angle 0) nằm theo hướng right
        Assert.Equal(6.0, fan[1].X, 6);
        Assert.Equal(5.0, fan[1].Y, 6);
    }
}
