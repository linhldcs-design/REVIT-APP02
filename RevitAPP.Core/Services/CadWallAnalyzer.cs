using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.Core.Services;

/// <summary>
/// Turns a scanned plan into the walls it draws.
///
/// A wall reaches the reader as two parallel boundaries with the wall between them, or as a
/// closed rectangle whose short side is its thickness. Nothing is read from text: the thickness
/// is measured between the boundaries, and the height comes from the levels chosen in Revit.
///
/// A beam is drawn exactly the same way, and so is a pair of dimension lines. No amount of
/// geometry tells them apart, so the user says which layers draw walls and the rest is left
/// alone.
/// </summary>
public static class CadWallAnalyzer
{
    // Names that suggest a layer draws walls. Only used to tick the box for the user: a guess
    // that decided on its own once turned away a whole scan, when the grid axes turned out to
    // sit on a layer called S-GRID.
    private static readonly string[] WallLayerHints =
    {
        "WALL", "TUONG", "TƯỜNG", "VACH", "VÁCH"
    };

    public static CadWallAnalysis Analyze(
        CadStructureTransferPackage package,
        CadWallAnalysisOptions? options = null)
    {
        options ??= new CadWallAnalysisOptions();
        var validation = Validate(options);
        if (validation is not null) return Invalid(validation);

        double scale;
        try
        {
            scale = CadGridUnitConverter.MillimetresPerDrawingUnit(package.InsUnits);
        }
        catch (InvalidDataException exception)
        {
            return Invalid(exception.Message);
        }

        var scaled = package.Segments
            .Where(segment => Finite(segment.Start) && Finite(segment.End))
            .Select(segment => segment with
            {
                Start = segment.Start * scale,
                End = segment.End * scale
            })
            .Where(segment => segment.Start.DistanceTo(segment.End) > 1e-6)
            .ToArray();
        if (scaled.Length == 0) return Invalid("Vùng chọn Tường không có LINE/POLYLINE hợp lệ.");

        var origin = new CadStructurePoint2(
            scaled.Min(segment => Math.Min(segment.Start.X, segment.End.X)),
            scaled.Min(segment => Math.Min(segment.Start.Y, segment.End.Y)));
        var segments = scaled
            .Select(segment => segment with
            {
                Start = segment.Start - origin,
                End = segment.End - origin
            })
            .ToArray();

        var layers = segments
            .GroupBy(segment => segment.Layer ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => new CadLayerTally(group.Key, group.Count())
            {
                SuggestedAsWall = LooksLikeWallLayer(group.Key)
            })
            .ToArray();

        var warnings = new List<string>();
        var anchor = package.SourceAnchor * scale - origin;

        if (options.WallLayers.Count == 0)
            return new CadWallAnalysis(origin, anchor, Array.Empty<CadWallCandidate>(),
                new[] { "Chưa chọn layer nào là tường — tick layer ở bảng bên phải rồi Apply." },
                null)
            {
                Layers = layers
            };

        var picked = options.WallLayers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wallSegments = segments
            .Where(segment => picked.Contains(segment.Layer ?? string.Empty))
            .ToArray();
        if (wallSegments.Length == 0)
            return new CadWallAnalysis(origin, anchor, Array.Empty<CadWallCandidate>(),
                new[] { "Layer đã chọn không có đối tượng nào trong vùng quét." }, null)
            {
                Layers = layers
            };

        var walls = new List<CadWallCandidate>();
        var claimed = new HashSet<int>();

        // A rectangle says on its own how thick the wall is and where it runs, so it is read
        // first. Its four sides are then kept out of the pairing below whether or not it turned
        // out to be a wall: a column drawn as a square would otherwise come back as a pair of
        // parallel boundaries and be built as a wall of no length.
        foreach (var rectangle in ClosedRectangles(wallSegments, options))
        {
            foreach (var id in rectangle.SideIds) claimed.Add(id);
            if (rectangle.Candidate is not null) walls.Add(rectangle.Candidate);
        }

        var remaining = wallSegments.Where(segment => !claimed.Contains(segment.Id)).ToArray();
        var rails = CadRailBuilder.Build(
            remaining, options.GapJoinToleranceMm, options.RailOffsetToleranceMm);
        walls.AddRange(PairRails(rails, options));

        walls = walls
            .Select((wall, index) => wall with { Id = index + 1 })
            .ToList();

        // A room drawn as four walls comes out with four open corners unless each centre line is
        // carried on to meet the next.
        var joined = JoinAtCorners(walls, options.JoinDistanceMm)
            .Where(wall => wall.LengthMm >= options.MinimumLengthMm)
            .Select((wall, index) => wall with { Id = index + 1 })
            .ToArray();

        if (joined.Length == 0)
            warnings.Add("Không dựng được tường nào từ layer đã chọn — "
                + $"kiểm tra bề dày có nằm trong {options.MinimumThicknessMm:0}–"
                + $"{options.MaximumThicknessMm:0} mm không.");

        return new CadWallAnalysis(origin, anchor, joined, warnings, null) { Layers = layers };
    }

