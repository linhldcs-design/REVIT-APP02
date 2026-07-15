using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class BeamSectionStationMathTests
{
    [Fact]
    public void Resolve_DuplicateColumnsAtBothSupports_ReturnsNearSupportAndMiddleOfSpan()
    {
        var result = BeamSectionStationMath.Resolve(new[] { 0.0, 0.0, 1.0, 1.0 }, 0.01);

        Assert.Equal(0.035, result.Support, 6);
        Assert.Equal(0.5, result.MidSpan, 6);
    }

    [Fact]
    public void Resolve_TwoDistinctSupports_UsesFirstPhysicalSpan()
    {
        var result = BeamSectionStationMath.Resolve(new[] { 0.15, 0.65, 0.95 }, 0.01);

        Assert.Equal(0.1675, result.Support, 6);
        Assert.Equal(0.4, result.MidSpan, 6);
    }

    [Theory]
    [InlineData(0.0, 0.035, 0.5)]
    [InlineData(1.0, 0.965, 0.5)]
    public void Resolve_OneDetectedSupport_CutsTowardBeamBody(
        double columnStation, double expectedSupport, double expectedMidSpan)
    {
        var result = BeamSectionStationMath.Resolve(new[] { columnStation }, 0.01);

        Assert.Equal(expectedSupport, result.Support, 6);
        Assert.Equal(expectedMidSpan, result.MidSpan, 6);
    }

    [Fact]
    public void Resolve_NoDetectedSupport_UsesStableFallback()
    {
        var result = BeamSectionStationMath.Resolve(Array.Empty<double>(), 0.01);

        Assert.Equal(0.035, result.Support, 6);
        Assert.Equal(0.5, result.MidSpan, 6);
    }

    [Fact]
    public void EnsureOutsideSupport_MovesCutBeyondColumnEdgeWhenNominalIsInside()
    {
        var result = BeamSectionStationMath.EnsureOutsideSupport(0.035, -0.04, 0.04, true, 0.002);

        Assert.Equal(0.042, result, 6);
    }

    [Fact]
    public void EnsureOutsideSupport_KeepsNominalWhenAlreadyOutsideColumn()
    {
        var result = BeamSectionStationMath.EnsureOutsideSupport(0.035, -0.02, 0.02, true, 0.002);

        Assert.Equal(0.035, result, 6);
    }

    [Fact]
    public void EnsureOutsideSupport_HandlesSpanTowardDecreasingStation()
    {
        var result = BeamSectionStationMath.EnsureOutsideSupport(0.965, 0.96, 1.04, false, 0.002);

        Assert.Equal(0.958, result, 6);
    }
}
