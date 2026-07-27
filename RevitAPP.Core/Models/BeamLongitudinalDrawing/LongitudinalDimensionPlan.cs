namespace RevitAPP.Core.Models.BeamLongitudinalDrawing;

public enum DimensionWitnessKind
{
    RebarZone,
    StirrupZone,
    SupportFace,
    SpanBoundary,
    Grid
}

public sealed record DimensionWitness(
    double ChainDistanceFeet,
    DimensionWitnessKind Kind,
    string Label)
{
    /// <summary>Một reference hình học có thể đồng thời là mép gối, biên nhịp và trục.</summary>
    public IReadOnlyList<DimensionWitnessKind> Roles { get; init; } = [Kind];
}

public sealed record LongitudinalDimensionInput(
    IReadOnlyList<DimensionWitness> UpperWitnesses,
    IReadOnlyList<DimensionWitness> LowerWitnesses);

public sealed record LongitudinalDimensionPlan(
    IReadOnlyList<DimensionWitness> Upper,
    IReadOnlyList<DimensionWitness> Lower);
