using Xunit;
using RevitPointCloudManager.Services;

namespace RevitPointCloudManager.Tests;

public class BillboardMathTests
{
    [Fact]
    public void QuadCorners_ShouldComputeCorrectCoordinates()
    {
        // Arrange
        double cx = 10.0;
        double cy = 20.0;
        double cz = 30.0;
        
        // Giả sử camera nhìn thẳng xuống (-Z), tức là vector right là (1, 0, 0) và up là (0, 1, 0)
        var right = (1.0, 0.0, 0.0);
        var up = (0.0, 1.0, 0.0);
        double half = 2.0;

        // Act
        var corners = BillboardMath.QuadCorners(cx, cy, cz, right, up, half);

        // Assert
        Assert.Equal(4, corners.Length);

        // Thứ tự: BL, BR, TR, TL
        // BL = (cx - rx - ux, cy - ry - uy, cz - rz - uz) = (10 - 2 - 0, 20 - 0 - 2, 30) = (8, 18, 30)
        Assert.Equal(8.0, corners[0].X, 5);
        Assert.Equal(18.0, corners[0].Y, 5);
        Assert.Equal(30.0, corners[0].Z, 5);

        // BR = (cx + rx - ux, cy + ry - uy, cz + rz - uz) = (10 + 2 - 0, 20 - 0 - 2, 30) = (12, 18, 30)
        Assert.Equal(12.0, corners[1].X, 5);
        Assert.Equal(18.0, corners[1].Y, 5);
        Assert.Equal(30.0, corners[1].Z, 5);

        // TR = (cx + rx + ux, cy + ry + uy, cz + rz + uz) = (10 + 2 + 0, 20 + 0 + 2, 30) = (12, 22, 30)
        Assert.Equal(12.0, corners[2].X, 5);
        Assert.Equal(22.0, corners[2].Y, 5);
        Assert.Equal(30.0, corners[2].Z, 5);

        // TL = (cx - rx + ux, cy - ry + uy, cz - rz + uz) = (10 - 2 + 0, 20 + 0 + 2, 30) = (8, 22, 30)
        Assert.Equal(8.0, corners[3].X, 5);
        Assert.Equal(22.0, corners[3].Y, 5);
        Assert.Equal(30.0, corners[3].Z, 5);
    }

    [Fact]
    public void Normalize_ShouldHandleZeroLengthVector()
    {
        // Arrange
        var zero = (0.0, 0.0, 0.0);

        // Act
        var result = BillboardMath.Normalize(zero);

        // Assert
        Assert.Equal(0.0, result.X);
        Assert.Equal(0.0, result.Y);
        Assert.Equal(0.0, result.Z);
    }
}
