using RevitAPP.Core.Models;

namespace RevitAPP.Core.Services;

/// <summary>
/// Builds the shared centreline geometry used by the faithful column-rebar preview. This class deliberately has
/// no Autodesk references; callers extract stack centres from Revit once, then may rebuild safely off-thread.
/// </summary>
public static class ColumnRebarGeometryFactory
{
    private const double PartnerToleranceMm = 40d;
    private const int MaxExplicitStirrupPaths = 20_000;

    public static ColumnRebarGeometryPlan Create(
        IReadOnlyList<ColumnRebarStackContext> stack,
        IReadOnlyList<StoreyRebarPlan> plans,
        RebarLapOptions lap,
        FoundationStarterOptions? starter = null,
        StirrupSpreadOptions? spread = null,
        ColumnEndOptions? ends = null,
        SectionTransitionOptions? transition = null)
    {
        if (stack == null) throw new ArgumentNullException(nameof(stack));
        if (plans == null) throw new ArgumentNullException(nameof(plans));
        if (stack.Count != plans.Count)
            throw new ArgumentException("Stack and reinforcement plan counts must match.", nameof(plans));

        spread ??= new StirrupSpreadOptions();
        ends ??= new ColumnEndOptions();
        transition ??= new SectionTransitionOptions();

        var context = BuildContext(stack, plans, starter);
        var paths = new List<ColumnRebarPath>();
        for (var i = 0; i < plans.Count; i++)
        {
            AddStirrups(paths, stack[i], plans[i], lap, spread);
            var mainPathStart = paths.Count;
            AddMainBars(paths, stack, plans, i, lap, ends, transition);
            if (i == 0 && starter is { Enabled: true })
                ExtendFirstStoreyBarsIntoFoundation(paths, mainPathStart, stack[i], plans[i], lap, starter);
        }

        return new ColumnRebarGeometryPlan(stack.ToArray(), context, paths);
    }

    private static IReadOnlyList<ColumnRebarContextVolume> BuildContext(
        IReadOnlyList<ColumnRebarStackContext> stack,
        IReadOnlyList<StoreyRebarPlan> plans,
        FoundationStarterOptions? starter)
    {
        var result = new List<ColumnRebarContextVolume>();
        for (var i = 0; i < stack.Count; i++)
        {
            var item = stack[i];
            var storey = plans[i].Storey;
            result.Add(new ColumnRebarContextVolume(storey.Index, ColumnRebarContextKind.Column,
                item.CenterXmm, item.CenterYmm, item.RotationRad, storey.BaseElevationMm,
                storey.TopElevationMm, storey.Section.WidthMm, storey.Section.HeightMm));

            if (plans[i].Config.BeamDepthMm > 0)
                result.Add(new ColumnRebarContextVolume(storey.Index, ColumnRebarContextKind.Beam,
                    item.CenterXmm, item.CenterYmm, item.RotationRad,
                    storey.TopElevationMm - plans[i].Config.BeamDepthMm, storey.TopElevationMm,
                    storey.Section.WidthMm, storey.Section.HeightMm));
        }

        if (stack.Count > 0 && starter is { Enabled: true })
        {
            var first = stack[0];
            var section = plans[0].Storey.Section;
            result.Add(new ColumnRebarContextVolume(plans[0].Storey.Index, ColumnRebarContextKind.Foundation,
                first.CenterXmm, first.CenterYmm, first.RotationRad,
                plans[0].Storey.BaseElevationMm - starter.HmMm, plans[0].Storey.BaseElevationMm,
                section.WidthMm + 2 * starter.LbMm, section.HeightMm + 2 * starter.LbMm));
        }

        return result;
    }

