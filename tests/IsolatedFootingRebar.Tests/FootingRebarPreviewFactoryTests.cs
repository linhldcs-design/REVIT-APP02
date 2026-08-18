using IsolatedFootingRebar.Models;
using IsolatedFootingRebar.Services;

namespace IsolatedFootingRebar.Tests;

public sealed class FootingRebarPreviewFactoryTests
{
    [Fact]
    public void Build_UsesExactPickedConcreteTriangles()
    {
        var geometry = Geometry() with
        {
            ConcreteTriangles =
            [
                new ConcreteTriangle(new Point3(-1, -1, 0), new Point3(1, -1, 0), new Point3(0, 1, 2))
            ]
        };

        var plan = FootingRebarPreviewFactory.Build(geometry, new FootingRebarModel
        {
            BottomEnabled = false, TopEnabled = false, HorizontalEnabled = false
        });

        Assert.Single(plan.Concrete);
        Assert.Equal(609.6, plan.Concrete[0].C.Zmm, 6);
    }

    [Fact]
    public void Build_ProducesEveryEnabledBarKind()
    {
        var model = new FootingRebarModel
        {
            BottomEnabled = true,
            TopEnabled = true,
            MidEnabled = true,
            MidLayers = 2,
            VerticalEnabled = true,
            HorizontalEnabled = true,
            Cover = new CoverSettings { BottomMm = 50, TopMm = 50, SideMm = 50 },
            Vertical = new VerticalBarConfig
            {
                Diameter = new RebarDiameter(10), UseSpacing = false, CountX = 2, CountY = 5,
                HookLengthMm = 100, WidthMm = 300
            },
            Horizontal = new HorizontalStirrupConfig
            {
                DiameterX = new RebarDiameter(8), DiameterY = new RebarDiameter(8), Layers = 2
            }
        };

        var kinds = FootingRebarPreviewFactory.Build(Geometry(), model).Paths.Select(p => p.Kind).Distinct().ToHashSet();

        Assert.Equal(Enum.GetValues<FootingPreviewBarKind>().Length, kinds.Count);
    }

    [Fact]
    public void Build_HookOptionChangesMeshPathShape()
    {
        var hooked = new FootingRebarModel
        {
            TopEnabled = false, HorizontalEnabled = false,
            Cover = new CoverSettings { BottomMm = 50, TopMm = 50, SideMm = 50 },
            BottomX = new LayerBarConfig { HookEnabled = true, HookLengthMm = 250 },
            BottomY = new LayerBarConfig { Enabled = false }
        };
        var straight = hooked with { BottomX = hooked.BottomX with { HookEnabled = false } };

        var hookedPath = FootingRebarPreviewFactory.Build(Geometry(), hooked).Paths[0];
        var straightPath = FootingRebarPreviewFactory.Build(Geometry(), straight).Paths[0];

        Assert.Equal(4, hookedPath.Points.Count);
        Assert.Equal(2, straightPath.Points.Count);
        Assert.Equal(250, hookedPath.Points[0].Zmm - hookedPath.Points[1].Zmm, 6);
    }

    [Fact]
    public void Build_InvalidCoverThrowsAndAllowsViewModelToKeepLastValidPlan()
    {
        var model = new FootingRebarModel
        {
            Cover = new CoverSettings { BottomMm = 400, TopMm = 400, SideMm = 2000 }
        };

        var error = Assert.Throws<ArgumentException>(() => FootingRebarPreviewFactory.Build(Geometry(), model));
        Assert.Contains("bảo vệ", error.Message);
    }

    [Fact]
    public void Build_SegmentedHorizontalHasVisibleOpening()
    {
        var model = new FootingRebarModel
        {
            BottomEnabled = false, TopEnabled = false, HorizontalEnabled = true,
            Cover = new CoverSettings { BottomMm = 50, TopMm = 50, SideMm = 50 },
            Horizontal = new HorizontalStirrupConfig
            {
                DiameterX = new RebarDiameter(8), DiameterY = new RebarDiameter(8),
                Closed = false, HookEnabled = true, HookLengthMm = 120, Layers = 1
            }
        };

        var path = Assert.Single(FootingRebarPreviewFactory.Build(Geometry(), model).Paths);

        Assert.False(path.IsClosed);
        Assert.True(path.Points.Count >= 7);
        Assert.Equal(120, path.Points[0].Xmm - path.Points[1].Xmm, 6);
    }