    /// <summary>
    /// Whether a layer's name suggests it draws walls.
    /// </summary>
    public static bool LooksLikeWallLayer(string layer) =>
        !string.IsNullOrWhiteSpace(layer)
        && WallLayerHints.Any(hint => layer.Contains(hint, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Walls drawn as closed rectangles. A rectangle whose sides are too near equal is a column
    /// standing in the plan, not a wall running along it.
    /// </summary>
    private static IEnumerable<(CadWallCandidate? Candidate, int[] SideIds)> ClosedRectangles(
        IReadOnlyList<CadStructureSegment> segments,
        CadWallAnalysisOptions options)
    {
        // A rectangle drawn as one polyline arrives with a source path its four sides share; one
        // drawn as four separate lines has none, so those are gathered by geometry instead.
        var byPath = segments
            .Where(segment => !string.IsNullOrEmpty(segment.SourcePath))
            .GroupBy(segment => segment.SourcePath!)
            .Select(group => group.ToArray());
        // Four loose lines only draw one rectangle when they were drawn together, so they are
        // gathered a layer at a time.
        var loose = segments
            .Where(segment => string.IsNullOrEmpty(segment.SourcePath))
            .GroupBy(segment => segment.Layer ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => LooseRectangles(group.ToArray(), options.RailOffsetToleranceMm));

        foreach (var sides in byPath.Concat(loose))
        {
            if (sides.Length != 4) continue;

            var corners = ClosedLoop(sides, options.RailOffsetToleranceMm);
            if (corners is null || corners.Count != 4) continue;

            var sideIds = sides.Select(side => side.Id).ToArray();

            var first = corners[0].DistanceTo(corners[1]);
            var second = corners[1].DistanceTo(corners[2]);
            var third = corners[2].DistanceTo(corners[3]);
            var fourth = corners[3].DistanceTo(corners[0]);

            // Opposite sides of a rectangle match; anything else is a quadrilateral that only
            // looks like one, and its sides may still pair up as walls.
            if (Math.Abs(first - third) > options.RailOffsetToleranceMm) continue;
            if (Math.Abs(second - fourth) > options.RailOffsetToleranceMm) continue;

            var length = Math.Max(first, second);
            var thickness = Math.Min(first, second);

            // A rectangle only draws a wall when it is narrow: its short side is the thickness.
            // The four sides of a room are a rectangle too, and a wide one belongs to the pairing
            // below, where each of its sides meets the facing side of the room's other face.
            if (thickness > options.MaximumThicknessMm) continue;

            // From here the four sides belong to this rectangle whatever it turns out to be. A
            // column is claimed and dropped rather than left for the pairing to find, where it
            // would pair its own opposite sides into a wall of no length.
            if (thickness < options.MinimumThicknessMm
                || length < options.MinimumLengthMm
                || length < thickness * options.MinimumLengthRatio)
            {
                yield return (null, sideIds);
                continue;
            }

            // The centre line joins the middles of the two short sides.
            var (startIndex, endIndex) = first >= second ? (3, 1) : (0, 2);
            var start = Midpoint(corners[startIndex], corners[(startIndex + 1) % 4]);
            var end = Midpoint(corners[endIndex], corners[(endIndex + 1) % 4]);

            yield return (new CadWallCandidate(0, start, end, thickness,
                CadWallSource.Rectangle, sideIds, CadWallCandidateStatus.Ready), sideIds);
        }
    }

    /// <summary>
    /// Rectangles drawn as four separate lines rather than as one polyline. Four lines that join
    /// end to end and come back where they started are one shape, whatever they were drawn with.
    /// </summary>
    private static IEnumerable<CadStructureSegment[]> LooseRectangles(
        IReadOnlyList<CadStructureSegment> segments,
        double toleranceMm)
    {
        var remaining = segments.ToList();
        while (remaining.Count >= 4)
        {
            var loop = new List<CadStructureSegment> { remaining[0] };
            remaining.RemoveAt(0);
            var tail = loop[0].End;
            var head = loop[0].Start;

            while (loop.Count < 4)
            {
                var index = remaining.FindIndex(segment =>
                    segment.Start.DistanceTo(tail) <= toleranceMm
                    || segment.End.DistanceTo(tail) <= toleranceMm);
                if (index < 0) break;

                var next = remaining[index];
                remaining.RemoveAt(index);
                tail = next.Start.DistanceTo(tail) <= toleranceMm ? next.End : next.Start;
                loop.Add(next);
            }

            if (loop.Count == 4 && tail.DistanceTo(head) <= toleranceMm)
                yield return loop.ToArray();
            else
                // Not a closed run of four: the pieces go back for the pairing to consider.
                remaining.AddRange(loop.Skip(1));
        }
    }

    /// <summary>
    /// The corners of a loop the segments draw, when they join end to end and come back to where
    /// they started. Null when they do not close.
    /// </summary>
    private static IReadOnlyList<CadStructurePoint2>? ClosedLoop(
        IReadOnlyList<CadStructureSegment> segments,
        double toleranceMm)
    {
        var remaining = segments.ToList();
        var first = remaining[0];
        remaining.RemoveAt(0);
        var points = new List<CadStructurePoint2> { first.Start, first.End };

        while (remaining.Count > 0)
        {
            var tail = points[^1];
            var index = remaining.FindIndex(segment =>
                segment.Start.DistanceTo(tail) <= toleranceMm
                || segment.End.DistanceTo(tail) <= toleranceMm);
            if (index < 0) return null;

            var next = remaining[index];
            remaining.RemoveAt(index);
            points.Add(next.Start.DistanceTo(tail) <= toleranceMm ? next.End : next.Start);
        }

        if (points[^1].DistanceTo(points[0]) > toleranceMm) return null;
        points.RemoveAt(points.Count - 1);
        return points;
    }

    /// <summary>
    /// Walls drawn as two parallel boundaries. Each pair of rails facing one another at a
    /// plausible distance makes a wall down the middle of them.
    /// </summary>
    private static IEnumerable<CadWallCandidate> PairRails(
        IReadOnlyList<CadRail> rails,
        CadWallAnalysisOptions options)
    {
        var used = new HashSet<int>();

        // Only boundaries drawn together describe one wall, and the layer is what says they were.
        // Two layers can both draw walls without a face of one ever pairing with a face of the
        // other, however square and however far apart they happen to run.
        foreach (var family in rails.GroupBy(rail =>
                     (rail.Layer,
                         Angle: (int)Math.Round(CadRailBuilder.Angle(rail.Direction)
                                                / CadRailBuilder.AngleBucketDegrees))))
        {
            var ordered = family.OrderBy(rail => rail.Offset).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var first = ordered[index];
                if (used.Contains(first.Id)) continue;

                for (var other = index + 1; other < ordered.Length; other++)
                {
                    var second = ordered[other];
                    if (used.Contains(second.Id)) continue;

                    var thickness = Math.Abs(second.Offset - first.Offset);
                    if (thickness < options.MinimumThicknessMm) continue;
                    if (thickness > options.MaximumThicknessMm) break;

                    var facing = CadRailBuilder.FacingIntervals(
                        first, second, options.GapJoinToleranceMm);
                    var span = facing
                        .OrderByDescending(interval => interval.End - interval.Start)
                        .FirstOrDefault();
                    var length = span.End - span.Start;
                    if (length < options.MinimumLengthMm) continue;

                    // The centre line runs along the axis the rails share, halfway between them.
                    var direction = first.Direction;
                    var normal = first.Normal;
                    var offset = (first.Offset + second.Offset) / 2.0;
                    var start = direction * span.Start + normal * offset;
                    var end = direction * span.End + normal * offset;

                    used.Add(first.Id);
                    used.Add(second.Id);
                    yield return new CadWallCandidate(0, start, end, thickness,
                        CadWallSource.ParallelLines,
                        first.SourceIds.Concat(second.SourceIds).Distinct().ToArray(),
                        CadWallCandidateStatus.Ready);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Walls whose centre lines have been carried on to meet at the corners.
    ///
    /// Two walls meeting at a corner are drawn face to face, so their centre lines stop short of
    /// one another by half a thickness each. Left that way, Revit builds a room with four open
    /// corners.
    /// </summary>
    private static IReadOnlyList<CadWallCandidate> JoinAtCorners(
        IReadOnlyList<CadWallCandidate> walls,
        double reachMm)
    {
        if (walls.Count < 2) return walls;

        var ends = walls
            .Select(wall => new[] { wall.StartMm, wall.EndMm })
            .ToArray();

        for (var index = 0; index < walls.Count; index++)
        for (var other = index + 1; other < walls.Count; other++)
        {
            var crossing = Crossing(
                ends[index][0], ends[index][1], ends[other][0], ends[other][1]);
            if (crossing is null) continue;

            // Only ends near the crossing are moved: a wall crossed in its middle carries on
            // through, and Revit joins the two where they meet.
            for (var side = 0; side < 2; side++)
            {
                if (ends[index][side].DistanceTo(crossing.Value) <= reachMm)
                    ends[index][side] = crossing.Value;
                if (ends[other][side].DistanceTo(crossing.Value) <= reachMm)
                    ends[other][side] = crossing.Value;
            }
        }

        return walls
            .Select((wall, index) => wall with
            {
                StartMm = ends[index][0],
                EndMm = ends[index][1]
            })
            .ToArray();
    }

    /// <summary>
    /// Where two infinite lines cross, or null when they run parallel.
    /// </summary>
    private static CadStructurePoint2? Crossing(
        CadStructurePoint2 firstStart,
        CadStructurePoint2 firstEnd,
        CadStructurePoint2 secondStart,
        CadStructurePoint2 secondEnd)
    {
        var first = firstEnd - firstStart;
        var second = secondEnd - secondStart;
        var denominator = CadRailBuilder.Cross(first, second);
        if (Math.Abs(denominator) < 1e-9) return null;

        var along = CadRailBuilder.Cross(secondStart - firstStart, second) / denominator;
        return firstStart + first * along;
    }

    private static CadStructurePoint2 Midpoint(CadStructurePoint2 first, CadStructurePoint2 second) =>
        new((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0);

    private static string? Validate(CadWallAnalysisOptions options)
    {
        if (!Finite(options.MinimumThicknessMm) || options.MinimumThicknessMm <= 0)
            return "Bề dày nhỏ nhất không hợp lệ.";
        if (!Finite(options.MaximumThicknessMm)
            || options.MaximumThicknessMm < options.MinimumThicknessMm)
            return "Bề dày lớn nhất phải lớn hơn bề dày nhỏ nhất.";
        if (!Finite(options.MinimumLengthMm) || options.MinimumLengthMm < 0)
            return "Chiều dài nhỏ nhất không hợp lệ.";
        if (!Finite(options.MinimumLengthRatio) || options.MinimumLengthRatio < 1)
            return "Tỷ lệ dài/dày phải từ 1 trở lên.";
        return null;
    }

    private static CadWallAnalysis Invalid(string error) =>
        new(default, default, Array.Empty<CadWallCandidate>(), Array.Empty<string>(), error);

    private static bool Finite(CadStructurePoint2 point) => Finite(point.X) && Finite(point.Y);

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
