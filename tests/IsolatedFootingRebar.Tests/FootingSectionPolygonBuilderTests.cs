using IsolatedFootingRebar.Models;
using IsolatedFootingRebar.Services;

namespace IsolatedFootingRebar.Tests;

public sealed class FootingSectionPolygonBuilderTests
{
    [Fact]
    public void Build_TriangularPrism_ReturnsTriangleAtRequestedElevation()
    {
        var contours = FootingSectionPolygonBuilder.Build(TriangularPrism(), 450);

        var contour = Assert.Single(contours);
        Assert.Equal(3, contour.Count);
        Assert.All(contour, point => Assert.Equal(450, point.Zmm, 6));
    }

    [Fact]
    public void Inset_Triangle_MovesAllEdgesInward()
    {
        PreviewPoint3D[] triangle = [new(-1500,-1200,450), new(1500,-1200,450), new(0,1200,450)];

        var inset = FootingSectionPolygonBuilder.Inset(triangle, 50);

        Assert.Equal(3, inset.Count);
        Assert.True(inset.Min(point => point.Ymm) > -1200);
        Assert.True(inset.Max(point => point.Ymm) < 1200);
    }

    [Fact]
    public void BuildAndInset_ConcavePolygon_PreservesNotchInsteadOfUsingBoundingBox()
    {
        PreviewPoint3D[] footprint =
        [
            new(-1000,-1000,0), new(1000,-1000,0), new(1000,1000,0), new(300,1000,0),
            new(300,-300,0), new(-300,-300,0), new(-300,1000,0), new(-1000,1000,0)
        ];
        var contour = Assert.Single(FootingSectionPolygonBuilder.Build(Prism(footprint, 500), 250));

        var inset = FootingSectionPolygonBuilder.Inset(contour, 50);

        Assert.Equal(8, contour.Count);
        Assert.Equal(8, inset.Count);
        Assert.Contains(inset, point => point.Xmm > 340 && point.Xmm < 360 && point.Ymm < -340);
        Assert.Contains(inset, point => point.Xmm < -340 && point.Xmm > -360 && point.Ymm < -340);
    }

    [Fact]
    public void Inset_CollapsedNarrowPolygon_FailsClosedInsteadOfReturningUnsafeCenterline()
    {
        PreviewPoint3D[] narrow =
        [
            new(-500,-500,0), new(500,-500,0), new(500,500,0), new(40,500,0),
            new(40,-400,0), new(-40,-400,0), new(-40,500,0), new(-500,500,0)
        ];

        var inset = FootingSectionPolygonBuilder.Inset(narrow, 50);

        Assert.Empty(inset);
    }

    private static IReadOnlyList<FootingPreviewTriangle> TriangularPrism()
    {
        PreviewPoint3D[] bottom = [new(-1500,-1200,0), new(1500,-1200,0), new(0,1200,0)];
        var top = bottom.Select(point => point with { Zmm = 900 }).ToArray();
        var triangles = new List<FootingPreviewTriangle>
        {
            new(bottom[0], bottom[1], bottom[2]), new(top[0], top[2], top[1])
        };
        for (var i = 0; i < 3; i++)
        {
            var n = (i + 1) % 3;
            triangles.Add(new FootingPreviewTriangle(bottom[i], bottom[n], top[n]));
            triangles.Add(new FootingPreviewTriangle(bottom[i], top[n], top[i]));
        }
        return triangles;
    }

    private static IReadOnlyList<FootingPreviewTriangle> Prism(
        IReadOnlyList<PreviewPoint3D> footprint, double height)
    {
        var triangles = new List<FootingPreviewTriangle>();
        for (var i = 0; i < footprint.Count; i++)
        {
            var next = (i + 1) % footprint.Count;
            var a = footprint[i]; var b = footprint[next];
            var at = a with { Zmm = height }; var bt = b with { Zmm = height };
            triangles.Add(new FootingPreviewTriangle(a, b, bt));
            triangles.Add(new FootingPreviewTriangle(a, bt, at));
        }
        return triangles;
    }
}
