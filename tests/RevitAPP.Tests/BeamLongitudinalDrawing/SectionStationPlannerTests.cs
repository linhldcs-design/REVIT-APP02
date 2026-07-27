using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.BeamLongitudinalDrawing;

public sealed class SectionStationPlannerTests
{
    [Fact]
    public void Plan_OneNonUniformSpan_ReturnsLeftMidRight()
    {
        var chain = Chain(10);
        var profile = new BeamSpanSectionProfile(1, F(1), F(2), F(3));

        var result = SectionStationPlanner.Plan(chain, [profile], reduceUniformSpans: true);

        Assert.Equal(3, result.Count);
        Assert.Equal(
            [SectionStationKind.LeftSupport, SectionStationKind.MidSpan, SectionStationKind.RightSupport],
            result.Select(x => x.Kind));
        Assert.Equal(new[] { 0.0, 5.0, 10.0 }, result.Select(x => x.ChainDistanceFeet));
    }

    [Fact]
    public void Plan_TwoSpansEquivalentCommonSupport_DeduplicatesToFiveStations()
    {
        var chain = Chain(10, 10);
        var common = F(5);
        var profiles = new[]
        {
            new BeamSpanSectionProfile(1, F(1), F(2), common),
            new BeamSpanSectionProfile(2, common, F(3), F(4))
        };

        var result = SectionStationPlanner.Plan(chain, profiles, reduceUniformSpans: false);

        Assert.Equal(5, result.Count);
        var shared = Assert.Single(result, x => Math.Abs(x.ChainDistanceFeet - 10) < 1e-9);
        Assert.Equal(SectionStationKind.SharedSupport, shared.Kind);
        Assert.Equal(new[] { 0, 1 }, shared.SourceSpanIndices);
    }

    [Fact]
    public void Plan_TwoSpansDifferentCommonSupport_KeepsSixStations()
    {
        var chain = Chain(10, 10);
        var profiles = new[]
        {
            new BeamSpanSectionProfile(1, F(1), F(2), F(3)),
            new BeamSpanSectionProfile(2, F(4), F(5), F(6))
        };

        var result = SectionStationPlanner.Plan(chain, profiles, reduceUniformSpans: false);

        Assert.Equal(6, result.Count);
        var transition = result.Where(x => Math.Abs(x.ChainDistanceFeet - 10) < 0.2).ToList();
        Assert.Equal(2, transition.Count);
        Assert.Contains(transition, item => item.ChainDistanceFeet < 10);
        Assert.Contains(transition, item => item.ChainDistanceFeet > 10);
    }

    [Fact]
    public void Plan_UniformSpan_ReducesToOneMidSpanStation()
    {
        var chain = Chain(12);
        var uniform = F(1);

        var result = SectionStationPlanner.Plan(
            chain, [new BeamSpanSectionProfile(1, uniform, uniform, uniform)], reduceUniformSpans: true);

        var station = Assert.Single(result);
        Assert.Equal(SectionStationKind.MidSpan, station.Kind);
        Assert.Contains("uniform", station.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_UniformFingerprintWithAdditionalReinforcement_DoesNotReduce()
    {
        var chain = Chain(12);
        var reinforced = F(1) with { HasAdditionalReinforcement = true };

        var result = SectionStationPlanner.Plan(
            chain, [new BeamSpanSectionProfile(1, reinforced, reinforced, reinforced)], reduceUniformSpans: true);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Plan_UniformFingerprintWithMultipleStirrupZones_DoesNotReduce()
    {
        var chain = Chain(12);
        var multipleZones = F(1) with
        {
            StirrupZones = [new StirrupZoneFingerprint(8, 0.3), new StirrupZoneFingerprint(8, 0.6)]
        };

        var result = SectionStationPlanner.Plan(
            chain, [new BeamSpanSectionProfile(1, multipleZones, multipleZones, multipleZones)],
            reduceUniformSpans: true);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Plan_TwoReducedSpansWithDifferentCommonSupport_PreservesBothTransitionSides()
    {
        var chain = Chain(10, 10);
        var left = F(2);
        var right = F(3);

        var result = SectionStationPlanner.Plan(chain,
            [new BeamSpanSectionProfile(1, left, left, left), new BeamSpanSectionProfile(2, right, right, right)],
            reduceUniformSpans: true);

        Assert.Equal(4, result.Count);
        var transition = result.Where(item => Math.Abs(item.ChainDistanceFeet - 10) < 0.2).ToList();
        Assert.Equal(2, transition.Count);
        Assert.Contains(transition, item => item.ChainDistanceFeet < 10);
        Assert.Contains(transition, item => item.ChainDistanceFeet > 10);
    }

    [Fact]
    public void Plan_TwoReducedSpansEquivalentCommonSupport_RemainsTwoMidStations()
    {
        var chain = Chain(10, 10);
        var uniform = F(2);

        var result = SectionStationPlanner.Plan(chain,
            [new BeamSpanSectionProfile(1, uniform, uniform, uniform),
             new BeamSpanSectionProfile(2, uniform, uniform, uniform)],
            reduceUniformSpans: true);

        Assert.Equal(2, result.Count);
        Assert.All(result, station => Assert.Equal(SectionStationKind.MidSpan, station.Kind));
    }

    private static BeamChainModel Chain(params double[] lengths)
    {
        var spans = new List<BeamSpanModel>();
        var x = 0d;
        for (var i = 0; i < lengths.Length; i++)
        {
            spans.Add(new BeamSpanModel(i + 1, i, new Point3(x, 0, 0),
                new Point3(x + lengths[i], 0, 0), lengths[i], 1, 2));
            x += lengths[i];
        }

        return new BeamChainModel(spans, new Point3(0, 0, 0), new Point3(x, 0, 0), x);
    }

    private static RebarStationFingerprint F(int quantity) => new(
        1, 2, [new RebarLayerFingerprint(0.1, 16, quantity)], [new StirrupZoneFingerprint(8, 0.3)], false);
}