    [Fact]
    public void RequiresIndividualMeshBars_DetectsTriangularFooting()
    {
        var geometry = Geometry() with { ConcreteTriangles = TriangularPrism() };
        var model = new FootingRebarModel
        {
            TopEnabled = false, HorizontalEnabled = false,
            Cover = new CoverSettings { BottomMm = 35, TopMm = 35, SideMm = 35 },
            BottomX = new LayerBarConfig { HookEnabled = false, SpacingMm = 200 },
            BottomY = new LayerBarConfig { HookEnabled = false, SpacingMm = 200 }
        };
        var plan = FootingRebarPreviewFactory.Build(geometry, model);

        Assert.True(FootingRebarPreviewFactory.RequiresIndividualMeshBars(plan, geometry, model));
        Assert.True(plan.Paths.Where(path => path.Kind == FootingPreviewBarKind.BottomX)
            .Select(path => path.Points[^1].Xmm - path.Points[0].Xmm).Distinct().Count() > 1);
    }

    [Fact]
    public void RequiresIndividualMeshBars_KeepsRectangularSetOptimization()
    {
        var geometry = Geometry();
        var model = new FootingRebarModel
        {
            TopEnabled = false, HorizontalEnabled = false,
            Cover = new CoverSettings { BottomMm = 35, TopMm = 35, SideMm = 35 }
        };
        var plan = FootingRebarPreviewFactory.Build(geometry, model);

        Assert.False(FootingRebarPreviewFactory.RequiresIndividualMeshBars(plan, geometry, model));
    }

    [Fact]
    public void Build_HorizontalRebarFollowsTriangularSectionInsteadOfBoundingRectangle()
    {
        var geometry = Geometry() with { ConcreteTriangles = TriangularPrism() };
        var model = new FootingRebarModel
        {
            BottomEnabled = false, TopEnabled = false, VerticalEnabled = false, HorizontalEnabled = true,
            Cover = new CoverSettings { BottomMm = 35, TopMm = 35, SideMm = 35 },
            Horizontal = new HorizontalStirrupConfig
            {
                DiameterX = new RebarDiameter(8), DiameterY = new RebarDiameter(8), Closed = true, Layers = 1
            }
        };

        var path = Assert.Single(FootingRebarPreviewFactory.Build(geometry, model).Paths);

        Assert.True(path.IsClosed);
        Assert.Equal(3, path.Points.Count);
        Assert.Equal(3, path.Points.Select(point => (Math.Round(point.Xmm), Math.Round(point.Ymm))).Distinct().Count());
    }

    [Fact]
    public void Build_ChairsOutsideTriangularSectionAreRemoved()
    {
        var geometry = Geometry() with { ConcreteTriangles = TriangularPrism() };
        var model = new FootingRebarModel
        {
            BottomEnabled = false, TopEnabled = false, VerticalEnabled = true, HorizontalEnabled = false,
            Cover = new CoverSettings { BottomMm = 35, TopMm = 35, SideMm = 35 },
            Vertical = new VerticalBarConfig
            {
                Diameter = new RebarDiameter(8), UseSpacing = true,
                SpacingXMm = 350, SpacingYMm = 350, WidthMm = 250, HookLengthMm = 100
            }
        };

        var plan = FootingRebarPreviewFactory.Build(geometry, model);
        var chairs = plan.Paths.Where(path => path.Kind == FootingPreviewBarKind.Chair).ToArray();

        Assert.NotEmpty(chairs);
        Assert.All(chairs.SelectMany(path => path.Points), point =>
        {
            var intervals = FootingSectionClipper.Clip(plan.Concrete, true, point.Ymm, point.Zmm);
            Assert.Contains(intervals, interval => point.Xmm >= interval.StartMm + 38 && point.Xmm <= interval.EndMm - 38);
        });
    }

    [Fact]
    public void Build_ChairsUseNumber_CreatesExactCountXTimesCountY()
    {
        var model = new FootingRebarModel
        {
            BottomEnabled = false, TopEnabled = false, VerticalEnabled = true, HorizontalEnabled = false,
            Cover = new CoverSettings { BottomMm = 35, TopMm = 35, SideMm = 35 },
            Vertical = new VerticalBarConfig
            {
                Diameter = new RebarDiameter(8), UseSpacing = false,
                CountX = 2, CountY = 5, WidthMm = 250, HookLengthMm = 100
            }
        };

        var chairs = FootingRebarPreviewFactory.Build(Geometry(), model).Paths
            .Where(path => path.Kind == FootingPreviewBarKind.Chair).ToArray();

        Assert.Equal(10, chairs.Length);
        Assert.Equal(5, chairs.Select(path => Math.Round(path.Points[0].Ymm, 3)).Distinct().Count());
    }

    [Fact]
    public void Build_ChairsUseNumber_AllowsSingleChairInEachDirection()
    {
        var model = new FootingRebarModel
        {
            BottomEnabled = false, TopEnabled = false, VerticalEnabled = true, HorizontalEnabled = false,
            Cover = new CoverSettings { BottomMm = 35, TopMm = 35, SideMm = 35 },
            Vertical = new VerticalBarConfig
            {
                Diameter = new RebarDiameter(8), UseSpacing = false,
                CountX = 1, CountY = 1, WidthMm = 250, HookLengthMm = 100
            }
        };

        var chair = Assert.Single(FootingRebarPreviewFactory.Build(Geometry(), model).Paths);

        Assert.Equal(FootingPreviewBarKind.Chair, chair.Kind);
        Assert.All(chair.Points, point => Assert.Equal(0, point.Ymm, 6));
    }

