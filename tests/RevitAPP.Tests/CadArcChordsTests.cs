using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadArcChordsTests
{
    private static readonly CadStructurePoint2 Start = new(0, 0);
    private static readonly CadStructurePoint2 End = new(4000, 0);

    [Theory]
    // A bulge is the tangent of a quarter of the angle the arc sweeps, so 1 is a half circle,
    // tan(22.5 degrees) a quarter, and a small bulge a shallow arc. The apex of the arc rises
    // above the middle of the chord by bulge x half the chord, on the left of the chord when the
    // bulge is positive.
    [InlineData(1.0, 2000.0)]
    [InlineData(0.41421356, 828.4)]
    [InlineData(0.2, 400.0)]
    [InlineData(0.05, 100.0)]
    [InlineData(-1.0, -2000.0)]
    [InlineData(-0.41421356, -828.4)]
    [InlineData(-0.2, -400.0)]
    [InlineData(-0.05, -100.0)]
    public void Trace_PutsTheApexWhereTheBulgeSaysItGoes(double bulge, double expectedApexY)
    {
        var points = CadArcChords.Trace(Start, End, bulge, maximumStepDegrees: 1.0);

        var apex = points.MaxBy(point => Math.Abs(point.Y));
        Assert.Equal(expectedApexY, apex.Y, Math.Abs(expectedApexY) * 0.02);
        Assert.Equal(2000.0, apex.X, 1.0);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.41421356)]
    [InlineData(0.05)]
    [InlineData(3.0)]
    [InlineData(-1.0)]
    [InlineData(-0.41421356)]
    [InlineData(-0.05)]
    [InlineData(-3.0)]
    public void Trace_StartsAndEndsOnTheSideItReplaces(double bulge)
    {
        var points = CadArcChords.Trace(Start, End, bulge);

        Assert.Equal(0.0, points[0].DistanceTo(Start), 3);
        Assert.Equal(0.0, points[^1].DistanceTo(End), 3);
    }

    [Theory]
    // The arc length of a circle is radius x sweep. A half circle on a 4000 chord has radius
    // 2000, so it measures 2000 x pi; a quarter circle has radius 2828 and measures 2828 x pi/2.
    [InlineData(1.0, 6283.2)]
    [InlineData(0.41421356, 4442.9)]
    [InlineData(-1.0, 6283.2)]
    [InlineData(-0.41421356, 4442.9)]
    public void Trace_FollowsTheArcRatherThanCuttingAcrossIt(double bulge, double expectedLength)
    {
        var points = CadArcChords.Trace(Start, End, bulge, maximumStepDegrees: 1.0);

        var length = 0.0;
        for (var index = 1; index < points.Count; index++)
            length += points[index - 1].DistanceTo(points[index]);
        Assert.Equal(expectedLength, length, expectedLength * 0.01);
    }

    [Theory]
    [InlineData(1.0, 2000.0)]
    [InlineData(0.41421356, 2828.4)]
    [InlineData(0.2, 5200.0)]
    [InlineData(-1.0, 2000.0)]
    [InlineData(-0.2, 5200.0)]
    public void Trace_KeepsEveryPointOnOneCircle(double bulge, double expectedRadius)
    {
        var points = CadArcChords.Trace(Start, End, bulge, maximumStepDegrees: 1.0);

        // Three points settle a circle; every other point has to sit on it too, or the chain is
        // not following an arc at all.
        var centre = CentreThrough(points[0], points[points.Count / 2], points[^1]);
        foreach (var point in points)
            Assert.Equal(expectedRadius, centre.DistanceTo(point), expectedRadius * 0.01);
    }

    [Fact]
    public void Trace_WithoutABulge_GivesBackTheStraightSide()
    {
        var points = CadArcChords.Trace(Start, End, 0.0);

        Assert.Equal(2, points.Count);
        Assert.Equal(0.0, points[0].DistanceTo(Start), 3);
        Assert.Equal(0.0, points[1].DistanceTo(End), 3);
    }

    [Fact]
    public void Trace_OnASlopedSide_PutsTheApexOffTheChordsLeft()
    {
        // The same half circle, drawn on a side running up and to the right. Its apex has to sit
        // to the left of that side, not above the drawing.
        var start = new CadStructurePoint2(0, 0);
        var end = new CadStructurePoint2(3000, 3000);

        var points = CadArcChords.Trace(start, end, 1.0, maximumStepDegrees: 1.0);

        var chordLength = start.DistanceTo(end);
        var apex = points.MaxBy(point =>
            Math.Abs((point.X - start.X) * (end.Y - start.Y) - (point.Y - start.Y) * (end.X - start.X)));
        var side = ((apex.X - start.X) * (end.Y - start.Y) - (apex.Y - start.Y) * (end.X - start.X))
                   / chordLength;
        Assert.Equal(-chordLength / 2.0, side, chordLength * 0.02);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(0.2)]
    public void Trace_TakesMoreStepsWhenAskedForFinerChords(double bulge)
    {
        var coarse = CadArcChords.Trace(Start, End, bulge, maximumStepDegrees: 30.0);
        var fine = CadArcChords.Trace(Start, End, bulge, maximumStepDegrees: 2.0);

        Assert.True(fine.Count > coarse.Count);
    }

    private static CadStructurePoint2 CentreThrough(
        CadStructurePoint2 first,
        CadStructurePoint2 second,
        CadStructurePoint2 third)
    {
        var ax = first.X;
        var ay = first.Y;
        var bx = second.X;
        var by = second.Y;
        var cx = third.X;
        var cy = third.Y;
        var d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        var ux = ((ax * ax + ay * ay) * (by - cy)
                  + (bx * bx + by * by) * (cy - ay)
                  + (cx * cx + cy * cy) * (ay - by)) / d;
        var uy = ((ax * ax + ay * ay) * (cx - bx)
                  + (bx * bx + by * by) * (ax - cx)
                  + (cx * cx + cy * cy) * (bx - ax)) / d;
        return new CadStructurePoint2(ux, uy);
    }
}
