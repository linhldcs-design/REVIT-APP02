using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.Core.Services;

/// <summary>
/// Turns the curved side of a polyline into the straight chords that follow it.
///
/// AutoCAD stores a curved side as a bulge: the tangent of a quarter of the angle the arc sweeps,
/// signed so that a positive bulge turns counter-clockwise from the start of the side to its end.
/// Everything the arc needs -- how far it turns, how big it is, where its centre sits -- follows
/// from that one number and the two ends of the side.
/// </summary>
public static class CadArcChords
{
    /// <summary>
    /// The points along a curved side, starting at <paramref name="start"/> and ending exactly on
    /// <paramref name="end"/>. A side with no bulge gives back just its two ends.
    /// </summary>
    public static IReadOnlyList<CadStructurePoint2> Trace(
        CadStructurePoint2 start,
        CadStructurePoint2 end,
        double bulge,
        double maximumStepDegrees = 5.0)
    {
        var chordX = end.X - start.X;
        var chordY = end.Y - start.Y;
        var chord = Math.Sqrt(chordX * chordX + chordY * chordY);
        // double.IsFinite is not available on the framework the older releases target.
        if (chord < 1e-9 || Math.Abs(bulge) < 1e-9
            || double.IsNaN(bulge) || double.IsInfinity(bulge))
            return new[] { start, end };

        var sweep = 4.0 * Math.Atan(bulge);
        var radius = chord / (2.0 * Math.Sin(sweep / 2.0));

        // The centre sits away from the middle of the chord along its normal, by as much as the
        // radius leans back from the apex. Radius and offset both carry the sign of the sweep, so
        // one expression places the centre whichever way the arc turns and however far it goes.
        var normalX = -chordY / chord;
        var normalY = chordX / chord;
        var offset = radius * Math.Cos(sweep / 2.0);
        var centreX = (start.X + end.X) / 2.0 - offset * normalX;
        var centreY = (start.Y + end.Y) / 2.0 - offset * normalY;

        var from = Math.Atan2(start.Y - centreY, start.X - centreX);
        var length = Math.Abs(radius);
        var steps = Math.Max(2, (int)Math.Ceiling(
            Math.Abs(sweep) / (maximumStepDegrees * Math.PI / 180.0)));

        var points = new List<CadStructurePoint2>(steps + 1) { start };
        for (var step = 1; step < steps; step++)
        {
            var angle = from - sweep * step / steps;
            points.Add(new CadStructurePoint2(
                centreX + length * Math.Cos(angle),
                centreY + length * Math.Sin(angle)));
        }
        points.Add(end);
        return points;
    }
}
