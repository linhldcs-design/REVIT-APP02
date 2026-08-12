using RevitAPP.Core.Models;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.ColumnRebar;

public sealed class ColumnRebarGeometryFactoryTests
{
    [Fact]
    public void Create_DistributionBars_UsesActualDiameterAndPerimeterCount()
    {
        var (stack, plans) = OneStorey(config: Config(barsX: 4, barsY: 3, distribution: true));

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions());

        Assert.Equal(4, geometry.Paths.Count(p => p.Kind == ColumnRebarPathKind.MainBar));
        var distribution = geometry.Paths.Where(p => p.Kind == ColumnRebarPathKind.DistributionBar).ToArray();
        Assert.Equal(6, distribution.Length);
        Assert.All(distribution, p => Assert.Equal(12d, p.DiameterMm));
        Assert.All(distribution, p => Assert.True(p.UsesDistributionBarType));
    }

    [Fact]
    public void Create_DistributionTypeAvailableButOptionOff_UsesMainBarMetadataAndDiameter()
    {
        var (stack, sourcePlans) = OneStorey(config: Config(barsX: 4, barsY: 3));
        var plan = sourcePlans[0] with { DistributionBar = new RebarBarTypeOption(3, "D12", 12) };

        var geometry = ColumnRebarGeometryFactory.Create(stack, new[] { plan }, new RebarLapOptions());

        Assert.Equal(10, geometry.MainBars.Count());
        Assert.DoesNotContain(geometry.MainBars, p => p.UsesDistributionBarType || p.DiameterMm != 20);
    }

    [Fact]
    public void Create_CrosstieBothDirections_AddsExplicitBarsAtEveryStirrupStation()
    {
        var config = Config(barsX: 4, barsY: 3) with
        {
            StirrupSectionType = SectionStirrupType.Crosstie,
            BeamDepthMm = 500
        };
        var (stack, plans) = OneStorey(config: config);
        var spread = new StirrupSpreadOptions(DistanceToFirstMm: 50, CrosstieDirection: CrosstieDirection.Both);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(), spread: spread);

        var stations = geometry.Paths.Where(p => p.Kind == ColumnRebarPathKind.Stirrup).ToArray();
        var crossties = geometry.Paths.Where(p => p.Kind == ColumnRebarPathKind.Crosstie).ToArray();
        Assert.NotEmpty(stations);
        Assert.Equal(stations.Length * 3, crossties.Length); // (BarsX-2) + (BarsY-2)
        Assert.Equal(50d, stations.Min(p => p.Points[0].Zmm));
    }

    [Fact]
    public void Create_SpreadThroughBeamAndJointOptions_ChangeStationFamilies()
    {
        var (stack, plans) = OneStorey(config: Config() with { BeamDepthMm = 600 });
        var lap = new RebarLapOptions();

        var through = ColumnRebarGeometryFactory.Create(stack, plans, lap,
            spread: new StirrupSpreadOptions(SpreadThroughBeam: true));
        var joint = ColumnRebarGeometryFactory.Create(stack, plans, lap,
            spread: new StirrupSpreadOptions(ReinforceJoint: true, JointStirrupCount: 4));

        Assert.True(through.Stirrups.Max(p => p.Points[0].Zmm) > 2400);
        Assert.Equal(4, joint.Paths.Count(p => p.Kind == ColumnRebarPathKind.Stirrup && p.Zone == "Joint"));
    }

    [Fact]
    public void Create_MiddleLapAndStagger_ProducesTwoBottomElevationsOnUpperStorey()
    {
        var (stack, plans) = TwoStoreys();
        var lap = new RebarLapOptions(LapFactor: 40, StaggerLap: true, LapPosition: LapPosition.Middle);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, lap);

        var bottoms = geometry.MainBars.Where(p => p.StoreyIndex == 1)
            .Select(p => p.Points.Min(x => x.Zmm)).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 4500d, 5300d }, bottoms);
    }

    [Fact]
    public void Create_CrankAtLap_KeepsLowerBarsStraightAndBendsIncomingUpperBars()
    {
        var (stack, plans) = TwoStoreys();

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(),
            ends: new ColumnEndOptions(CrankAtLap: true));

        var lower = geometry.MainBars.Where(p => p.StoreyIndex == 0).ToArray();
        var upper = geometry.MainBars.Where(p => p.StoreyIndex == 1).ToArray();
        Assert.All(lower, p => Assert.Equal(2, p.Points.Count));
        Assert.All(upper, p => Assert.Equal(4, p.Points.Count));
        Assert.All(upper, p => Assert.NotNull(p.FallbackPoints));
        Assert.All(upper, p => Assert.Equal(p.DiameterMm, HorizontalDistance(p.Points[1], p.Points[2]), 6));
        Assert.All(upper, p => Assert.True(p.Points[0].Zmm < p.Points[1].Zmm));
    }

    [Fact]
    public void Create_CrankAtLapOnTopStorey_PreservesTopHook()
    {
        var (stack, plans) = TwoStoreys();

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(),
            ends: new ColumnEndOptions(TopHookBending: true, TopHookLengthMm: 175, CrankAtLap: true));

        var upper = geometry.MainBars.Where(p => p.StoreyIndex == 1).ToArray();
        Assert.All(upper, p => Assert.Equal(5, p.Points.Count));
        Assert.All(upper, p => Assert.Equal(175, HorizontalDistance(p.Points[^2], p.Points[^1]), 6));
    }

    [Fact]
    public void Create_TopHook_AddsInwardHorizontalLeg()
    {
        var (stack, plans) = OneStorey();

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(),
            ends: new ColumnEndOptions(TopHookBending: true, TopHookLengthMm: 175));

        Assert.All(geometry.MainBars, p =>
        {
            Assert.Equal(3, p.Points.Count);
            Assert.Equal(2, p.FallbackPoints!.Count);
            Assert.Equal(p.Points[1].Zmm, p.Points[2].Zmm);
            Assert.Equal(175d, HorizontalDistance(p.Points[1], p.Points[2]), 6);
        });
    }

    [Theory]
    [InlineData(LargeStepMode.AnchorAtSlab, 3)]
    [InlineData(LargeStepMode.CrankContinuous, 4)]
    public void Create_LargeSectionStep_RepresentsSelectedTransitionMode(LargeStepMode mode, int pointCount)
    {
        var (stack, plans) = TwoStoreys(lowerSection: new ColumnSection(700, 700),
            upperSection: new ColumnSection(300, 300));

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(),
            transition: new SectionTransitionOptions(LargeStepMode: mode));

        Assert.Contains(geometry.MainBars.Where(p => p.StoreyIndex == 0), p => p.Points.Count == pointCount);
    }

    [Theory]
    [InlineData(49d)]
    [InlineData(50d)]
    public void Create_SectionOffsetAtOrBelowThreshold_UsesSmoothCrankEvenInAnchorMode(double offset)
    {
        var upperSize = 500 - 2 * offset;
        var (stack, plans) = TwoStoreys(upperSection: new ColumnSection(upperSize, upperSize));

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(),
            transition: new SectionTransitionOptions(BendIfOffsetLeMm: 50, SlopeRatioHdOverE: 8,
                LargeStepMode: LargeStepMode.AnchorAtSlab));

        var cranks = geometry.MainBars.Where(p => p.StoreyIndex == 0).ToArray();
        Assert.All(cranks, p => Assert.Equal(4, p.Points.Count));
        Assert.Equal(3000 - Math.Max(offset * 8, 300), cranks[0].Points[1].Zmm, 6);
    }

    [Fact]
    public void Create_SectionOffsetAboveThreshold_UsesLargeStepModeAndJointAnchorDown()
    {
        var (stack, plans) = TwoStoreys(upperSection: new ColumnSection(398, 398)); // e = 51mm
        var options = new SectionTransitionOptions(BendIfOffsetLeMm: 50,
            LargeStepMode: LargeStepMode.AnchorAtSlab, JointAnchorDownMm: 325);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(), transition: options);

        Assert.All(geometry.MainBars.Where(p => p.StoreyIndex == 0), p => Assert.Equal(3, p.Points.Count));
        Assert.All(geometry.MainBars.Where(p => p.StoreyIndex == 1),
            p => Assert.Equal(2675d, p.Points.Min(x => x.Zmm)));
    }

    [Fact]
    public void Create_SectionOffsetAboveThresholdCrankMode_DoesNotAnchorUpperBarsDown()
    {
        var (stack, plans) = TwoStoreys(upperSection: new ColumnSection(398, 398));
        var options = new SectionTransitionOptions(BendIfOffsetLeMm: 50,
            LargeStepMode: LargeStepMode.CrankContinuous, JointAnchorDownMm: 325);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(), transition: options);

        Assert.All(geometry.MainBars.Where(p => p.StoreyIndex == 0), p => Assert.Equal(4, p.Points.Count));
        Assert.DoesNotContain(geometry.MainBars.Where(p => p.StoreyIndex == 1),
            p => p.Points.Min(x => x.Zmm) < 3000);
    }

    [Fact]
    public void Create_SeparatedTie_ProducesChildLoopsDistinctFromClosedTie()
    {
        var separatedConfig = Config(barsX: 4, barsY: 3) with { StirrupSectionType = SectionStirrupType.Separated };
        var (stack, separatedPlans) = OneStorey(config: separatedConfig);
        var closedPlans = new[] { separatedPlans[0] with { Config = separatedConfig with { StirrupSectionType = SectionStirrupType.ClosedTie } } };

        var separated = ColumnRebarGeometryFactory.Create(stack, separatedPlans, new RebarLapOptions());
        var closed = ColumnRebarGeometryFactory.Create(stack, closedPlans, new RebarLapOptions());

        var separatedAtFirst = separated.Stirrups.Where(p => p.Points[0].Zmm == 0).ToArray();
        var closedAtFirst = closed.Stirrups.Where(p => p.Points[0].Zmm == 0).ToArray();
        Assert.Equal(3, separatedAtFirst.Length); // one child loop per adjacent X bay
        Assert.Single(closedAtFirst);
        Assert.All(separatedAtFirst, p => Assert.True(
            p.Points.Max(x => x.Xmm) - p.Points.Min(x => x.Xmm) < 500 - 2 * 25 - 8));
    }

    [Fact]
    public void Create_PathologicalTinyStirrupSpacing_RejectsBeforeMaterializingPaths()
    {
        var (stack, plans) = OneStorey(config: Config() with { SpacingEndMm = 0.01, SpacingMidMm = 0.01 });

        var error = Assert.Throws<ArgumentException>(() =>
            ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions()));

        Assert.Contains("20,000", error.Message);
    }

    [Fact]
    public void Create_OverflowBypassStirrupSpacing_RejectsBeforeZoneCountConversion()
    {
        const double overflowSpacing = 3.49e-7;
        var (stack, plans) = OneStorey(config: Config() with
        {
            SpacingEndMm = overflowSpacing,
            SpacingMidMm = overflowSpacing
        });

        var error = Assert.Throws<ArgumentException>(() =>
            ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions()));

        Assert.Contains("20,000", error.Message);
    }

    [Fact]
    public void Create_UniformA150_EmitsOneContinuousStirrupZoneForPreviewAndBuilder()
    {
        var config = Config() with { UniformStirrupSpacing = true, UniformSpacingMm = 150 };
        var (stack, plans) = OneStorey(config: config);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions());
        var stirrups = geometry.Stirrups.Where(path => path.Kind == ColumnRebarPathKind.Stirrup).ToArray();

        Assert.Equal(21, stirrups.Length);
        Assert.Single(stirrups.Select(path => path.Zone).Distinct());
        Assert.All(stirrups.Zip(stirrups.Skip(1)), pair =>
            Assert.Equal(150, pair.Second.Points[0].Zmm - pair.First.Points[0].Zmm, 6));
    }

    [Fact]
    public void Create_UniformA150_WithFirstOffset_DoesNotPlaceStirrupBeyondStorey()
    {
        var config = Config() with { UniformStirrupSpacing = true, UniformSpacingMm = 150 };
        var (stack, plans) = OneStorey(config: config);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(),
            spread: new StirrupSpreadOptions(DistanceToFirstMm: 50));
        var elevations = geometry.Stirrups.Where(path => path.Kind == ColumnRebarPathKind.Stirrup)
            .Select(path => path.Points[0].Zmm).ToArray();

        Assert.Equal(50, elevations.Min());
        Assert.Equal(2900, elevations.Max());
        Assert.All(elevations, elevation => Assert.InRange(elevation, 0, 3000));
    }

    [Fact]
    public void Create_FoundationStarterSplitBothSides_EmitsFeetOnOppositeSidesAndFoundationContext()
    {
        var (stack, plans) = OneStorey();
        var starter = new FoundationStarterOptions(true, HmMm: 350, LbMm: 250,
            Direction: StarterBendDirection.Right, SplitBothSides: true);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(), starter);

        var starters = geometry.Starters.ToArray();
        Assert.Equal(8, starters.Length);
        Assert.Contains(starters, p => p.Points[0].Xmm < p.Points[1].Xmm);
        Assert.Contains(starters, p => p.Points[0].Xmm > p.Points[1].Xmm);
        Assert.All(starters.Where(p => p.Points.Count > 3), p => Assert.Equal(3, p.FallbackPoints!.Count));
        Assert.Contains(geometry.Context, c => c.Kind == ColumnRebarContextKind.Foundation &&
                                               c.BaseElevationMm == -350);
    }

    [Fact]
    public void Create_StarterDistributionTypeAvailableButOptionOff_UsesMainBarTypeMetadata()
    {
        var (stack, sourcePlans) = OneStorey(config: Config(barsX: 4, barsY: 3));
        var plan = sourcePlans[0] with { DistributionBar = new RebarBarTypeOption(3, "D12", 12) };

        var geometry = ColumnRebarGeometryFactory.Create(stack, new[] { plan }, new RebarLapOptions(),
            new FoundationStarterOptions(Enabled: true));

        Assert.Equal(10, geometry.Starters.Count());
        Assert.DoesNotContain(geometry.Starters, p => p.UsesDistributionBarType || p.DiameterMm != 20);
    }

    [Fact]
    public void Create_FoundationBars_RunContinuouslyThroughFirstStoreyWithoutBaseSplice()
    {
        var (stack, plans) = TwoStoreys();
        var lap = new RebarLapOptions(LapFactor: 30, StaggerLap: false,
            LapPosition: LapPosition.NearBottom, LapDistanceFromBottomMm: 50);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, lap,
            new FoundationStarterOptions(Enabled: true, HmMm: 350, LbMm: 250),
            ends: new ColumnEndOptions(CrankAtLap: true));

        var foundationBars = geometry.Starters.ToArray();
        Assert.Equal(8, foundationBars.Length);
        Assert.DoesNotContain(geometry.MainBars, p => p.StoreyIndex == 0);
        Assert.All(foundationBars, p =>
        {
            Assert.Equal(-350, p.Points[0].Zmm);
            Assert.Equal(-350, p.Points[1].Zmm);
            Assert.True(p.Points[^1].Zmm > plans[0].Storey.TopElevationMm);
            Assert.DoesNotContain(p.Points.Skip(2).Take(p.Points.Count - 3),
                point => point.Zmm == plans[0].Storey.BaseElevationMm);
        });

        var incomingUpper = geometry.MainBars.Where(p => p.StoreyIndex == 1).ToArray();
        Assert.All(incomingUpper, p => Assert.Equal(4, p.Points.Count));
    }

    [Fact]
    public void Create_OneStoreyFoundationBars_PreserveTopHookOnContinuousBar()
    {
        var (stack, plans) = OneStorey();

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(),
            new FoundationStarterOptions(Enabled: true, HmMm: 300, LbMm: 200),
            ends: new ColumnEndOptions(TopHookBending: true, TopHookLengthMm: 175));

        Assert.Empty(geometry.MainBars);
        Assert.All(geometry.Starters, path =>
        {
            Assert.Equal(-300, path.Points[0].Zmm);
            Assert.Equal(175, HorizontalDistance(path.Points[^2], path.Points[^1]), 6);
            Assert.True(IsCoplanar(path.Points));
        });
    }

    [Fact]
    public void Create_FoundationBarsWithSectionCrank_RemainCoplanarForRevit()
    {
        var (stack, plans) = TwoStoreys(upperSection: new ColumnSection(420, 420));

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions(),
            new FoundationStarterOptions(Enabled: true, HmMm: 300, LbMm: 200,
                Direction: StarterBendDirection.Right),
            transition: new SectionTransitionOptions(BendIfOffsetLeMm: 50));

        Assert.All(geometry.Starters, path => Assert.True(IsCoplanar(path.Points)));
    }

    [Theory]
    [InlineData(500, 500)]
    [InlineData(420, 420)]
    public void Create_FoundationBarsWithStagger_UseTwoFiftyFiftyTopElevations(
        double upperWidth, double upperHeight)
    {
        var (stack, plans) = TwoStoreys(upperSection: new ColumnSection(upperWidth, upperHeight));
        var lap = new RebarLapOptions(LapFactor: 30, StaggerLap: true,
            LapPosition: LapPosition.NearBottom, LapDistanceFromBottomMm: 50);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, lap,
            new FoundationStarterOptions(Enabled: true),
            transition: new SectionTransitionOptions(BendIfOffsetLeMm: 50));
        var tops = geometry.Starters.GroupBy(path => path.Points.Max(point => point.Zmm))
            .OrderBy(group => group.Key).ToArray();

        Assert.Equal(2, tops.Length);
        Assert.Equal(4, tops[0].Count());
        Assert.Equal(4, tops[1].Count());
        Assert.Equal(600, tops[1].Key - tops[0].Key, 6);

        if (upperWidth < 500)
        {
            var fallbackTops = geometry.Starters
                .GroupBy(path => path.FallbackPoints!.Max(point => point.Zmm))
                .OrderBy(group => group.Key).ToArray();
            Assert.Equal(new[] { 4, 4 }, fallbackTops.Select(group => group.Count()).ToArray());
            Assert.Equal(600, fallbackTops[1].Key - fallbackTops[0].Key, 6);
            Assert.All(geometry.Starters, path =>
            {
                Assert.Equal(path.Points[^1].Xmm, path.FallbackPoints![^1].Xmm, 6);
                Assert.Equal(path.Points[^1].Ymm, path.FallbackPoints[^1].Ymm, 6);
                Assert.True(IsCoplanar(path.FallbackPoints));
            });
        }
    }

    [Fact]
    public void Create_FoundationBarsWithLargeCrankContinuous_PreserveFiftyFiftyStagger()
    {
        var (stack, plans) = TwoStoreys(upperSection: new ColumnSection(398, 398));
        var geometry = ColumnRebarGeometryFactory.Create(stack, plans,
            new RebarLapOptions(LapFactor: 30, StaggerLap: true,
                LapPosition: LapPosition.NearBottom, LapDistanceFromBottomMm: 50),
            new FoundationStarterOptions(Enabled: true),
            transition: new SectionTransitionOptions(BendIfOffsetLeMm: 50,
                LargeStepMode: LargeStepMode.CrankContinuous));

        var tops = geometry.Starters.GroupBy(path => path.Points.Max(point => point.Zmm))
            .OrderBy(group => group.Key).ToArray();
        Assert.Equal(new[] { 4, 4 }, tops.Select(group => group.Count()).ToArray());
        Assert.Equal(600, tops[1].Key - tops[0].Key, 6);
        var fallbackTops = geometry.Starters
            .GroupBy(path => path.FallbackPoints!.Max(point => point.Zmm))
            .OrderBy(group => group.Key).ToArray();
        Assert.Equal(new[] { 4, 4 }, fallbackTops.Select(group => group.Count()).ToArray());
        Assert.Equal(600, fallbackTops[1].Key - fallbackTops[0].Key, 6);
        Assert.All(geometry.Starters, path =>
        {
            Assert.Equal(path.Points[^1].Xmm, path.FallbackPoints![^1].Xmm, 6);
            Assert.Equal(path.Points[^1].Ymm, path.FallbackPoints[^1].Ymm, 6);
            Assert.True(IsCoplanar(path.FallbackPoints));
        });
    }

    [Fact]
    public void Create_FoundationBarsWithOneAxisTransition_CollapseCollinearFallbackVertex()
    {
        var (stack, plans) = TwoStoreys(upperSection: new ColumnSection(420, 500));
        var geometry = ColumnRebarGeometryFactory.Create(stack, plans,
            new RebarLapOptions(LapFactor: 30, StaggerLap: true,
                LapPosition: LapPosition.NearBottom, LapDistanceFromBottomMm: 50),
            new FoundationStarterOptions(Enabled: true),
            transition: new SectionTransitionOptions(BendIfOffsetLeMm: 50));

        var straightFallbacks = geometry.Starters
            .Where(path => path.FallbackPoints!.Count == 3)
            .ToArray();

        Assert.NotEmpty(straightFallbacks);
        Assert.All(straightFallbacks, path =>
        {
            Assert.Equal(path.FallbackPoints![^2].Xmm, path.FallbackPoints[^1].Xmm, 6);
            Assert.Equal(path.FallbackPoints[^2].Ymm, path.FallbackPoints[^1].Ymm, 6);
        });
        Assert.All(geometry.Starters, path => Assert.True(IsCoplanar(path.FallbackPoints!)));
    }

    [Fact]
    public void Create_FoundationBarsWithStaggerDisabled_UseOneTopElevation()
    {
        var (stack, plans) = TwoStoreys(upperSection: new ColumnSection(420, 420));
        var geometry = ColumnRebarGeometryFactory.Create(stack, plans,
            new RebarLapOptions(LapFactor: 30, StaggerLap: false),
            new FoundationStarterOptions(Enabled: true),
            transition: new SectionTransitionOptions(BendIfOffsetLeMm: 50));

        Assert.Single(geometry.Starters.Select(path => path.Points.Max(point => point.Zmm)).Distinct());
    }

    [Fact]
    public void Create_RotationAndOffset_TransformsAllPathsIntoSharedStackCoordinates()
    {
        var (stack, plans) = OneStorey(centerX: 1000, centerY: 2000, rotation: Math.PI / 2);

        var geometry = ColumnRebarGeometryFactory.Create(stack, plans, new RebarLapOptions());

        var first = geometry.MainBars.First().Points[0];
        var level = Assert.Single(geometry.Storeys);
        Assert.Equal(("L1", 0d, 3000d), (level.LevelName, level.BaseElevationMm, level.TopElevationMm));
        Assert.InRange(first.Xmm, 1000 - 220, 1000 + 220);
        Assert.InRange(first.Ymm, 2000 - 220, 2000 + 220);
    }

    private static (IReadOnlyList<ColumnRebarStackContext>, IReadOnlyList<StoreyRebarPlan>) OneStorey(
        FloorRebarConfig? config = null, double centerX = 0, double centerY = 0, double rotation = 0)
    {
        var storey = new ColumnStorey(0, "L1", 0, 3000, new ColumnSection(500, 500));
        return (new[] { new ColumnRebarStackContext(storey, centerX, centerY, rotation) },
            new[] { Plan(storey, config ?? Config()) });
    }

    private static (IReadOnlyList<ColumnRebarStackContext>, IReadOnlyList<StoreyRebarPlan>) TwoStoreys(
        ColumnSection? lowerSection = null, ColumnSection? upperSection = null)
    {
        var lower = new ColumnStorey(0, "L1", 0, 3000, lowerSection ?? new ColumnSection(500, 500));
        var upper = new ColumnStorey(1, "L2", 3000, 6000, upperSection ?? new ColumnSection(500, 500));
        return (new[] { new ColumnRebarStackContext(lower, 0, 0), new ColumnRebarStackContext(upper, 0, 0) },
            new[] { Plan(lower, Config()), Plan(upper, Config()) });
    }

    private static StoreyRebarPlan Plan(ColumnStorey storey, FloorRebarConfig config) =>
        new(storey, config, new RebarBarTypeOption(1, "D20", 20),
            new RebarBarTypeOption(2, "D8", 8),
            config.UseDistributionBar ? new RebarBarTypeOption(3, "D12", 12) : null);

    private static FloorRebarConfig Config(int barsX = 3, int barsY = 3, bool distribution = false) =>
        new(20, barsX, barsY, 8, 100, 200, UseDistributionBar: distribution,
            DistributionBarDiameterMm: distribution ? 12 : 0);

    private static double HorizontalDistance(GeometryPoint3D a, GeometryPoint3D b) =>
        Math.Sqrt(Math.Pow(a.Xmm - b.Xmm, 2) + Math.Pow(a.Ymm - b.Ymm, 2));

    private static bool IsCoplanar(IReadOnlyList<GeometryPoint3D> points)
    {
        var origin = points[0];
        var first = points.Skip(1).Select(p => Vector(origin, p)).First(v => Length(v) > 1e-6);
        var second = points.Skip(1).Select(p => Vector(origin, p))
            .FirstOrDefault(v => Length(Cross(first, v)) > 1e-6);
        if (Length(second) < 1e-6) return true;
        var normal = Cross(first, second);
        return points.All(p => Math.Abs(Dot(normal, Vector(origin, p))) <= 1e-4 * Length(normal));

        static (double X, double Y, double Z) Vector(GeometryPoint3D a, GeometryPoint3D b) =>
            (b.Xmm - a.Xmm, b.Ymm - a.Ymm, b.Zmm - a.Zmm);
        static (double X, double Y, double Z) Cross((double X, double Y, double Z) a,
            (double X, double Y, double Z) b) =>
            (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static double Length((double X, double Y, double Z) v) => Math.Sqrt(Dot(v, v));
    }
}
