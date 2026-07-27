using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Core.Services;

public static class LongitudinalDimensionPlanner
{
    public static LongitudinalDimensionPlan Plan(
        LongitudinalDimensionInput input,
        double duplicateToleranceFeet)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (duplicateToleranceFeet < 0)
            throw new ArgumentOutOfRangeException(nameof(duplicateToleranceFeet));

        return new LongitudinalDimensionPlan(
            Normalize(input.UpperWitnesses, duplicateToleranceFeet),
            Normalize(input.LowerWitnesses, duplicateToleranceFeet));
    }

    private static IReadOnlyList<DimensionWitness> Normalize(
        IReadOnlyList<DimensionWitness> witnesses,
        double tolerance)
    {
        var result = new List<DimensionWitness>();
        foreach (var witness in witnesses.OrderBy(item => item.ChainDistanceFeet).ThenBy(item => item.Kind))
        {
            if (!MathCompat.IsFinite(witness.ChainDistanceFeet)) continue;
            if (result.Count == 0 || witness.ChainDistanceFeet - result[^1].ChainDistanceFeet > tolerance)
            {
                result.Add(witness);
                continue;
            }

            var existing = result[^1];
            result[^1] = existing with
            {
                Roles = existing.Roles.Concat(witness.Roles).Distinct().OrderBy(role => role).ToList(),
                Label = MergeLabels(existing.Label, witness.Label)
            };
        }
        return result;
    }

    private static string MergeLabels(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second;
        if (string.IsNullOrWhiteSpace(second) || string.Equals(first, second, StringComparison.Ordinal)) return first;
        return first + " | " + second;
    }
}
