using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadRailBuilderTests
{
    [Fact]
    public void Build_TwoParallelLines_GivesTwoRails()
    {
        var rails = CadRailBuilder.Build(new[]
        {
            Segment(1, 0, 0, 6000, 0),
            Segment(2, 0, 200, 6000, 200)
        }, gapToleranceMm: 300, offsetToleranceMm: 10);

        Assert.Equal(2, rails.Count);
        Assert.Equal(200.0, Math.Abs(rails[1].Offset - rails[0].Offset), 1);
    }

    [Fact]
    public void Build_OneBoundaryDrawnInPieces_StaysOneRail()
    {
        // A boundary is trimmed at every column face, so it reaches the reader in pieces.
        var rails = CadRailBuilder.Build(new[]
        {
            Segment(1, 0, 0, 2000, 0),
            Segment(2, 2400, 0, 5000, 0),
            Segment(3, 5400, 0, 8000, 0)
        }, gapToleranceMm: 500, offsetToleranceMm: 10);

        var rail = Assert.Single(rails);
        Assert.Equal(0.0, rail.Start, 1);
        Assert.Equal(8000.0, rail.End, 1);
    }

    [Fact]
    public void Build_PiecesDriftingAcrossTheLine_StayOneRail()
    {
        // Copying a bay or snapping to a nearby element leaves a millimetre or two of drift.
        var rails = CadRailBuilder.Build(new[]
        {
            Segment(1, 0, 0, 3000, 0),
            Segment(2, 3000, 3, 6000, 3),
            Segment(3, 6000, -2, 9000, -2)
        }, gapToleranceMm: 300, offsetToleranceMm: 10);

        Assert.Single(rails);
    }

    [Fact]
    public void Build_PiecesFurtherApartThanTheTolerance_StayApart()
    {
        var rails = CadRailBuilder.Build(new[]
        {
            Segment(1, 0, 0, 3000, 0),
            Segment(2, 0, 50, 3000, 50)
        }, gapToleranceMm: 300, offsetToleranceMm: 10);

        Assert.Equal(2, rails.Count);
    }

    [Fact]
    public void Build_ABoundaryDrawnBackwards_ReadsAsTheSameLine()
    {
        // A line and the same line drawn the other way round are one boundary.
        var rails = CadRailBuilder.Build(new[]
        {
            Segment(1, 0, 0, 3000, 0),
            Segment(2, 6000, 0, 3000, 0)
        }, gapToleranceMm: 300, offsetToleranceMm: 10);

        var rail = Assert.Single(rails);
        Assert.Equal(6000.0, rail.End - rail.Start, 1);
    }

    [Fact]
    public void Build_PiecesADegreeApart_ReadAsOneDirection()
    {
        // A drawing rarely repeats an angle exactly, and a degree of drift must not read as two
        // directions. Whether such pieces then join into one rail depends on how far apart they
        // sit across the line, which the offset tolerance decides separately.
        var rails = CadRailBuilder.Build(new[]
        {
            Segment(1, 0, 0, 4000, 0),
            Segment(2, 0, 0, 4000, 70)
        }, gapToleranceMm: 300, offsetToleranceMm: 100);

        Assert.Single(rails);
    }

    [Fact]
    public void Build_LinesAtRightAngles_AreDifferentRails()
    {
        var rails = CadRailBuilder.Build(new[]
        {
            Segment(1, 0, 0, 6000, 0),
            Segment(2, 0, 0, 0, 6000)
        }, gapToleranceMm: 300, offsetToleranceMm: 10);

        Assert.Equal(2, rails.Count);
    }

    [Fact]
    public void Build_ARailBrokenWiderThanTheGap_KeepsBothStretches()
    {
        var rails = CadRailBuilder.Build(new[]
        {
            Segment(1, 0, 0, 2000, 0),
            Segment(2, 5000, 0, 7000, 0)
        }, gapToleranceMm: 300, offsetToleranceMm: 10);

        var rail = Assert.Single(rails);
        Assert.Equal(2, rail.Intervals.Count);
        Assert.Equal(4000.0, rail.CoveredLength, 1);
    }

    [Fact]
    public void Build_KeepsTheSegmentsEachRailCameFrom()
    {
        var rails = CadRailBuilder.Build(new[]
        {
            Segment(7, 0, 0, 2000, 0),
            Segment(9, 2400, 0, 5000, 0)
        }, gapToleranceMm: 500, offsetToleranceMm: 10);

        var rail = Assert.Single(rails);
        Assert.Equal(new[] { 7, 9 }, rail.SourceIds.OrderBy(id => id));
    }

    [Theory]
    [InlineData(0, 0, 1000, 0, 0)]
    [InlineData(0, 0, 0, 1000, 90)]
    [InlineData(0, 0, 1000, 1000, 45)]
    // A line has no front or back, so the angle folds into a half turn.
    [InlineData(1000, 0, 0, 0, 0)]
    [InlineData(0, 1000, 0, 0, 90)]
    public void Angle_FoldsADirectionIntoAHalfTurn(
        double x1, double y1, double x2, double y2, double expected)
    {
        var direction = CadRailBuilder.Normalize(
            new CadStructurePoint2(x2 - x1, y2 - y1));

        Assert.Equal(expected, CadRailBuilder.Angle(direction), 1);
    }

    [Fact]
    public void MergeIntervals_JoinsStretchesWithinTheGap()
    {
        var merged = CadRailBuilder.MergeIntervals(new[]
        {
            new CadRailInterval(0, 1000),
            new CadRailInterval(1200, 2000),
            new CadRailInterval(5000, 6000)
        }, gap: 300);

        Assert.Equal(2, merged.Count);
        Assert.Equal(0.0, merged[0].Start, 1);
        Assert.Equal(2000.0, merged[0].End, 1);
    }

    [Fact]
    public void FacingIntervals_CoversWhatEitherRailReaches()
    {
        // A short stub facing a long run must not inherit the long run's extent, but a gap one
        // rail bridges keeps the pair continuous.
        var first = CadRailBuilder.Build(new[] { Segment(1, 0, 0, 8000, 0) },
            gapToleranceMm: 300, offsetToleranceMm: 10)[0];
        var second = CadRailBuilder.Build(new[] { Segment(2, 2000, 200, 4000, 200) },
            gapToleranceMm: 300, offsetToleranceMm: 10)[0];

        var facing = CadRailBuilder.FacingIntervals(first, second, gap: 300);

        var covered = facing.Sum(interval => interval.End - interval.Start);
        Assert.Equal(8000.0, covered, 1);
    }

    private static CadStructureSegment Segment(
        int id, double x1, double y1, double x2, double y2) =>
        new(id, new CadStructurePoint2(x1, y1), new CadStructurePoint2(x2, y2),
            "WALL", string.Empty);
}
