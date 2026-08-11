using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadWallAnalyzerTests
{
    private const string WallLayer = "A-WALL";
    private const string BeamLayer = "NT2-NET DAM 0.4";

    [Fact]
    public void Analyze_TwoParallelLines_GiveOneWallDownTheMiddle()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 6000, 0),
            Segment(2, 0, 200, 6000, 200)
        });

        var wall = Assert.Single(result.Walls);
        Assert.Equal(200.0, wall.ThicknessMm, 1);
        Assert.Equal(6000.0, wall.LengthMm, 1);
        // The wall runs down the middle of the pair, not along either face.
        Assert.Equal(100.0, wall.StartMm.Y, 1);
        Assert.Equal(100.0, wall.EndMm.Y, 1);
    }

    [Fact]
    public void Analyze_ARectangle_GivesOneWallAlongItsLength()
    {
        var result = Analyze(Rectangle(10, 0, 0, 6000, 200));

        var wall = Assert.Single(result.Walls);
        Assert.Equal(200.0, wall.ThicknessMm, 1);
        Assert.Equal(6000.0, wall.LengthMm, 1);
        Assert.Equal(CadWallSource.Rectangle, wall.Source);
    }

    [Fact]
    public void Analyze_ASquareRectangle_IsAColumnAndIsLeftAlone()
    {
        var result = Analyze(Rectangle(10, 0, 0, 400, 400));

        Assert.Empty(result.Walls);
    }

    [Fact]
    public void Analyze_ARectangleTooStubbyToBeAWall_IsLeftAlone()
    {
        // 300 x 600 is twice as long as it is wide, which reads as a column rather than a wall.
        var result = Analyze(Rectangle(10, 0, 0, 600, 300));

        Assert.Empty(result.Walls);
    }

    [Fact]
    public void Analyze_ARotatedRectangle_MeasuresItsShortSide()
    {
        // The same wall drawn at an angle: 200 thick, 4000 long, turned 30 degrees.
        var angle = 30.0 * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        CadStructurePoint2 Turn(double x, double y) =>
            new(x * cos - y * sin, x * sin + y * cos);

        var corners = new[] { Turn(0, 0), Turn(4000, 0), Turn(4000, 200), Turn(0, 200) };
        var segments = new List<CadStructureSegment>();
        for (var index = 0; index < corners.Length; index++)
            segments.Add(new CadStructureSegment(index + 1, corners[index],
                corners[(index + 1) % corners.Length], WallLayer, string.Empty));

        var wall = Assert.Single(Analyze(segments).Walls);
        Assert.Equal(200.0, wall.ThicknessMm, 1);
        Assert.Equal(4000.0, wall.LengthMm, 1);
    }

    [Fact]
    public void Analyze_ThicknessOutsideWhatTheUserAllows_IsLeftAlone()
    {
        // Two lines a metre apart are two different things, not one very thick wall.
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 6000, 0),
            Segment(2, 0, 1000, 6000, 1000)
        });

        Assert.Empty(result.Walls);
    }

    [Fact]
    public void Analyze_ALineWithNothingParallelToIt_IsLeftAlone()
    {
        var result = Analyze(new[] { Segment(1, 0, 0, 6000, 0) });

        Assert.Empty(result.Walls);
    }

    [Fact]
    public void Analyze_BoundariesDriftingApart_StillGiveOneWall()
    {
        // Trimming and snapping leave a few millimetres of drift along a drawn boundary.
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 3000, 0),
            Segment(2, 3000, 3, 6000, 3),
            Segment(3, 0, 200, 6000, 200)
        });

        var wall = Assert.Single(result.Walls);
        Assert.Equal(6000.0, wall.LengthMm, tolerance: 20.0);
    }

    [Fact]
    public void Analyze_ABoundaryTrimmedAtAColumn_StillGivesTheWholeWall()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 2800, 0),
            Segment(2, 3000, 0, 6000, 0),
            Segment(3, 0, 200, 6000, 200)
        });

        var wall = Assert.Single(result.Walls);
        Assert.Equal(6000.0, wall.LengthMm, tolerance: 20.0);
    }

    [Fact]
    public void Analyze_LayersTheUserDidNotPick_AreLeftAlone()
    {
        // A beam is drawn exactly like a wall, so only the layer tells them apart.
        var segments = new List<CadStructureSegment>
        {
            Segment(1, 0, 0, 6000, 0),
            Segment(2, 0, 200, 6000, 200),
            new(3, new CadStructurePoint2(0, 3000), new CadStructurePoint2(6000, 3000),
                BeamLayer, string.Empty),
            new(4, new CadStructurePoint2(0, 3200), new CadStructurePoint2(6000, 3200),
                BeamLayer, string.Empty)
        };

        var walls = Analyze(segments).Walls;

        Assert.Single(walls);
        Assert.Equal(100.0, walls[0].StartMm.Y, 1);
    }

    [Fact]
    public void Analyze_ParallelBoundariesOnDifferentLayers_DoNotPairIntoAWall()
    {
        // Two layers can both draw walls without a face of one ever belonging to the other, and
        // a face of each can run parallel at exactly a wall's thickness by coincidence.
        const string other = "A-WALL-INT";
        var result = CadWallAnalyzer.Analyze(
            Package(new List<CadStructureSegment>
            {
                Segment(1, 0, 0, 6000, 0),
                new(2, new CadStructurePoint2(0, 200), new CadStructurePoint2(6000, 200),
                    other, string.Empty)
            }),
            new CadWallAnalysisOptions { WallLayers = new[] { WallLayer, other } });

        Assert.Empty(result.Walls);
    }

    [Fact]
    public void Analyze_EachLayerPairsWithinItself()
    {
        const string other = "A-WALL-INT";
        var result = CadWallAnalyzer.Analyze(
            Package(new List<CadStructureSegment>
            {
                Segment(1, 0, 0, 6000, 0),
                Segment(2, 0, 200, 6000, 200),
                new(3, new CadStructurePoint2(0, 3000), new CadStructurePoint2(6000, 3000),
                    other, string.Empty),
                new(4, new CadStructurePoint2(0, 3100), new CadStructurePoint2(6000, 3100),
                    other, string.Empty)
            }),
            new CadWallAnalysisOptions { WallLayers = new[] { WallLayer, other } });

        Assert.Equal(2, result.Walls.Count);
        Assert.Contains(result.Walls, wall => Math.Abs(wall.ThicknessMm - 200) < 1);
        Assert.Contains(result.Walls, wall => Math.Abs(wall.ThicknessMm - 100) < 1);
    }

    [Fact]
    public void Analyze_WithNoLayerPicked_ReadsNoWallAndSaysSo()
    {
        var result = CadWallAnalyzer.Analyze(
            Package(new[]
            {
                Segment(1, 0, 0, 6000, 0),
                Segment(2, 0, 200, 6000, 200)
            }),
            new CadWallAnalysisOptions());

        Assert.Empty(result.Walls);
        Assert.Contains(result.Warnings, warning => warning.Contains("layer"));
    }

    [Fact]
    public void Analyze_ReportsEveryLayerItSawAndHowMuchIsOnIt()
    {
        var result = CadWallAnalyzer.Analyze(
            Package(new List<CadStructureSegment>
            {
                Segment(1, 0, 0, 6000, 0),
                Segment(2, 0, 200, 6000, 200),
                new(3, new CadStructurePoint2(0, 3000), new CadStructurePoint2(6000, 3000),
                    BeamLayer, string.Empty)
            }),
            new CadWallAnalysisOptions());

        Assert.Equal(2, result.Layers.Count);
        Assert.Equal(2, result.Layers.Single(layer => layer.Layer == WallLayer).SegmentCount);
        // A layer named for walls is ticked for the user; one named for beams is not.
        Assert.True(result.Layers.Single(layer => layer.Layer == WallLayer).SuggestedAsWall);
        Assert.False(result.Layers.Single(layer => layer.Layer == BeamLayer).SuggestedAsWall);
    }

    [Fact]
    public void Analyze_FourWallsRoundARoom_MeetAtEveryCorner()
    {
        // A room 6000 x 4000 inside, walls 200 thick, drawn as inner and outer faces.
        var segments = new List<CadStructureSegment>();
        var id = 1;
        void Line(double x1, double y1, double x2, double y2) =>
            segments.Add(Segment(id++, x1, y1, x2, y2));

        // outer face
        Line(0, 0, 6400, 0);
        Line(6400, 0, 6400, 4400);
        Line(6400, 4400, 0, 4400);
        Line(0, 4400, 0, 0);
        // inner face
        Line(200, 200, 6200, 200);
        Line(6200, 200, 6200, 4200);
        Line(6200, 4200, 200, 4200);
        Line(200, 4200, 200, 200);

        var walls = Analyze(segments).Walls;

        Assert.Equal(4, walls.Count);
        Assert.All(walls, wall => Assert.Equal(200.0, wall.ThicknessMm, 1));

        // Every wall end has to meet another wall's end, or the room comes out with open corners.
        foreach (var wall in walls)
        foreach (var end in new[] { wall.StartMm, wall.EndMm })
            Assert.Contains(walls.Where(other => other.Id != wall.Id), other =>
                other.StartMm.DistanceTo(end) < 1.0 || other.EndMm.DistanceTo(end) < 1.0);
    }

    [Fact]
    public void Analyze_AWallShorterThanTheMinimum_IsLeftAlone()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 250, 0),
            Segment(2, 0, 200, 250, 200)
        });

        Assert.Empty(result.Walls);
    }

    private static CadWallAnalysis Analyze(IReadOnlyList<CadStructureSegment> segments) =>
        CadWallAnalyzer.Analyze(
            Package(segments),
            new CadWallAnalysisOptions { WallLayers = new[] { WallLayer } });

    private static CadStructureTransferPackage Package(IReadOnlyList<CadStructureSegment> segments) =>
        new(CadStructureTransferPackage.CurrentSchemaVersion, "wall-test", DateTime.UtcNow,
            "wall.dwg", "2025", 4, default, segments);

    private static List<CadStructureSegment> Rectangle(
        int firstId, double x1, double y1, double x2, double y2) =>
        new()
        {
            Segment(firstId, x1, y1, x2, y1),
            Segment(firstId + 1, x2, y1, x2, y2),
            Segment(firstId + 2, x2, y2, x1, y2),
            Segment(firstId + 3, x1, y2, x1, y1)
        };

    private static CadStructureSegment Segment(
        int id, double x1, double y1, double x2, double y2) =>
        new(id, new CadStructurePoint2(x1, y1), new CadStructurePoint2(x2, y2),
            WallLayer, string.Empty);
}
