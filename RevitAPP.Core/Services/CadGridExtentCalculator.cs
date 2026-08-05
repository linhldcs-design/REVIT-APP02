using RevitAPP.Core.Models.CadGrid;

namespace RevitAPP.Core.Services;

/// <summary>
/// Decides how far an existing grid must reach to span a set of crossing grids. Kept free
/// of Revit types so the endpoint arithmetic — the part that decides whether a grid grows,
/// shrinks or drifts sideways — is testable outside Revit.
/// </summary>
public static class CadGridExtentCalculator
{
    /// <summary>
    /// Returns the lengthened segment, or null when the line already spans the crossing
    /// grids. The line only ever grows along its own axis: it is never shortened and never
    /// moved sideways, because a shifted grid silently relocates the model.
    /// </summary>
    /// <param name="crossingPoints">
    /// Endpoints of every grid that runs across this one, in the same coordinate space.
    /// </param>
    public static (CadGridPoint2 Start, CadGridPoint2 End)? Extend(
        CadGridPoint2 start,
        CadGridPoint2 end,
        IReadOnlyList<CadGridPoint2> crossingPoints,
        double margin)
    {
        if (crossingPoints.Count == 0) return null;

        var dx = end.Xmm - start.Xmm;
        var dy = end.Ymm - start.Ymm;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0) throw new ArgumentException("Grid có độ dài bằng 0.");

        var direction = new CadGridPoint2(dx / length, dy / length);

        // Project everything onto this line's own axis so the comparison is one
        // dimensional and cannot introduce a sideways component.
        var startAt = Project(start, direction);
        var endAt = Project(end, direction);
        var currentMin = Math.Min(startAt, endAt);
        var currentMax = Math.Max(startAt, endAt);

        var projections = crossingPoints.Select(point => Project(point, direction)).ToArray();
        var requiredMin = projections.Min() - margin;
        var requiredMax = projections.Max() + margin;

        var newMin = Math.Min(currentMin, requiredMin);
        var newMax = Math.Max(currentMax, requiredMax);

        const double tolerance = 1e-9;
        if (newMin >= currentMin - tolerance && newMax <= currentMax + tolerance) return null;

        // Rebuild from a point on the line with its own axial offset removed, so the
        // result stays exactly on the original infinite line.
        var basePoint = start - direction * startAt;
        return (basePoint + direction * newMin, basePoint + direction * newMax);
    }

    private static double Project(CadGridPoint2 point, CadGridPoint2 direction) =>
        point.Xmm * direction.Xmm + point.Ymm * direction.Ymm;
}
