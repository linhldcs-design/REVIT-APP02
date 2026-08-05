using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadStructureAnalyzerTests
{
    [Fact]
    public void Analyze_FourLinesAndGrids_DetectsColumnAndLeavesGrids()
    {
        var package = Package(
            Segment(1, 0, 0, 0, 10000, "GRID"),
            Segment(2, 5000, 0, 5000, 10000, "GRID"),
            Segment(10, 2350, 3850, 2650, 3850, "COL"),
            Segment(11, 2650, 3850, 2650, 4150, "COL"),
            Segment(12, 2650, 4150, 2350, 4150, "COL"),
            Segment(13, 2350, 4150, 2350, 3850, "COL"));

        var result = CadStructureAnalyzer.Analyze(package);

        Assert.True(result.IsValid, result.Error);
        var column = Assert.Single(result.Columns);
        Assert.Equal(2500, column.CenterMm.X, 6);
        Assert.Equal(4000, column.CenterMm.Y, 6);
        Assert.Equal(300, column.WidthMm, 6);
        Assert.Equal(300, column.HeightMm, 6);
        Assert.Equal(2, result.GridSegmentsMm.Count);
    }

    [Fact]
    public void Analyze_RotatedRectangleFromBlock_PreservesCenterSizeAngleAndPath()
    {
        const double angle = 30.0;
        var corners = Rectangle(6000, 3000, 300, 500, angle);
        var segments = Enumerable.Range(0, 4)
            .Select(index => new CadStructureSegment(
                index + 1,
                corners[index],
                corners[(index + 1) % 4],
                "S-COLS",
                "COLUMN_DYNAMIC/NESTED"))
            .ToArray();

        var result = CadStructureAnalyzer.Analyze(Package(segments));

        var column = Assert.Single(result.Columns);
        Assert.Equal(300, column.WidthMm, 3);
        Assert.Equal(500, column.HeightMm, 3);
        Assert.Equal(angle, column.AngleDegrees, 3);
        Assert.Equal("COLUMN_DYNAMIC/NESTED", column.SourcePath);
    }

    [Fact]
    public void Analyze_LargeGridCell_IsNotDetectedAsColumn()
    {
        var result = CadStructureAnalyzer.Analyze(Package(
            Segment(1, 0, 0, 6000, 0),
            Segment(2, 6000, 0, 6000, 7000),
            Segment(3, 6000, 7000, 0, 7000),
            Segment(4, 0, 7000, 0, 0)));

        Assert.Empty(result.Columns);
        Assert.Equal(4, result.GridSegmentsMm.Count);
    }

    [Fact]
    public void Analyze_SourceAnchor_IsRelativeToPreviewOrigin()
    {
        var package = Package(
            new CadStructurePoint2(101000, 202000),
            Segment(1, 100000, 200000, 100000, 210000),
            Segment(2, 110000, 200000, 110000, 210000));

        var result = CadStructureAnalyzer.Analyze(package);

        Assert.Equal(1000, result.SourceAnchorRelativeMm.X, 6);
        Assert.Equal(2000, result.SourceAnchorRelativeMm.Y, 6);
    }

    [Fact]
    public void Analyze_RectangleEdgesAcrossHashBoundary_StillConnectsWithinTolerance()
    {
        var result = CadStructureAnalyzer.Analyze(Package(
            Segment(1, 0.49, 0.49, 300.49, 0.49, "COL"),
            Segment(2, 300.51, 0.51, 300.51, 300.51, "COL"),
            Segment(3, 300.49, 300.49, 0.49, 300.49, "COL"),
            Segment(4, 0.51, 300.51, 0.51, 0.51, "COL")));

        Assert.Single(result.Columns);
    }

    [Fact]
    public void Analyze_EdgesFromDifferentLayersOrBlockInstances_AreNotCombined()
    {
        var result = CadStructureAnalyzer.Analyze(Package(
            Segment(1, 0, 0, 300, 0, "COL-A", "BLOCK@1"),
            Segment(2, 300, 0, 300, 300, "COL-A", "BLOCK@1"),
            Segment(3, 300, 300, 0, 300, "COL-B", "BLOCK@2"),
            Segment(4, 0, 300, 0, 0, "COL-B", "BLOCK@2")));

        Assert.Empty(result.Columns);
        Assert.Equal(4, result.GridSegmentsMm.Count);
    }

    [Fact]
    public void Analyze_DuplicateRectangleEdges_ConsumesEveryCoincidentSegment()
    {
        var result = CadStructureAnalyzer.Analyze(Package(
            Segment(1, 0, 0, 300, 0, "COL"),
            Segment(2, 300, 0, 300, 500, "COL"),
            Segment(3, 300, 500, 0, 500, "COL"),
            Segment(4, 0, 500, 0, 0, "COL"),
            Segment(5, 300, 0, 0, 0, "COL", "COLUMN/PL@B"),
            Segment(6, 300, 500, 300, 0, "COL", "COLUMN/PL@B"),
            Segment(7, 0, 500, 300, 500, "COL", "COLUMN/PL@B"),
            Segment(8, 0, 0, 0, 500, "COL", "COLUMN/PL@B")));

        var column = Assert.Single(result.Columns);
        Assert.Equal(8, column.SourceSegmentIds.Count);
        Assert.Empty(result.GridSegmentsMm);
    }

    [Fact]
    public void Analyze_ReorderedRectangleAtFortyFiveDegrees_KeepsWidthAndAngleStable()
    {
        var corners = Rectangle(1000, 2000, 500, 300, 45);
        var forward = Enumerable.Range(0, 4)
            .Select(index => new CadStructureSegment(index + 1, corners[index],
                corners[(index + 1) % 4], "COL", "BLOCK@1"))
            .ToArray();
        var reordered = new[]
        {
            new CadStructureSegment(11, corners[2], corners[1], "COL", "BLOCK@1"),
            new CadStructureSegment(12, corners[1], corners[0], "COL", "BLOCK@1"),
            new CadStructureSegment(13, corners[0], corners[3], "COL", "BLOCK@1"),
            new CadStructureSegment(14, corners[3], corners[2], "COL", "BLOCK@1")
        };

        var first = Assert.Single(CadStructureAnalyzer.Analyze(Package(forward)).Columns);
        var second = Assert.Single(CadStructureAnalyzer.Analyze(Package(reordered)).Columns);

        Assert.Equal(500, first.WidthMm, 3);
        Assert.Equal(first.WidthMm, second.WidthMm, 6);
        Assert.Equal(first.HeightMm, second.HeightMm, 6);
        Assert.Equal(first.AngleDegrees, second.AngleDegrees, 6);
    }

    private static CadStructurePoint2[] Rectangle(
        double centerX, double centerY, double width, double height, double angleDegrees)
    {
        var angle = angleDegrees * Math.PI / 180.0;
        var x = new CadStructurePoint2(Math.Cos(angle), Math.Sin(angle));
        var y = new CadStructurePoint2(-Math.Sin(angle), Math.Cos(angle));
        var center = new CadStructurePoint2(centerX, centerY);
        return new[]
        {
            center - x * (width / 2) - y * (height / 2),
            center + x * (width / 2) - y * (height / 2),
            center + x * (width / 2) + y * (height / 2),
            center - x * (width / 2) + y * (height / 2)
        };
    }

    private static CadStructureSegment Segment(
        int id, double x1, double y1, double x2, double y2,
        string layer = "0", string source = "") =>
        new(id, new CadStructurePoint2(x1, y1), new CadStructurePoint2(x2, y2), layer, source);

    private static CadStructureTransferPackage Package(params CadStructureSegment[] segments) =>
        Package(new CadStructurePoint2(0, 0), segments);

    private static CadStructureTransferPackage Package(
        CadStructurePoint2 anchor,
        params CadStructureSegment[] segments) =>
        new(CadStructureTransferPackage.CurrentSchemaVersion, "test", DateTime.UtcNow,
            "sample.dwg", "2025", 4, anchor, segments);
}
