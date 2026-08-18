using IsolatedFootingRebar.Models;
using IsolatedFootingRebar.Services;

namespace IsolatedFootingRebar.Tests;

public sealed class FootingSectionClipperTests
{
    [Fact]
    public void Clip_TriangularFooting_ReturnsWidthAtActualSection()
    {
        var mesh = Prism(
        [
            new PreviewPoint3D(-1000, -500, 0),
            new PreviewPoint3D(1000, -500, 0),
            new PreviewPoint3D(0, 1000, 0)
        ], 500);

        var interval = Assert.Single(FootingSectionClipper.Clip(mesh, alongX: true, crossMm: 0, zMm: 250));

        Assert.Equal(-666.667, interval.StartMm, 3);
        Assert.Equal(666.667, interval.EndMm, 3);
    }

    [Fact]
    public void Clip_ConcavePolygon_ReturnsMultipleInsideIntervals()
    {
        var mesh = Prism(
        [
            new PreviewPoint3D(-1000,-1000,0), new PreviewPoint3D(1000,-1000,0),
            new PreviewPoint3D(1000,1000,0), new PreviewPoint3D(300,1000,0),
            new PreviewPoint3D(300,-300,0), new PreviewPoint3D(-300,-300,0),
            new PreviewPoint3D(-300,1000,0), new PreviewPoint3D(-1000,1000,0)
        ], 500);

        var intervals = FootingSectionClipper.Clip(mesh, alongX: true, crossMm: 500, zMm: 250);

        Assert.Equal(2, intervals.Count);
        Assert.Equal(new FootingLineInterval(-1000, -300), intervals[0]);
        Assert.Equal(new FootingLineInterval(300, 1000), intervals[1]);
    }

    private static IReadOnlyList<FootingPreviewTriangle> Prism(IReadOnlyList<PreviewPoint3D> footprint, double height)
    {
        var triangles = new List<FootingPreviewTriangle>();
        for (var i = 0; i < footprint.Count; i++)
        {
            var next = (i + 1) % footprint.Count;
            var a = footprint[i];
            var b = footprint[next];
            var at = a with { Zmm = height };
            var bt = b with { Zmm = height };
            triangles.Add(new FootingPreviewTriangle(a, b, bt));
            triangles.Add(new FootingPreviewTriangle(a, bt, at));
        }
        return triangles;
    }
}