    private static void AddStirrups(List<ColumnRebarPath> result, ColumnRebarStackContext item,
        StoreyRebarPlan plan, RebarLapOptions lap, StirrupSpreadOptions spread)
    {
        var config = plan.Config;
        var loop = TcvnRebarCalculator.BuildStirrupLoop(plan.Storey.Section, lap.CoverMm, config.StirrupDiameterMm);
        var loops = config.StirrupSectionType == SectionStirrupType.Separated
            ? BuildSeparatedLoops(plan.Storey.Section, config, lap.CoverMm, loop)
            : new[] { loop };
        var zoneConfig = spread.SpreadThroughBeam ? config with { BeamDepthMm = 0 } : config;
        var pathsPerStation = loops.Count;
        if (config.StirrupSectionType == SectionStirrupType.Crosstie)
        {
            if (spread.CrosstieDirection is CrosstieDirection.X or CrosstieDirection.Both)
                pathsPerStation += Math.Max(0, config.BarsX - 2);
            if (spread.CrosstieDirection is CrosstieDirection.Y or CrosstieDirection.Both)
                pathsPerStation += Math.Max(0, config.BarsY - 2);
        }

        // Guard before ComputeZones performs its double -> int conversion. A conservative full-height estimate
        // deliberately over-counts all three zones but prevents tiny/invalid spacing from overflowing ZoneCount.
        var stationUpper = config.UniformStirrupSpacing
            ? StationUpperBound(plan.Storey.ClearHeightMm, config.UniformSpacingMm)
            : 2d * StationUpperBound(plan.Storey.ClearHeightMm, config.SpacingEndMm) +
              StationUpperBound(plan.Storey.ClearHeightMm, config.SpacingMidMm);
        if (spread.ReinforceJoint && !spread.SpreadThroughBeam && config.BeamDepthMm > 0)
            stationUpper += Math.Max(2d, spread.JointStirrupCount);
        if (stationUpper > MaxExplicitStirrupPaths / (double)Math.Max(1, pathsPerStation))
            throw new ArgumentException($"Stirrup settings create more than {MaxExplicitStirrupPaths:N0} explicit preview paths. Increase spacing.", nameof(plan));

        var zones = TcvnRebarCalculator.ComputeZones(plan.Storey.ClearHeightMm, plan.Storey.Section, zoneConfig,
            spread.MinConfineZoneMm, spread.ConfineClearanceDivisor);

        var stationCount = (long)zones.Bottom.Count + zones.Middle.Count + zones.Top.Count;
        if (spread.ReinforceJoint && !spread.SpreadThroughBeam && config.BeamDepthMm > 0)
            stationCount += Math.Max(2, spread.JointStirrupCount);
        if (stationCount * pathsPerStation > MaxExplicitStirrupPaths)
            throw new ArgumentException($"Stirrup settings create more than {MaxExplicitStirrupPaths:N0} explicit preview paths. Increase spacing.", nameof(plan));

        var isFirstZone = true;
        foreach (var entry in new[] { ("Bottom", zones.Bottom), ("Middle", zones.Middle), ("Top", zones.Top) })
        {
            if (entry.Item2.Count <= 0 || entry.Item2.LengthMm <= 0)
            {
                isFirstZone = false;
                continue;
            }

            var start = plan.Storey.BaseElevationMm + entry.Item2.StartElevationMm +
                        (isFirstZone ? spread.DistanceToFirstMm : 0);
            var zoneEnd = plan.Storey.BaseElevationMm + entry.Item2.StartElevationMm + entry.Item2.LengthMm;
            isFirstZone = false;
            for (var station = 0; station < entry.Item2.Count; station++)
            {
                var elevation = start + station * entry.Item2.SpacingMm;
                if (elevation > zoneEnd + 1e-6) break;
                foreach (var stationLoop in loops)
                    AddStirrupStation(result, item, plan, stationLoop, elevation,
                        entry.Item1, spread);
            }
        }

        if (spread.ReinforceJoint && !spread.SpreadThroughBeam && config.BeamDepthMm > 0)
        {
            var count = Math.Max(2, spread.JointStirrupCount);
            var start = plan.Storey.BaseElevationMm + plan.Storey.ClearHeightMm - config.BeamDepthMm;
            var spacing = config.BeamDepthMm / (count - 1);
            for (var station = 0; station < count; station++)
                foreach (var stationLoop in loops)
                    AddStirrupStation(result, item, plan, stationLoop, start + station * spacing, "Joint", spread,
                        includeCrossties: false);
        }
    }

