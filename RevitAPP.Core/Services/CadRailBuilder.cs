using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.Core.Services;

/// <summary>
/// A stretch of an axis that geometry covers.
/// </summary>
public readonly record struct CadRailInterval(double Start, double End);

/// <summary>
/// Where one drawn boundary lies along a rail, and which segment drew it.
/// </summary>
public readonly record struct CadRailSource(int SegmentId, double Start, double End);

/// <summary>
/// One drawn boundary of a beam or a wall: every collinear piece of it, gathered onto a single
/// line with a direction and an offset from the origin.
///
/// A boundary is rarely one segment in a drawing. It is trimmed at every column face, broken
/// where another element crosses it, and its pieces drift a millimetre or two apart when they are
/// copied or snapped. What matters is the line they all sit on, and the stretches of that line
/// they cover between them.
/// </summary>
public sealed record CadRail(
    int Id,
    CadStructurePoint2 Direction,
    CadStructurePoint2 Normal,
    double Offset,
    IReadOnlyList<CadRailInterval> Intervals,
    IReadOnlyList<int> SourceIds,
    IReadOnlyList<CadRailSource> Sources)
{
    /// <summary>
    /// The layer this boundary was drawn on. Two boundaries only describe one element when they
    /// were drawn together, and the layer is what says so: a wall face and a beam face can run
    /// parallel at exactly a wall's thickness and mean nothing to each other.
    /// </summary>
    public string Layer { get; init; } = string.Empty;


    public double Start => Intervals.Min(interval => interval.Start);
    public double End => Intervals.Max(interval => interval.End);
    public double CoveredLength => Intervals.Sum(interval => interval.End - interval.Start);
}

/// <summary>
/// Gathers loose CAD segments into the boundaries they draw.
///
/// Both beams and walls are drawn the same way -- two parallel boundaries with the section
/// between them -- so both are read from the same rails. What differs is what is made of a pair
/// once it is found, which is left to the caller.
/// </summary>
public static class CadRailBuilder
{
    /// <summary>
    /// Directions within this many degrees of one another are the same direction. A boundary
    /// drawn in pieces rarely repeats its angle exactly, and a degree of drift is enough to lose
    /// a beam if the pieces are read as separate lines.
    /// </summary>
    public const double AngleBucketDegrees = 5.0;

    /// <summary>
    /// The boundaries a set of segments draws.
    /// </summary>
    /// <param name="segments">Segments to gather, already in millimetres.</param>
    /// <param name="gapToleranceMm">
    /// Largest break along a boundary that still reads as one stretch. A boundary trimmed at a
    /// column face leaves gaps this wide.
    /// </param>
    /// <param name="offsetToleranceMm">
    /// How far apart two pieces may sit across the line and still belong to it.
    /// </param>
    public static IReadOnlyList<CadRail> Build(
        IReadOnlyList<CadStructureSegment> segments,
        double gapToleranceMm,
        double offsetToleranceMm)
    {
        var pieces = segments.Select(segment =>
        {
            var vector = segment.End - segment.Start;
            var length = Length(vector);
            var direction = CanonicalDirection(vector * (1.0 / length));
            var normal = new CadStructurePoint2(-direction.Y, direction.X);
            return (Segment: segment, Direction: direction, Normal: normal,
                Offset: Dot(segment.Start, normal));
        }).ToArray();

        // Cluster by actual distance rather than by rounding the offset into fixed cells: two
        // pieces of one drawn boundary must stay together even when their offsets fall either
        // side of a cell edge, which would otherwise split a beam over a millimetre of drift.
        var groups = pieces
            // Boundaries on different layers never belong to the same element, so a layer is
            // part of what makes a rail rather than something checked afterwards.
            .GroupBy(piece => (Layer: piece.Segment.Layer ?? string.Empty,
                Angle: (int)Math.Round(Angle(piece.Direction) / AngleBucketDegrees)))
            .SelectMany(family =>
            {
                var clusters = new List<List<(CadStructureSegment Segment,
                    CadStructurePoint2 Direction, CadStructurePoint2 Normal, double Offset)>>();
                foreach (var piece in family.OrderBy(item => item.Offset))
                {
                    var current = clusters.Count == 0 ? null : clusters[^1];
                    if (current is not null && piece.Offset - current[^1].Offset <= offsetToleranceMm)
                        current.Add(piece);
                    else
                        clusters.Add(new() { piece });
                }
                return clusters;
            })
            .ToArray();

        return groups.Select((group, index) =>
        {
            var direction = Normalize(new CadStructurePoint2(
                group.Average(piece => piece.Direction.X),
                group.Average(piece => piece.Direction.Y)));
            var normal = new CadStructurePoint2(-direction.Y, direction.X);
            var offset = group.Average(piece =>
                (Dot(piece.Segment.Start, normal) + Dot(piece.Segment.End, normal)) / 2.0);

            var intervals = MergeIntervals(group.Select(piece =>
            {
                var a = Dot(piece.Segment.Start, direction);
                var b = Dot(piece.Segment.End, direction);
                return new CadRailInterval(Math.Min(a, b), Math.Max(a, b));
            }), gapToleranceMm);

            var sources = group.Select(piece =>
            {
                var a = Dot(piece.Segment.Start, direction);
                var b = Dot(piece.Segment.End, direction);
                return new CadRailSource(piece.Segment.Id, Math.Min(a, b), Math.Max(a, b));
            }).ToArray();

            return new CadRail(index + 1, direction, normal, offset, intervals,
                group.Select(piece => piece.Segment.Id).Distinct().ToArray(), sources)
            {
                Layer = group[0].Segment.Layer ?? string.Empty
            };
        }).ToArray();
    }

