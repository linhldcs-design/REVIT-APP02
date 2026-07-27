using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.BeamLongitudinalDrawing;

/// <summary>Khóa các helper mặt cắt ngang sẽ được tái sử dụng ở Phase 05.</summary>
public sealed class ExistingCrossSectionCharacterizationTests
{
    [Fact]
    public void ExistingStationMath_DefaultBeam_KeepsVerifiedSupportAndMidSpanStations()
    {
        var result = BeamSectionStationMath.Resolve([0, 1], 0.01);

        Assert.Equal(0.035, result.Support, 6);
        Assert.Equal(0.5, result.MidSpan, 6);
    }

    [Fact]
    public void ExistingSectionBoxMath_KeepsOneHundredFiftyMillimeterFarClip()
    {
        var halfDepth = BeamSectionBoxMath.HalfDepthFeet(BeamSectionBoxMath.CrossFarClipOffsetMm);

        Assert.Equal(75.0 / BeamSectionBoxMath.MillimetersPerFoot, halfDepth, 9);
    }

    [Fact]
    public void ExistingCrossTagLayout_KeepsMainTagsOutsideBeamBounds()
    {
        var positions = CrossTagLayout.TagYsFromBeamBounds(4, 2, 0);

        Assert.Equal(4, positions.Length);
        Assert.True(positions[0] > 2);
        Assert.True(positions[^1] < 0);
        Assert.True(positions.SequenceEqual(positions.OrderByDescending(value => value)));
    }
}