    /// <summary>
    /// Creates overlapping child ties, one around every adjacent bar bay on the axis with the larger number of
    /// bars. Each longitudinal bar is enclosed by at least one tie and the result is visibly distinct from a
    /// single closed perimeter tie.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<Point2D>> BuildSeparatedLoops(ColumnSection section,
        FloorRebarConfig config, double coverMm, IReadOnlyList<Point2D> outer)
    {
        var bars = TcvnRebarCalculator.BuildMainBarPositions(section, config, coverMm);
        var minX = outer.Min(p => p.Xmm);
        var maxX = outer.Max(p => p.Xmm);
        var minY = outer.Min(p => p.Ymm);
        var maxY = outer.Max(p => p.Ymm);
        var loops = new List<IReadOnlyList<Point2D>>();

        if (config.BarsX >= config.BarsY && config.BarsX > 2)
        {
            var xs = bars.Select(p => p.Xmm).Distinct().OrderBy(x => x).ToArray();
            for (var i = 0; i < xs.Length - 1; i++)
                loops.Add(Rectangle(xs[i], xs[i + 1], minY, maxY));
        }
        else if (config.BarsY > 2)
        {
            var ys = bars.Select(p => p.Ymm).Distinct().OrderBy(y => y).ToArray();
            for (var i = 0; i < ys.Length - 1; i++)
                loops.Add(Rectangle(minX, maxX, ys[i], ys[i + 1]));
        }

        return loops.Count > 0 ? loops : new[] { outer };

        static IReadOnlyList<Point2D> Rectangle(double left, double right, double bottom, double top) =>
            new[] { new Point2D(left, bottom), new Point2D(right, bottom), new Point2D(right, top), new Point2D(left, top) };
    }

    private static void AddStirrupStation(List<ColumnRebarPath> result, ColumnRebarStackContext item,
        StoreyRebarPlan plan, IReadOnlyList<Point2D> loop, double elevationMm, string zone,
        StirrupSpreadOptions spread, bool includeCrossties = true)
    {
        var ordered = new[] { loop[2], loop[3], loop[0], loop[1], loop[2] };
        result.Add(Path(item, plan, ColumnRebarPathKind.Stirrup, plan.Config.StirrupDiameterMm,
            ordered.Select(p => World(item, p, elevationMm)), zone));

        if (!includeCrossties || plan.Config.StirrupSectionType != SectionStirrupType.Crosstie) return;
        var halfW = Math.Abs(loop[0].Xmm);
        var halfH = Math.Abs(loop[0].Ymm);
        if (spread.CrosstieDirection is CrosstieDirection.X or CrosstieDirection.Both)
            foreach (var x in TcvnRebarCalculator.CrosstieXPositions(plan.Storey.Section, plan.Config, spreadCover(plan, loop)))
                result.Add(Path(item, plan, ColumnRebarPathKind.Crosstie, plan.Config.StirrupDiameterMm,
                    new[] { World(item, new Point2D(x, halfH), elevationMm), World(item, new Point2D(x, -halfH), elevationMm) }, zone));
        if (spread.CrosstieDirection is CrosstieDirection.Y or CrosstieDirection.Both)
            foreach (var y in TcvnRebarCalculator.CrosstieYPositions(plan.Storey.Section, plan.Config, spreadCover(plan, loop)))
                result.Add(Path(item, plan, ColumnRebarPathKind.Crosstie, plan.Config.StirrupDiameterMm,
                    new[] { World(item, new Point2D(halfW, y), elevationMm), World(item, new Point2D(-halfW, y), elevationMm) }, zone));

        // Recover cover from the loop definition: loop width = B - 2c - d_tie.
        static double spreadCover(StoreyRebarPlan p, IReadOnlyList<Point2D> l) =>
            (p.Storey.Section.WidthMm - 2 * Math.Abs(l[0].Xmm) - p.Config.StirrupDiameterMm) / 2d;
    }

    private static void AddMainBars(List<ColumnRebarPath> result,
        IReadOnlyList<ColumnRebarStackContext> stack, IReadOnlyList<StoreyRebarPlan> plans, int i,
        RebarLapOptions lap, ColumnEndOptions ends, SectionTransitionOptions transition)
    {
        var item = stack[i];
        var plan = plans[i];
        var config = plan.Config;
        var bars = TcvnRebarCalculator.BuildClassifiedMainBars(plan.Storey.Section, config, lap.CoverMm);
        var isBottom = i == 0;
        var isTop = i == plans.Count - 1;

        var previousOffset = !isBottom
            ? TcvnRebarCalculator.SectionStepOffsetMm(plans[i - 1].Storey.Section, plans[i - 1].Config,
                plan.Storey.Section, config, lap.CoverMm)
            : 0d;
        var prevLargeAnchor = previousOffset > transition.BendIfOffsetLeMm &&
                              transition.LargeStepMode == LargeStepMode.AnchorAtSlab;
        var jointDown = prevLargeAnchor
            ? transition.JointAnchorDownMm : 0d;
        var staggerShift = TcvnRebarCalculator.LapLengthMm(config.MainBarDiameterMm, lap.LapFactor);
        var lapZoneOffset = lap.LapPosition == LapPosition.Middle
            ? plan.Storey.ClearHeightMm / 2d
            : lap.LapDistanceFromBottomMm;

        var nextBop = false;
        var nextStepOffset = 0d;
        IReadOnlyList<PlacedBar>? upperBars = null;
        var offLocalX = 0d;
        var offLocalY = 0d;
        if (!isTop && (nextStepOffset = TcvnRebarCalculator.SectionStepOffsetMm(plan.Storey.Section, config,
                plans[i + 1].Storey.Section, plans[i + 1].Config, lap.CoverMm)) >= 1d)
        {
            nextBop = true;
            upperBars = TcvnRebarCalculator.BuildClassifiedMainBars(plans[i + 1].Storey.Section,
                plans[i + 1].Config, lap.CoverMm);
            var dx = stack[i + 1].CenterXmm - item.CenterXmm;
            var dy = stack[i + 1].CenterYmm - item.CenterYmm;
            var cos = Math.Cos(item.RotationRad);
            var sin = Math.Sin(item.RotationRad);
            offLocalX = dx * cos + dy * sin;
            offLocalY = -dx * sin + dy * cos;
        }

        foreach (var bar in bars)
        {
            var barLap = TcvnRebarCalculator.LapLengthMm(bar.DiameterMm, lap.LapFactor);
            var parity = TcvnRebarCalculator.StaggerParity(bar.Position, plan.Storey.Section, config, lap.CoverMm);
            var (bottom, top) = TcvnRebarCalculator.MainBarSpan(plan.Storey.BaseElevationMm,
                plan.Storey.TopElevationMm, barLap, staggerShift, lapZoneOffset, isBottom, isTop,
                lap.StaggerLap, parity);
            if (isTop) top -= lap.CoverMm + config.StirrupDiameterMm;
            if (prevLargeAnchor) bottom = plan.Storey.BaseElevationMm - jointDown;

            var topPos = bar.Position;
            var transitionOffset = 0d;
            var noUpperPartner = false;
            if (nextBop && upperBars!.Count > 0)
            {
                var inUpper = new Point2D(bar.Position.Xmm - offLocalX, bar.Position.Ymm - offLocalY);
                var nearest = upperBars.OrderBy(b => DistanceSquared(b.Position, inUpper)).First();
                topPos = new Point2D(nearest.Position.Xmm + offLocalX, nearest.Position.Ymm + offLocalY);
                transitionOffset = Math.Sqrt(DistanceSquared(topPos, bar.Position));
                noUpperPartner = transitionOffset > PartnerToleranceMm;
            }

            var smoothCrank = nextBop && nextStepOffset <= transition.BendIfOffsetLeMm;
            var largeStep = nextBop && nextStepOffset > transition.BendIfOffsetLeMm;
            var doCrank = smoothCrank || largeStep && transition.LargeStepMode == LargeStepMode.CrankContinuous;
            var doAnchor = largeStep && transition.LargeStepMode == LargeStepMode.AnchorAtSlab;
            IReadOnlyList<GeometryPoint3D> points;
            IReadOnlyList<GeometryPoint3D>? fallback = null;
            if (doAnchor)
            {
                var anchorTop = plan.Storey.TopElevationMm - lap.CoverMm - config.StirrupDiameterMm;
                var hook = ends.TopHookLengthMm > 0 ? ends.TopHookLengthMm : 150d;
                var leg = TcvnRebarCalculator.TopHookLegEnd(bar.Position, hook);
                points = new[] { World(item, bar.Position, bottom), World(item, bar.Position, anchorTop), World(item, leg, anchorTop) };
                fallback = new[] { World(item, bar.Position, bottom), World(item, bar.Position, anchorTop) };
            }
            else if (doCrank)
            {
                var hd = Math.Max(nextStepOffset * transition.SlopeRatioHdOverE, 300);
                points = new[]
                {
                    World(item, bar.Position, bottom),
                    World(item, bar.Position, plan.Storey.TopElevationMm - hd),
                    World(item, topPos, plan.Storey.TopElevationMm),
                    World(item, topPos, top)
                };
                // Keep the fallback simpler than the primary crank, while still arriving at the upper
                // bar position and retaining this bar's parity-specific lap top. Extending vertically at
                // the lower position can put the fallback outside a smaller/offset upper column.
                fallback = DistanceSquared(topPos, bar.Position) < 1e-6
                    ? new[]
                    {
                        World(item, bar.Position, bottom),
                        World(item, topPos, top)
                    }
                    : new[]
                    {
                        World(item, bar.Position, bottom),
                        World(item, topPos, plan.Storey.TopElevationMm),
                        World(item, topPos, top)
                    };
            }
            else
            {
                // Lap principle 2: keep the lower-storey bar straight. The incoming upper-storey bar
                // starts one diameter inward, then cranks back to its design position near the top
                // of the lap zone. This is the inverse of the old principle, which bent the lower bar.
                var path = new List<GeometryPoint3D>();
                if (!isBottom && !prevLargeAnchor && ends.CrankAtLap && barLap > 0)
                {
                    var slope = Math.Min(Math.Max(bar.DiameterMm * 6, 50), barLap / 2);
                    var crankOffset = TcvnRebarCalculator.TopHookLegEnd(bar.Position, bar.DiameterMm);
                    path.Add(World(item, crankOffset, bottom));
                    path.Add(World(item, crankOffset, bottom + barLap - slope));
                    path.Add(World(item, bar.Position, bottom + barLap));
                    path.Add(World(item, bar.Position, top));
                }
                else
                {
                    path.Add(World(item, topPos, bottom));
                    path.Add(World(item, topPos, top));
                }

                if (isTop && ends.TopHookBending && ends.TopHookLengthMm > 0)
                {
                    var leg = TcvnRebarCalculator.TopHookLegEnd(bar.Position, ends.TopHookLengthMm);
                    path.Add(World(item, leg, top));
                }

                points = path;
                fallback = path.Count > 2
                    ? new[] { World(item, topPos, bottom), World(item, topPos, top) }
                    : null;
            }

            var usesDistributionType = !bar.IsCorner && config.UseDistributionBar;
            result.Add(Path(item, plan, usesDistributionType ? ColumnRebarPathKind.DistributionBar :
                ColumnRebarPathKind.MainBar, bar.DiameterMm, points, fallback: fallback,
                usesDistributionBarType: usesDistributionType));
        }
    }

    /// <summary>
    /// Turns every first-storey longitudinal bar into one continuous foundation bar. The horizontal L leg,
    /// foundation embedment and first-storey/lap geometry are emitted as one centreline, so Revit cannot create
    /// a splice at the foundation/column interface. The next storey remains the incoming lap bar.
    /// </summary>
    private static void ExtendFirstStoreyBarsIntoFoundation(List<ColumnRebarPath> result, int mainPathStart,
        ColumnRebarStackContext item, StoreyRebarPlan plan, RebarLapOptions lap,
        FoundationStarterOptions starter)
    {
        var bars = TcvnRebarCalculator.BuildClassifiedMainBars(plan.Storey.Section, plan.Config, lap.CoverMm);
        var (baseDirX, baseDirY) = starter.DirectionVector;
        var mainPaths = result.Skip(mainPathStart)
            .Where(p => p.Kind is ColumnRebarPathKind.MainBar or ColumnRebarPathKind.DistributionBar)
            .ToArray();
        if (mainPaths.Length != bars.Count)
            throw new InvalidOperationException("First-storey main-bar geometry does not match its foundation bars.");

        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var mainPath = mainPaths[index];
            var dirX = baseDirX;
            var dirY = baseDirY;
            if (starter.SplitBothSides)
            {
                if (baseDirX != 0) dirX = bar.Position.Xmm >= 0 ? 1 : -1;
                if (baseDirY != 0) dirY = bar.Position.Ymm >= 0 ? 1 : -1;
            }

            var bottom = plan.Storey.BaseElevationMm - starter.HmMm;
            var footDirection = AlignFoundationFootWithBarPlane(item, mainPath.Points, dirX, dirY);
            var foot = new Point2D(bar.Position.Xmm + footDirection.Xmm * starter.LbMm,
                bar.Position.Ymm + footDirection.Ymm * starter.LbMm);
            var points = PrefixFoundationLeg(item, bar.Position, foot, bottom, mainPath.Points);
            var fallback = mainPath.FallbackPoints == null
                ? null
                : PrefixFoundationLeg(item, bar.Position, foot, bottom, mainPath.FallbackPoints);
            result[result.IndexOf(mainPath)] = mainPath with
            {
                Kind = ColumnRebarPathKind.FoundationStarter,
                Points = points,
                FallbackPoints = fallback
            };
        }
    }

    private static Point2D AlignFoundationFootWithBarPlane(ColumnRebarStackContext item,
        IReadOnlyList<GeometryPoint3D> mainPoints, double preferredX, double preferredY)
    {
        for (var i = 1; i < mainPoints.Count; i++)
        {
            var worldDx = mainPoints[i].Xmm - mainPoints[i - 1].Xmm;
            var worldDy = mainPoints[i].Ymm - mainPoints[i - 1].Ymm;
            var length = Math.Sqrt(worldDx * worldDx + worldDy * worldDy);
            if (length < 1e-6) continue;

            var cos = Math.Cos(item.RotationRad);
            var sin = Math.Sin(item.RotationRad);
            var localX = (worldDx * cos + worldDy * sin) / length;
            var localY = (-worldDx * sin + worldDy * cos) / length;
            if (localX * preferredX + localY * preferredY < 0)
                return new Point2D(-localX, -localY);
            return new Point2D(localX, localY);
        }

        return new Point2D(preferredX, preferredY);
    }

    private static IReadOnlyList<GeometryPoint3D> PrefixFoundationLeg(ColumnRebarStackContext item,
        Point2D barPosition, Point2D foot, double bottom, IReadOnlyList<GeometryPoint3D> mainPoints)
    {
        var points = new List<GeometryPoint3D>(mainPoints.Count + 2)
        {
            World(item, foot, bottom),
            World(item, barPosition, bottom)
        };
        // The first main point is at the first-storey base on the same vertical leg. Omitting that point keeps
        // the embedment and column portion as one uninterrupted straight Revit curve instead of two collinear curves.
        points.AddRange(mainPoints.Skip(1));
        return points;
    }

    private static ColumnRebarPath Path(ColumnRebarStackContext item, StoreyRebarPlan plan,
        ColumnRebarPathKind kind, double diameter, IEnumerable<GeometryPoint3D> points, string? zone = null,
        IReadOnlyList<GeometryPoint3D>? fallback = null, bool usesDistributionBarType = false) =>
        new(plan.Storey.Index, plan.Storey.LevelName, kind, diameter, points.ToArray(), zone, fallback,
            usesDistributionBarType);

    private static GeometryPoint3D World(ColumnRebarStackContext item, Point2D local, double zMm)
    {
        var cos = Math.Cos(item.RotationRad);
        var sin = Math.Sin(item.RotationRad);
        return new GeometryPoint3D(item.CenterXmm + local.Xmm * cos - local.Ymm * sin,
            item.CenterYmm + local.Xmm * sin + local.Ymm * cos, zMm);
    }

    private static double DistanceSquared(Point2D a, Point2D b) =>
        Math.Pow(a.Xmm - b.Xmm, 2) + Math.Pow(a.Ymm - b.Ymm, 2);

    private static double StationUpperBound(double heightMm, double spacingMm)
    {
        if (spacingMm <= 0 || double.IsNaN(spacingMm) || double.IsInfinity(spacingMm))
            throw new ArgumentException("Stirrup spacing must be a finite value greater than zero.");
        return Math.Floor(heightMm / spacingMm) + 1d;
    }
}
