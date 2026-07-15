using IsolatedFootingRebar.Services;

namespace IsolatedFootingRebar.Tests;

public sealed class FootingMathTests
{
    [Theory]
    [InlineData(2000, 150, 14)]
    [InlineData(0, 150, 0)]
    [InlineData(2000, 0, 0)]
    public void SpacingToCount_UsesWholeGapsPlusBoundaryBar(
        double usableLengthMm, double spacingMm, int expected)
    {
        var count = FootingMath.SpacingToCount(usableLengthMm, spacingMm);

        Assert.Equal(expected, count);
    }

    [Fact]
    public void EvenPositions_MultipleBars_IncludesBothEnds()
    {
        var positions = FootingMath.EvenPositions(2000, 5);

        Assert.Equal([0, 500, 1000, 1500, 2000], positions);
    }

    [Fact]
    public void EvenPositions_OneBar_ReturnsMiddle()
    {
        var positions = FootingMath.EvenPositions(2000, 1);

        Assert.Equal([1000], positions);
    }

    [Fact]
    public void UnitConversion_RoundTripsMillimeters()
    {
        var feet = FootingMath.MmToFeet(1234.5);
        var mm = FootingMath.FeetToMm(feet);

        Assert.Equal(1234.5, mm, precision: 9);
    }

    [Theory]
    [InlineData(2000, 35, 1930)]
    [InlineData(60, 35, 0)]
    public void UsableLengthMm_ClampsAtZero(double dimensionMm, double coverMm, double expected)
    {
        var usable = FootingMath.UsableLengthMm(dimensionMm, coverMm);

        Assert.Equal(expected, usable);
    }
}