    [Fact]
    public void Build_TriangularMeshCenterlinesRespectPerpendicularCoverAndBarRadius()
    {
        var geometry = Geometry() with { ConcreteTriangles = TriangularPrism() };
        var model = new FootingRebarModel
        {
            TopEnabled = false, HorizontalEnabled = false,
            Cover = new CoverSettings { BottomMm = 35, TopMm = 35, SideMm = 35 },
            BottomX = new LayerBarConfig { Diameter = new RebarDiameter(12), HookEnabled = false, SpacingMm = 200 },
            BottomY = new LayerBarConfig { Enabled = false }
        };

        var paths = FootingRebarPreviewFactory.Build(geometry, model).Paths;
        var required = 35 + 12d / 2;
        var a = new PreviewPoint3D(-1500, -1200, 0);
        var b = new PreviewPoint3D(0, 1200, 0);

        Assert.All(paths.SelectMany(path => path.Points), point =>
            Assert.True(DistanceToLine(point, a, b) >= required - 0.75));
    }

    [Fact]
    public void Build_HooksOnTaperedFootingAreShortenedBeforeTheyExitConcrete()
    {
        var geometry = Geometry() with { ConcreteTriangles = TaperedTriangularPrism() };
        var model = new FootingRebarModel
        {
            TopEnabled = false, HorizontalEnabled = false,
            Cover = new CoverSettings { BottomMm = 35, TopMm = 35, SideMm = 35 },
            BottomX = new LayerBarConfig
            {
                Diameter = new RebarDiameter(12), HookEnabled = true, HookLengthMm = 600, SpacingMm = 250
            },
            BottomY = new LayerBarConfig { Enabled = false }
        };

        var paths = FootingRebarPreviewFactory.Build(geometry, model).Paths;

        Assert.NotEmpty(paths);
        Assert.Contains(paths, path => path.Points.Count == 2);
        Assert.All(paths, path => Assert.True(path.Points.Count is 2 or 4));
    }

    private static FootingGeometry Geometry() => new()
    {
        BaseCenter = new Point3(0, 0, 0),
        DirX = new Point3(1, 0, 0),
        DirY = new Point3(0, 1, 0),
        WidthXFeet = 3000 / 304.8,
        WidthYFeet = 2400 / 304.8,
        BottomZFeet = 0,
        BaseTopZFeet = 900 / 304.8
    };

    private static IReadOnlyList<ConcreteTriangle> TriangularPrism()
    {
        Point3 P(double x, double y, double z) => new(x / 304.8, y / 304.8, z / 304.8);
        var bottom = new[] { P(-1500,-1200,0), P(1500,-1200,0), P(0,1200,0) };
        var top = bottom.Select(point => point with { Z = 900 / 304.8 }).ToArray();
        var triangles = new List<ConcreteTriangle>
        {
            new(bottom[0], bottom[1], bottom[2]), new(top[0], top[2], top[1])
        };
        for (var i = 0; i < 3; i++)
        {
            var n = (i + 1) % 3;
            triangles.Add(new ConcreteTriangle(bottom[i], bottom[n], top[n]));
            triangles.Add(new ConcreteTriangle(bottom[i], top[n], top[i]));
        }
        return triangles;
    }

    private static IReadOnlyList<ConcreteTriangle> TaperedTriangularPrism()
    {
        Point3 P(double x, double y, double z) => new(x / 304.8, y / 304.8, z / 304.8);
        var bottom = new[] { P(-1500,-1200,0), P(1500,-1200,0), P(0,1200,0) };
        var top = new[] { P(-750,-600,900), P(750,-600,900), P(0,600,900) };
        var triangles = new List<ConcreteTriangle>
        {
            new(bottom[0], bottom[1], bottom[2]), new(top[0], top[2], top[1])
        };
        for (var i = 0; i < 3; i++)
        {
            var n = (i + 1) % 3;
            triangles.Add(new ConcreteTriangle(bottom[i], bottom[n], top[n]));
            triangles.Add(new ConcreteTriangle(bottom[i], top[n], top[i]));
        }
        return triangles;
    }

    private static double DistanceToLine(PreviewPoint3D point, PreviewPoint3D a, PreviewPoint3D b)
    {
        var numerator = Math.Abs((b.Ymm - a.Ymm) * point.Xmm - (b.Xmm - a.Xmm) * point.Ymm +
                                 b.Xmm * a.Ymm - b.Ymm * a.Xmm);
        var denominator = Math.Sqrt(Math.Pow(b.Ymm - a.Ymm, 2) + Math.Pow(b.Xmm - a.Xmm, 2));
        return numerator / denominator;
    }
}
