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
    public void Analyze_AWallBrokenAtADoor_WithTwoBridgeLines_StaysContinuous()
    {
        // A door interrupts both wall faces by more than GapJoinTolerance. Carrying both faces
        // through the opening says this is one wall, not two unrelated collinear walls.
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 0, 2000),
            Segment(2, 0, 3000, 0, 6000),
            Segment(3, 200, 0, 200, 2000),
            Segment(4, 200, 3000, 200, 6000),
            Segment(5, 0, 2000, 0, 3000),
            Segment(6, 200, 2000, 200, 3000)
        });

        var wall = Assert.Single(result.Walls);
        Assert.Equal(200.0, wall.ThicknessMm, 1);
        Assert.Equal(6000.0, wall.LengthMm, 1);
        Assert.Equal(100.0, wall.StartMm.X, 1);
        Assert.Equal(100.0, wall.EndMm.X, 1);
    }

    [Fact]
    public void Analyze_DoorBridgeLinesSplitIntoPieces_StillBridgeTheWall()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 0, 2000),
            Segment(2, 0, 3000, 0, 6000),
            Segment(3, 200, 0, 200, 2000),
            Segment(4, 200, 3000, 200, 6000),
            Segment(5, 0, 2000, 0, 2500),
            Segment(6, 0, 2500, 0, 3000),
            Segment(7, 200, 2000, 200, 2500),
            Segment(8, 200, 2500, 200, 3000)
        });

        var wall = Assert.Single(result.Walls);
        Assert.Equal(6000.0, wall.LengthMm, 1);
    }

    [Fact]
    public void Analyze_CappedWallPiecesAroundADoor_AreConsolidatedIntoOneWall()
    {
        // This is the shape seen in real plans: each solid piece is closed by an end cap or a
        // door jamb. Closed-rectangle recognition must not leave the two pieces as separate walls.
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 0, 2000),
            Segment(2, 0, 3000, 0, 6000),
            Segment(3, 200, 0, 200, 2000),
            Segment(4, 200, 3000, 200, 6000),
            Segment(5, 0, 2000, 200, 2000),
            Segment(6, 0, 3000, 200, 3000),
            Segment(7, 0, 0, 200, 0),
            Segment(8, 0, 6000, 200, 6000),
            Segment(9, 0, 2000, 0, 3000),
            Segment(10, 200, 2000, 200, 3000)
        });

        var wall = Assert.Single(result.Walls);
        Assert.Equal(6000.0, wall.LengthMm, 1);
        Assert.Equal(200.0, wall.ThicknessMm, 1);
    }

    [Fact]
    public void Analyze_UnselectedDoorBridgeLayer_DoesNotAlterTheSelectedWallLayer()
    {
        // Only layers the user ticked may affect wall geometry. Otherwise dimensions, grids or
        // door details can silently join two real walls across a large empty space.
        const string bridgeLayer = "A-DOOR-BRIDGE";
        var result = Analyze(new CadStructureSegment[]
        {
            Segment(1, 0, 0, 0, 2000),
            Segment(2, 0, 3000, 0, 6000),
            Segment(3, 200, 0, 200, 2000),
            Segment(4, 200, 3000, 200, 6000),
            new(5, new CadStructurePoint2(0, 2000), new CadStructurePoint2(0, 3000),
                bridgeLayer, string.Empty),
            new(6, new CadStructurePoint2(200, 2000), new CadStructurePoint2(200, 3000),
                bridgeLayer, string.Empty)
        });

        Assert.Equal(2, result.Walls.Count);
        Assert.DoesNotContain(result.Walls, wall => Math.Abs(wall.LengthMm - 6000.0) < 1.0);
    }

    [Fact]
    public void Analyze_AWallGapWithOnlyOneBridgeLine_IsNotBridged()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 0, 2000),
            Segment(2, 0, 3000, 0, 6000),
            Segment(3, 200, 0, 200, 2000),
            Segment(4, 200, 3000, 200, 6000),
            Segment(5, 0, 2000, 0, 3000)
        });

        Assert.Equal(2, result.Walls.Count);
        Assert.DoesNotContain(result.Walls, wall => Math.Abs(wall.LengthMm - 6000.0) < 1.0);
    }

    [Fact]
    public void Analyze_BridgeLinesOnAnotherSelectedLayer_DoNotBridgeTheWallLayer()
    {
        const string doorLayer = "A-DOOR";
        var result = CadWallAnalyzer.Analyze(
            Package(new CadStructureSegment[]
            {
                Segment(1, 0, 0, 0, 2000),
                Segment(2, 0, 3000, 0, 6000),
                Segment(3, 200, 0, 200, 2000),
                Segment(4, 200, 3000, 200, 6000),
                new(5, new CadStructurePoint2(0, 2000), new CadStructurePoint2(0, 3000),
                    doorLayer, string.Empty),
                new(6, new CadStructurePoint2(200, 2000), new CadStructurePoint2(200, 3000),
                    doorLayer, string.Empty)
            }),
            new CadWallAnalysisOptions { WallLayers = new[] { WallLayer, doorLayer } });

        // The selected door layer may produce its own candidate, but it must not extend A-WALL.
        Assert.Equal(3, result.Walls.Count);
        Assert.DoesNotContain(result.Walls, wall => Math.Abs(wall.LengthMm - 6000.0) < 1.0);
    }

    [Fact]
    public void Analyze_AnUnopposedBoundaryFragment_DoesNotCreateAPhantomWall()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 2000, 0),
            Segment(2, 0, 200, 2000, 200),
            Segment(3, 3000, 200, 6000, 200)
        });

        var wall = Assert.Single(result.Walls);
        Assert.Equal(2000.0, wall.LengthMm, 1);
    }

    [Fact]
    public void Analyze_StaggeredFaceGaps_StillReconstructOneWall()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 1600, 0),
            Segment(2, 2000, 0, 4000, 0),
            Segment(3, 0, 200, 2000, 200),
            Segment(4, 2400, 200, 4000, 200)
        });

        var wall = Assert.Single(result.Walls);
        Assert.Equal(4000.0, wall.LengthMm, 1);
    }

    [Fact]
    public void Analyze_UnselectedGridLinesAtGapEnds_DoNotBridgeSeparateWalls()
    {
        const string gridLayer = "S-GRID";
        var result = Analyze(new CadStructureSegment[]
        {
            Segment(1, 0, 0, 2000, 0),
            Segment(2, 0, 200, 2000, 200),
            Segment(3, 5000, 0, 7000, 0),
            Segment(4, 5000, 200, 7000, 200),
            new(5, new CadStructurePoint2(2000, -1000), new CadStructurePoint2(2000, 1000),
                gridLayer, string.Empty),
            new(6, new CadStructurePoint2(5000, -1000), new CadStructurePoint2(5000, 1000),
                gridLayer, string.Empty)
        });

        Assert.Equal(2, result.Walls.Count);
        Assert.DoesNotContain(result.Walls, wall => Math.Abs(wall.LengthMm - 7000.0) < 1.0);
    }

    [Fact]
    public void Analyze_LongCrossingWallLinesAtGapEnds_AreNotMistakenForDoorJambs()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 2000, 0),
            Segment(2, 0, 200, 2000, 200),
            Segment(3, 5000, 0, 7000, 0),
            Segment(4, 5000, 200, 7000, 200),
            Segment(5, 2000, -1000, 2000, 1000),
            Segment(6, 5000, -1000, 5000, 1000)
        });

        Assert.Equal(2, result.Walls.Count);
        Assert.DoesNotContain(result.Walls, wall => Math.Abs(wall.LengthMm - 7000.0) < 1.0);
    }

    [Fact]
    public void Analyze_NearlyParallelButNonCollinearWalls_AreNotStraightenedTogether()
    {
        const double angle = 4.0 * Math.PI / 180.0;
        var direction = new CadStructurePoint2(Math.Cos(angle), Math.Sin(angle));
        var normal = new CadStructurePoint2(-direction.Y, direction.X);
        var secondStart = new CadStructurePoint2(3000, 30);
        var secondEnd = secondStart + direction * 2000;
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 2000, 0),
            Segment(2, 0, 200, 2000, 200),
            new CadStructureSegment(3, secondStart - normal * 100, secondEnd - normal * 100,
                WallLayer, string.Empty),
            new CadStructureSegment(4, secondStart + normal * 100, secondEnd + normal * 100,
                WallLayer, string.Empty),
            Segment(5, 2000, 0, 3000, 0),
            Segment(6, 2000, 200, 3000, 200)
        });

        Assert.Equal(2, result.Walls.Count);
    }

    [Fact]
    public void Analyze_DoorNearWallEnd_PreservesShortNibWhenJambsProveContinuation()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 0, 200),
            Segment(2, 0, 1200, 0, 6000),
            Segment(3, 200, 0, 200, 200),
            Segment(4, 200, 1200, 200, 6000),
            Segment(5, 0, 200, 0, 1200),
            Segment(6, 200, 200, 200, 1200)
        });

        var wall = Assert.Single(result.Walls);
        Assert.Equal(6000.0, wall.LengthMm, 1);
    }

    [Fact]
    public void Analyze_SeparateCappedWalls_DoNotMergeWithoutLongitudinalBridgeLines()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 0, 2000),
            Segment(2, 200, 0, 200, 2000),
            Segment(3, 0, 0, 200, 0),
            Segment(4, 0, 2000, 200, 2000),
            Segment(5, 0, 12000, 0, 14000),
            Segment(6, 200, 12000, 200, 14000),
            Segment(7, 0, 12000, 200, 12000),
            Segment(8, 0, 14000, 200, 14000)
        });

        Assert.Equal(2, result.Walls.Count);
    }

    [Fact]
    public void Analyze_CappedWallsInsideRailGapTolerance_StillNeedBridgeLines()
    {
        var result = Analyze(new[]
        {
            Segment(1, 0, 0, 0, 2000),
            Segment(2, 200, 0, 200, 2000),
            Segment(3, 0, 0, 200, 0),
            Segment(4, 0, 2000, 200, 2000),
            Segment(5, 0, 2200, 0, 4000),
            Segment(6, 200, 2200, 200, 4000),
            Segment(7, 0, 2200, 200, 2200),
            Segment(8, 0, 4000, 200, 4000)
        });

        Assert.Equal(2, result.Walls.Count);
    }

    [Fact]
    public void Analyze_DoorContinuationWithOffsetDrift_MergesBeforeADistantExactOffsetWall()
    {
        CadStructureSegment PathSegment(
            int id, double x1, double y1, double x2, double y2, string path) =>
            new(id, new CadStructurePoint2(x1, y1), new CadStructurePoint2(x2, y2),
                WallLayer, path);

        var result = Analyze(new[]
        {
            PathSegment(1, 0, 0, 2000, 0, "A"),
            PathSegment(2, 2000, 0, 2000, 200, "A"),
            PathSegment(3, 2000, 200, 0, 200, "A"),
            PathSegment(4, 0, 200, 0, 0, "A"),
            PathSegment(5, 3000, 5, 6000, 5, "C"),
            PathSegment(6, 6000, 5, 6000, 205, "C"),
            PathSegment(7, 6000, 205, 3000, 205, "C"),
            PathSegment(8, 3000, 205, 3000, 5, "C"),
            PathSegment(9, 10000, 0, 12000, 0, "B"),
            PathSegment(10, 12000, 0, 12000, 200, "B"),
            PathSegment(11, 12000, 200, 10000, 200, "B"),
            PathSegment(12, 10000, 200, 10000, 0, "B"),
            Segment(13, 2000, 0, 3000, 5),
            Segment(14, 2000, 200, 3000, 205)
        });

        Assert.Equal(2, result.Walls.Count);
        Assert.Contains(result.Walls, wall => Math.Abs(wall.LengthMm - 6000.0) < 20.0);
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
