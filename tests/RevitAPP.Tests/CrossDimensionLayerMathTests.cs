using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CrossDimensionLayerMathTests
{
    [Fact]
    public void OrderedUniqueLevels_DepressedSlab_ReturnsThreeChainSegments()
    {
        var levels = CrossDimensionLayerMath.OrderedUniqueLevels([0, 280, 400, 450], 2);

        Assert.Equal([0, 280, 400, 450], levels);
    }

    [Fact]
    public void OrderedUniqueLevels_AlignedSlab_MergesCoincidentBeamAndSlabTop()
    {
        var levels = CrossDimensionLayerMath.OrderedUniqueLevels([0, 330, 330.5, 450], 2);

        Assert.Equal([0, 330, 450], levels);
    }

    [Fact]
    public void OrderedUniqueLevels_BeamTopEqualsSlabTop_PreservesOldTwoSegmentRule()
    {
        // Beam 450; slab thickness 100 nằm trong đầu dầm: 350 phần dầm + 100 phần sàn.
        var levels = CrossDimensionLayerMath.OrderedUniqueLevels([0, 350, 450, 450], 2);

        Assert.Equal([0, 350, 450], levels);
    }

    [Fact]
    public void OrderedUniqueLevels_GapBelowDisplayPrecision_DoesNotCreateZeroSegment()
    {
        var levels = CrossDimensionLayerMath.OrderedUniqueLevels(
            [0, 330, 444, 450], CrossDimensionLayerMath.CoincidentToleranceMm);

        Assert.Equal([0, 330, 450], levels);
    }

    [Fact]
    public void OrderedUniqueLevels_RealFiftyMillimeterDrop_RemainsSeparateSegment()
    {
        var levels = CrossDimensionLayerMath.OrderedUniqueLevels(
            [0, 280, 400, 450], CrossDimensionLayerMath.CoincidentToleranceMm);

        Assert.Equal([0, 280, 400, 450], levels);
    }

    [Fact]
    public void OrderedUniqueLevels_SlabAboveBeam_KeepsOuterTopForOverallDimension()
    {
        var levels = CrossDimensionLayerMath.OrderedUniqueLevels([0, 350, 350, 450], 2);

        Assert.Equal([0, 350, 450], levels);
    }

    [Fact]
    public void OrderedUniqueLevels_NoSlab_ReturnsBeamBottomAndTop()
    {
        var levels = CrossDimensionLayerMath.OrderedUniqueLevels([0, 450], 2);

        Assert.Equal([0, 450], levels);
    }

    [Theory]
    [InlineData(4, 0, 1)] // zero at bottom: preserve outer bottom
    [InlineData(4, 1, 2)] // zero inside: remove the upper interior face
    [InlineData(4, 2, 2)] // zero at top: preserve outer top
    public void ReferenceIndexToRemoveForZeroSegment_PreservesOuterEnvelope(
        int referenceCount, int zeroSegmentIndex, int expectedReferenceIndex)
    {
        Assert.Equal(expectedReferenceIndex,
            CrossDimensionLayerMath.ReferenceIndexToRemoveForZeroSegment(referenceCount, zeroSegmentIndex));
    }
}