    /// <summary>
    /// Runs of stretches joined where the break between them is no wider than the given gap.
    /// </summary>
    public static IReadOnlyList<CadRailInterval> MergeIntervals(
        IEnumerable<CadRailInterval> source,
        double gap)
    {
        var ordered = source.OrderBy(item => item.Start).ToArray();
        if (ordered.Length == 0) return Array.Empty<CadRailInterval>();

        var merged = new List<CadRailInterval> { ordered[0] };
        foreach (var current in ordered.Skip(1))
        {
            var last = merged[^1];
            if (current.Start <= last.End + gap)
                merged[^1] = new CadRailInterval(last.Start, Math.Max(last.End, current.End));
            else
                merged.Add(current);
        }
        return merged;
    }

    /// <summary>
    /// Stretches where two rails face one another, so a section could span between them.
    ///
    /// A rail collects every collinear boundary in the drawing, so a short stub sharing a line
    /// with a long run would otherwise inherit that run's extent. Stations covered by either rail
    /// stay in one stretch, which keeps staggered fragments and interior gaps continuous; a
    /// stretch ends only where neither rail has geometry.
    /// </summary>
    public static IReadOnlyList<CadRailInterval> FacingIntervals(
        CadRail first,
        CadRail second,
        double gap) =>
        MergeIntervals(first.Intervals.Concat(second.Intervals), gap);

    public static double Dot(CadStructurePoint2 first, CadStructurePoint2 second) =>
        first.X * second.X + first.Y * second.Y;

    public static double Cross(CadStructurePoint2 first, CadStructurePoint2 second) =>
        first.X * second.Y - first.Y * second.X;

    public static double Length(CadStructurePoint2 value) => Math.Sqrt(Dot(value, value));

    public static CadStructurePoint2 Normalize(CadStructurePoint2 value)
    {
        var length = Length(value);
        return length <= 1e-12 ? new CadStructurePoint2(1, 0) : value * (1.0 / length);
    }

    /// <summary>
    /// A direction turned to face one way, so that a line and the same line drawn backwards read
    /// as the same boundary.
    /// </summary>
    public static CadStructurePoint2 CanonicalDirection(CadStructurePoint2 direction) =>
        direction.X < -1e-12 || Math.Abs(direction.X) <= 1e-12 && direction.Y < 0
            ? direction * -1
            : direction;

    /// <summary>
    /// The angle of a direction in degrees, folded into a half turn: a line has no front or back.
    /// </summary>
    public static double Angle(CadStructurePoint2 direction)
    {
        var angle = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;
        while (angle < 0) angle += 180.0;
        while (angle >= 180) angle -= 180.0;
        return angle;
    }

    public static double AngleDifference(CadStructurePoint2 first, CadStructurePoint2 second)
    {
        var difference = Math.Abs(Angle(first) - Angle(second));
        return Math.Min(difference, 180.0 - difference);
    }
}
