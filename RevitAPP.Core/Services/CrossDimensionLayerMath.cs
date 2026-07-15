namespace RevitAPP.Core.Services;

/// <summary>Orders vertical faces for a cross-section chain dimension and merges coincident elevations.</summary>
public static class CrossDimensionLayerMath
{
    /// <summary>
    /// Faces closer than this are treated as coincident. The project dimension style rounds tiny gaps to zero,
    /// therefore the merge tolerance must be at least its display precision.
    /// </summary>
    public const double CoincidentToleranceMm = 10.0;

    public static IReadOnlyList<double> OrderedUniqueLevels(IEnumerable<double> elevations, double tolerance)
    {
        if (!MathCompat.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));

        var sorted = elevations.Where(MathCompat.IsFinite).OrderBy(value => value).ToList();
        var clusters = new List<List<double>>();
        foreach (var value in sorted)
        {
            if (clusters.Count == 0 || Math.Abs(value - clusters[^1][^1]) > tolerance)
                clusters.Add([value]);
            else
                clusters[^1].Add(value);
        }

        // Preserve the true outer envelope: bottom cluster keeps its minimum, top cluster keeps its maximum.
        // Interior coincident clusters keep their lower face, matching the historical chain-dimension rule.
        return clusters.Select((cluster, index) =>
            index == clusters.Count - 1 && clusters.Count > 1 ? cluster[^1] : cluster[0]).ToList();
    }

    /// <summary>
    /// Selects the interior reference to remove when Revit formats one chain segment as zero.
    /// The first and last references are the overall envelope and must always be preserved.
    /// </summary>
    public static int ReferenceIndexToRemoveForZeroSegment(int referenceCount, int zeroSegmentIndex)
    {
        if (referenceCount < 3) throw new ArgumentOutOfRangeException(nameof(referenceCount));
        if (zeroSegmentIndex < 0 || zeroSegmentIndex >= referenceCount - 1)
            throw new ArgumentOutOfRangeException(nameof(zeroSegmentIndex));

        if (zeroSegmentIndex == 0) return 1;
        if (zeroSegmentIndex == referenceCount - 2) return referenceCount - 2;
        return zeroSegmentIndex + 1;
    }
}
