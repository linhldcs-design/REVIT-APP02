using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.BeamLongitudinalDrawing;

public sealed class LongitudinalDimensionPlannerTests
{
    [Fact]
    public void Plan_UnorderedDuplicateWitnesses_SortsAndDeduplicatesEachLayer()
    {
        var input = new LongitudinalDimensionInput(
            UpperWitnesses:
            [
                new DimensionWitness(10, DimensionWitnessKind.StirrupZone, "A"),
                new DimensionWitness(0, DimensionWitnessKind.RebarZone, "B"),
                new DimensionWitness(10.004, DimensionWitnessKind.RebarZone, "duplicate")
            ],
            LowerWitnesses:
            [
                new DimensionWitness(20, DimensionWitnessKind.SpanBoundary, "end"),
                new DimensionWitness(0, DimensionWitnessKind.Grid, "start"),
                new DimensionWitness(10, DimensionWitnessKind.SupportFace, "support")
            ]);

        var result = LongitudinalDimensionPlanner.Plan(input, 0.01);

        Assert.Equal(new[] { 0d, 10d }, result.Upper.Select(x => x.ChainDistanceFeet));
        Assert.Equal(new[] { 0d, 10d, 20d }, result.Lower.Select(x => x.ChainDistanceFeet));
        Assert.Equal(2, result.Upper[1].Roles.Count);
        Assert.Contains(DimensionWitnessKind.StirrupZone, result.Upper[1].Roles);
        Assert.Contains(DimensionWitnessKind.RebarZone, result.Upper[1].Roles);
    }

    [Fact]
    public void Plan_NegativeTolerance_Throws()
    {
        var input = new LongitudinalDimensionInput([], []);

        Assert.Throws<ArgumentOutOfRangeException>(() => LongitudinalDimensionPlanner.Plan(input, -0.01));
    }
}
