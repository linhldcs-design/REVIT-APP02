using RevitAPP.Core.Models.CadGrid;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadGridGeometryTests
{
    [Fact]
    public void Intersects_CrossingSegmentWithEndpointsOutside_ReturnsTrue()
    {
        var box = CadGridSelectionBox.FromCorners(Point(0, 0), Point(100, 100));
        var segment = Segment(1, -50, 50, 150, 50);

        Assert.True(CadGridGeometry.Intersects(segment, box));
    }

    [Fact]
    public void Intersects_SegmentOutside_ReturnsFalse()
    {
        var box = CadGridSelectionBox.FromCorners(Point(100, 100), Point(0, 0));
        var segment = Segment(1, -50, 150, 150, 150);

        Assert.False(CadGridGeometry.Intersects(segment, box));
    }

    [Fact]
    public void TryIntersectBounded_PerpendicularLines_ReturnsIntersection()
    {
        var first = Segment(1, 3000, 0, 3000, 10000);
        var second = Segment(2, 0, 4500, 10000, 4500);

        var success = CadGridGeometry.TryIntersectBounded(first, second, 1, out var point);

        Assert.True(success);
        Assert.Equal(3000, point.Xmm, 6);
        Assert.Equal(4500, point.Ymm, 6);
    }

    [Fact]
    public void AreSameInfiniteLine_ReversedEndpointsWithinTolerance_ReturnsTrue()
    {
        var first = Segment(1, 0, 0, 10000, 0);
        var second = Segment(2, 9000, 0.5, 1000, 0.5);

        Assert.True(CadGridGeometry.AreSameInfiniteLine(
            first,
            second,
            distanceToleranceMm: 1,
            angleToleranceRadians: Math.PI / 1800));
    }

    [Fact]
    public void AreSameInfiniteLine_LongLinesWithinAngleTolerance_ReturnsTrueAtSharedCenter()
    {
        var halfLength = 5000d;
        var angle = 0.05 * Math.PI / 180d;
        var first = Segment(1, -halfLength, 0, halfLength, 0);
        var second = Segment(
            2,
            -Math.Cos(angle) * halfLength,
            -Math.Sin(angle) * halfLength,
            Math.Cos(angle) * halfLength,
            Math.Sin(angle) * halfLength);

        Assert.True(CadGridGeometry.AreSameInfiniteLine(
            first,
            second,
            distanceToleranceMm: 1,
            angleToleranceRadians: 0.1 * Math.PI / 180d));
    }

    [Fact]
    public void Analyze_RectangularNetwork_ReturnsOrderedSpacingChains()
    {
        var segments = new[]
        {
            Segment(1, 0, 0, 0, 12000),
            Segment(2, 3000, 0, 3000, 12000),
            Segment(3, 7500, 0, 7500, 12000),
            Segment(10, 0, 0, 7500, 0),
            Segment(11, 0, 4000, 7500, 4000),
            Segment(12, 0, 8000, 7500, 8000),
            Segment(13, 0, 12000, 7500, 12000)
        };

        var result = CadGridNetworkAnalyzer.Analyze(segments);

        Assert.True(result.IsValid, result.Error);
        var network = Assert.IsType<CadGridNetwork>(result.Network);
        var verticalFamily = FamilyContaining(network, 1);
        var horizontalFamily = FamilyContaining(network, 10);
        Assert.Equal(new[] { 1, 2, 3 }, verticalFamily.OrderedSegmentIds);
        Assert.Collection(
            verticalFamily.ConsecutiveDistancesMm,
            distance => Assert.Equal(3000d, distance, 6),
            distance => Assert.Equal(4500d, distance, 6));
        Assert.Equal(new[] { 10, 11, 12, 13 }, horizontalFamily.OrderedSegmentIds);
        Assert.All(
            horizontalFamily.ConsecutiveDistancesMm,
            distance => Assert.Equal(4000d, distance, 6));
        Assert.Equal(12, network.Intersections.Count);
    }

    [Fact]
    public void Analyze_SkewedNetwork_ComputesPerpendicularAxisDistances()
    {
        var segments = new[]
        {
            Segment(1, 0, 0, 3000, 10000),
            Segment(2, 4000, 0, 7000, 10000),
            Segment(10, 0, 1000, 8000, 1000),
            Segment(11, 0, 7000, 8000, 7000)
        };

        var result = CadGridNetworkAnalyzer.Analyze(segments);

        Assert.True(result.IsValid, result.Error);
        var network = Assert.IsType<CadGridNetwork>(result.Network);
        Assert.Equal(4, network.Intersections.Count);
        var skewedFamily = FamilyContaining(network, 1);
        var horizontalFamily = FamilyContaining(network, 10);
        var expectedSkewedAxisSpacing = 4000d * 10000d / Math.Sqrt(3000d * 3000d + 10000d * 10000d);
        Assert.Collection(
            skewedFamily.ConsecutiveDistancesMm,
            distance => Assert.Equal(expectedSkewedAxisSpacing, distance, 6));
        Assert.Collection(
            horizontalFamily.ConsecutiveDistancesMm,
            distance => Assert.Equal(6000d, distance, 6));
    }

    [Fact]
    public void Analyze_IsolatedParallelLine_ReportsItAsSkipped()
    {
        var segments = new[]
        {
            Segment(1, 0, 0, 0, 10000),
            Segment(2, 3000, 0, 3000, 10000),
            Segment(3, 6000, 20000, 6000, 25000),
            Segment(10, 0, 1000, 4000, 1000),
            Segment(11, 0, 7000, 4000, 7000)
        };

        var result = CadGridNetworkAnalyzer.Analyze(segments);

        Assert.True(result.IsValid, result.Error);
        Assert.Contains(3, Assert.IsType<CadGridNetwork>(result.Network).SkippedSegmentIds);
    }

    [Fact]
    public void Analyze_IsolatedThirdDirectionLine_ReportsItAsSkipped()
    {
        var segments = new[]
        {
            Segment(1, 0, 0, 0, 10000),
            Segment(2, 3000, 0, 3000, 10000),
            Segment(10, 0, 1000, 4000, 1000),
            Segment(11, 0, 7000, 4000, 7000),
            Segment(20, 20000, 20000, 25000, 25000)
        };

        var result = CadGridNetworkAnalyzer.Analyze(segments);

        Assert.True(result.IsValid, result.Error);
        Assert.Contains(20, Assert.IsType<CadGridNetwork>(result.Network).SkippedSegmentIds);
    }

    [Fact]
    public void Analyze_ThirdDirectionCrossingNetwork_IsRejected()
    {
        var segments = new[]
        {
            Segment(1, 0, 0, 0, 10000),
            Segment(2, 3000, 0, 3000, 10000),
            Segment(10, 0, 1000, 4000, 1000),
            Segment(11, 0, 7000, 4000, 7000),
            Segment(20, -1000, 0, 4000, 5000)
        };

        var result = CadGridNetworkAnalyzer.Analyze(segments);

        Assert.False(result.IsValid);
        Assert.Contains("họ line thứ ba", result.Error);
    }

    [Fact]
    public void Analyze_TrimmedNetwork_UsesReferenceWithMostIntersections()
    {
        var segments = new[]
        {
            Segment(1, 0, 0, 0, 10000),
            Segment(2, 3000, 0, 3000, 10000),
            Segment(3, 6000, 0, 6000, 10000),
            Segment(10, 0, 1000, 3000, 1000),
            Segment(11, 3000, 7000, 6000, 7000)
        };

        var result = CadGridNetworkAnalyzer.Analyze(segments);

        Assert.True(result.IsValid, result.Error);
        var network = Assert.IsType<CadGridNetwork>(result.Network);
        Assert.Equal(5, network.FirstFamily.OrderedSegmentIds
            .Concat(network.SecondFamily.OrderedSegmentIds)
            .Distinct()
            .Count());
        Assert.Empty(network.SkippedSegmentIds);
    }

    [Fact]
    public void Analyze_ThreeDirectionFamilies_IsRejected()
    {
        var segments = new[]
        {
            Segment(1, 0, 0, 0, 10000),
            Segment(2, 0, 0, 10000, 0),
            Segment(3, 0, 0, 10000, 10000)
        };

        var result = CadGridNetworkAnalyzer.Analyze(segments);

        Assert.False(result.IsValid);
        Assert.Contains("hai phương", result.Error);
    }

    [Fact]
    public void Analyze_SpacingChangesAcrossReferenceLines_IsRejected()
    {
        var height = 10000d;
        var drift = Math.Tan(0.04 * Math.PI / 180d) * height;
        var segments = new[]
        {
            Segment(1, 0, 0, 0, height),
            Segment(2, 3000, 0, 3000 + drift, height),
            Segment(3, 6000, 0, 6000 - drift, height),
            Segment(10, 0, 0, 6000, 0),
            Segment(11, 0, height, 6000, height)
        };

        var result = CadGridNetworkAnalyzer.Analyze(segments);

        Assert.False(result.IsValid);
        Assert.Contains("line chuẩn", result.Error);
    }

    private static CadGridNetworkFamily FamilyContaining(CadGridNetwork network, int segmentId) =>
        network.FirstFamily.OrderedSegmentIds.Contains(segmentId)
            ? network.FirstFamily
            : network.SecondFamily;

    private static CadGridPoint2 Point(double x, double y) => new(x, y);

    private static CadGridSegment2 Segment(
        int id,
        double startX,
        double startY,
        double endX,
        double endY) =>
        new(id, Point(startX, startY), Point(endX, endY));
}
