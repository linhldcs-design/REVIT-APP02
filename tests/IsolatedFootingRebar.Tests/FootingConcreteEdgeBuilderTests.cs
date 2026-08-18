using IsolatedFootingRebar.Models;
using IsolatedFootingRebar.Services;

namespace IsolatedFootingRebar.Tests;

public sealed class FootingConcreteEdgeBuilderTests
{
    [Fact]
    public void Build_RemovesCoplanarTriangulationDiagonal()
    {
        var a = new PreviewPoint3D(0, 0, 0);
        var b = new PreviewPoint3D(100, 0, 0);
        var c = new PreviewPoint3D(100, 100, 0);
        var d = new PreviewPoint3D(0, 100, 0);

        var edges = FootingConcreteEdgeBuilder.Build(
        [
            new FootingPreviewTriangle(a, b, c),
            new FootingPreviewTriangle(a, c, d)
        ]);

        Assert.Equal(4, edges.Count);
        Assert.DoesNotContain(edges, edge => Same(edge, a, c));
    }

    [Fact]
    public void Build_KeepsSharpSharedEdge()
    {
        var a = new PreviewPoint3D(0, 0, 0);
        var b = new PreviewPoint3D(100, 0, 0);
        var c = new PreviewPoint3D(0, 100, 0);
        var d = new PreviewPoint3D(0, 0, 100);

        var edges = FootingConcreteEdgeBuilder.Build(
        [
            new FootingPreviewTriangle(a, b, c),
            new FootingPreviewTriangle(b, a, d)
        ]);

        Assert.Contains(edges, edge => Same(edge, a, b));
    }

    private static bool Same(FootingPreviewEdge edge, PreviewPoint3D a, PreviewPoint3D b)
        => edge.A == a && edge.B == b || edge.A == b && edge.B == a;
}
