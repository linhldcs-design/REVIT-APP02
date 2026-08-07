using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadPlanarGraphTests
{
    [Fact]
    public void BuildFaces_SingleRectangle_ReturnsOneFace()
    {
        var faces = CadPlanarGraph.BuildFaces(
            Rectangle(1, 0, 0, 4000, 3000), 20.0, out var unclosed);

        var face = Assert.Single(faces);
        Assert.Equal(4, face.VerticesMm.Count);
        Assert.Equal(12_000_000, face.AreaMm2, 0);
        Assert.Equal(0, unclosed);
    }

    [Fact]
    public void BuildFaces_TwoCellsSharingAnEdge_ReturnsTwoFaces()
    {
        var segments = new List<CadStructureSegment>();
        segments.AddRange(Rectangle(1, 0, 0, 4000, 3000));
        segments.AddRange(Rectangle(10, 4000, 0, 8000, 3000));

        var faces = CadPlanarGraph.BuildFaces(segments, 20.0, out _);

        Assert.Equal(2, faces.Count);
        Assert.All(faces, face => Assert.Equal(12_000_000, face.AreaMm2, 0));
    }

    [Fact]
    public void BuildFaces_LinesCrossingWithoutAVertex_StillFormFourCells()
    {
        // A drawn grid: two long horizontals and two long verticals that cross in the middle with
        // no vertex of their own. Splitting at the crossings is what turns them into cells.
        var segments = new List<CadStructureSegment>
        {
            Segment(1, 0, 0, 8000, 0),
            Segment(2, 0, 3000, 8000, 3000),
            Segment(3, 0, 6000, 8000, 6000),
            Segment(4, 0, 0, 0, 6000),
            Segment(5, 4000, 0, 4000, 6000),
            Segment(6, 8000, 0, 8000, 6000)
        };

        var faces = CadPlanarGraph.BuildFaces(segments, 20.0, out _);

        Assert.Equal(4, faces.Count);
        Assert.All(faces, face => Assert.Equal(12_000_000, face.AreaMm2, 0));
    }

    [Fact]
    public void BuildFaces_CornersThatDoNotMeet_AreSnappedIntoOneFace()
    {
        // Corners left 8 mm apart, which is what trimming and snapping leave behind.
        var segments = new List<CadStructureSegment>
        {
            Segment(1, 0, 0, 4000, 0),
            Segment(2, 4008, 3, 4008, 3000),
            Segment(3, 4005, 3004, 5, 3004),
            Segment(4, 2, 2998, 2, 6)
        };

        var faces = CadPlanarGraph.BuildFaces(segments, 20.0, out var unclosed);

        Assert.Single(faces);
        Assert.Equal(0, unclosed);
    }

    [Fact]
    public void BuildFaces_GapWiderThanTolerance_ReportsUnclosedVertices()
    {
        var segments = new List<CadStructureSegment>
        {
            Segment(1, 0, 0, 4000, 0),
            Segment(2, 4100, 0, 4100, 3000),
            Segment(3, 4100, 3000, 0, 3000),
            Segment(4, 0, 3000, 0, 0)
        };

        var faces = CadPlanarGraph.BuildFaces(segments, 20.0, out var unclosed);

        Assert.Empty(faces);
        Assert.True(unclosed > 0);
    }

    [Fact]
    public void BuildFaces_DanglingLine_DoesNotBecomeAFace()
    {
        var segments = new List<CadStructureSegment>(Rectangle(1, 0, 0, 4000, 3000))
        {
            Segment(10, 4000, 1500, 6000, 1500)
        };

        var faces = CadPlanarGraph.BuildFaces(segments, 20.0, out _);

        var face = Assert.Single(faces);
        Assert.Equal(12_000_000, face.AreaMm2, 0);
    }

    [Fact]
    public void BuildFaces_CellInsideACell_ReturnsBothFaces()
    {
        var segments = new List<CadStructureSegment>();
        segments.AddRange(Rectangle(1, 0, 0, 9000, 9000));
        segments.AddRange(Rectangle(10, 3000, 3000, 6000, 6000));

        var faces = CadPlanarGraph.BuildFaces(segments, 20.0, out _);

        Assert.Equal(2, faces.Count);
        Assert.Contains(faces, face => Math.Abs(face.AreaMm2 - 9_000_000) < 1);
    }

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
        new(id, new CadStructurePoint2(x1, y1), new CadStructurePoint2(x2, y2), "SLAB", string.Empty);
}
