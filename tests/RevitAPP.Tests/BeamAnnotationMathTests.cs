using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class BeamAnnotationMathTests
{
    [Theory]
    [InlineData(5, 0, 10, 0, true)]
    [InlineData(10.05, 0, 10, 0.1, true)]
    [InlineData(10.2, 0, 10, 0.1, false)]
    [InlineData(5, 10, 0, 0, true)]
    public void IntersectsStation_HandlesToleranceAndReversedRange(
        double station, double min, double max, double tolerance, bool expected)
    {
        Assert.Equal(expected, BeamAnnotationMath.IntersectsStation(station, min, max, tolerance));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(8, 5)]
    public void CrossTagSlot_ClampsOverflowToLastConfiguredSlot(int index, int expected)
    {
        Assert.Equal(expected, BeamAnnotationMath.CrossTagSlot(index));
    }
}
