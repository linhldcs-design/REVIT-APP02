using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class BeamSectionBoxMathTests
{
    [Fact]
    public void CrossHalfDepth_ProducesExactly150MillimeterFarClipOffset()
    {
        var halfDepthFeet = BeamSectionBoxMath.HalfDepthFeet(BeamSectionBoxMath.CrossFarClipOffsetMm);
        var fullDepthMm = halfDepthFeet * 2.0 * BeamSectionBoxMath.MillimetersPerFoot;

        Assert.Equal(150.0, fullDepthMm, 6);
    }

    [Fact]
    public void CrossFarClipTarget_ConvertsToRevitFeetExactly()
    {
        var targetFeet = BeamSectionBoxMath.CrossFarClipOffsetMm / BeamSectionBoxMath.MillimetersPerFoot;

        Assert.Equal(150.0, targetFeet * BeamSectionBoxMath.MillimetersPerFoot, 6);
    }

    [Fact]
    public void CrossSupportClearance_IncludesHalfFarClipAndSafetyGap()
    {
        var clearanceMm = BeamSectionBoxMath.CrossSupportClearanceFeet() * BeamSectionBoxMath.MillimetersPerFoot;

        Assert.Equal(85.0, clearanceMm, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void HalfDepthFeet_RejectsNonPositiveOffset(double offsetMm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeamSectionBoxMath.HalfDepthFeet(offsetMm));
    }
}
