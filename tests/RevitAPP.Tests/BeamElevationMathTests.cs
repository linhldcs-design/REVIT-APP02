using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class BeamElevationMathTests
{
    [Fact]
    public void Resolve_ParametersOverrideFamilyBoundingBoxControlGeometry()
    {
        var result = BeamElevationMath.Resolve(
            topParameter: 8050, bottomParameter: 7600, height: 450,
            boundingTop: 8100, boundingBottom: 7600, axisZ: 0);

        Assert.Equal((8050d, 7600d), result);
    }

    [Fact]
    public void Resolve_TopOnly_DerivesBottomFromSectionHeight()
    {
        var result = BeamElevationMath.Resolve(8050, null, 450, 8100, 7600, 0);

        Assert.Equal((8050d, 7600d), result);
    }

    [Fact]
    public void Resolve_MissingParameters_FallsBackToBoundingBox()
    {
        var result = BeamElevationMath.Resolve(null, null, 450, 8100, 7600, 0);

        Assert.Equal((8100d, 7600d), result);
    }
}
