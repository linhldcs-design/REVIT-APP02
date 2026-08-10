using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.Core.Services;

/// <summary>
/// Turns loose CAD segments into the smallest areas they enclose.
///
/// Drawn lines overrun their corners, stop short of them, and cross without a vertex, so the
/// segments are first split at every crossing and their ends merged onto shared vertices. Walking
/// the resulting graph by always taking the next edge clockwise traces each bounded face exactly
/// once; a dangling line belongs to no face and drops out on its own.
/// </summary>
public static class CadPlanarGraph
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// The outline round everything the scan covers, taken from the lines themselves rather than
    /// assembled from the areas they enclose.
    ///
    /// Every line is split at its crossings and its ends merged, then the walk starts at the
    /// leftmost vertex and keeps to the outside by turning as far clockwise as it can at each
    /// junction. That traces the outer face of the drawing in one pass, whatever the bays inside
    /// it look like.
    /// </summary>
    public static CadSlabLoop? BuildOuterBoundary(
        IReadOnlyList<CadStructureSegment> segmentsMm,
        double snapToleranceMm,
        double maximumNotchDepthMm = 0.0)
    {
        var split = SplitAtIntersections(segmentsMm);
        var vertices = new List<CadStructurePoint2>();
        var edges = new List<(int A, int B)>();

        foreach (var segment in split)
        {
            var a = SnapVertex(vertices, segment.Start, snapToleranceMm);
            var b = SnapVertex(vertices, segment.End, snapToleranceMm);
            if (a == b) continue;
            if (edges.Any(edge => (edge.A == a && edge.B == b) || (edge.A == b && edge.B == a)))
                continue;
            edges.Add((a, b));
        }
        if (vertices.Count < 3) return null;

        var adjacency = new List<List<int>>();
        for (var index = 0; index < vertices.Count; index++) adjacency.Add(new List<int>());
        foreach (var (a, b) in edges)
        {
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        // The leftmost vertex is on the outline by definition, and the edge leaving it that points
        // most steeply downwards is the one running along the outside.
        var start = 0;
        for (var index = 1; index < vertices.Count; index++)
        {
            if (adjacency[index].Count == 0) continue;
            if (vertices[index].X < vertices[start].X
                || (Math.Abs(vertices[index].X - vertices[start].X) < Epsilon
                    && vertices[index].Y < vertices[start].Y))
                start = index;
        }
        if (adjacency[start].Count == 0) return null;

        var first = adjacency[start]
            .OrderBy(neighbour => Math.Atan2(
                vertices[neighbour].Y - vertices[start].Y,
                vertices[neighbour].X - vertices[start].X))
            .First();

        var loop = new List<int> { start };
        var previous = start;
        var current = first;
        var guard = edges.Count * 4 + 8;
        while (guard-- > 0)
        {
            loop.Add(current);
            var next = NextAroundOutside(vertices, adjacency, current, previous);
            if (next < 0) return null;
            if (current == start && next == first) break;
            previous = current;
            current = next;
            if (current == start && previous == first) break;
        }
        if (loop.Count < 4) return null;
        if (loop[^1] == loop[0]) loop.RemoveAt(loop.Count - 1);
        if (loop.Count < 3) return null;

        // Walking the outside goes up a spur and back down it again, which leaves the same vertex
        // twice and a pair of edges lying on top of each other. Revit reads that as a profile
        // crossing itself, so the excursion is removed and only the loop around it kept.
        loop = RemoveExcursions(loop);
        if (loop.Count < 3) return null;

        var points = loop.Select(index => vertices[index]).ToList();
        // Columns are drawn on the grid lines, so the walk round the outside steps in and out again
        // at every one of them and leaves the edge saw-toothed. A notch that shallow is the column,
        // not the shape of the floor -- the floor is cast round its columns, edge included.
        points = SmoothNotches(points, maximumNotchDepthMm);
        // Straightening a notch leaves its two ends sitting on the line that replaced it. They
        // draw the same edge but make the outline look stepped in the review, so they go too.
        points = DropCollinear(points);
        if (points.Count < 3) return null;

        var outline = new CadSlabLoop(points);
        return outline.SignedAreaMm2 < 0
            ? new CadSlabLoop(outline.VerticesMm.Reverse().ToArray())
            : outline;
    }

    /// <summary>
    /// Straightens the edge across notches shallower than the given depth: where the outline steps
    /// aside and comes back to the line it was on, the step is dropped and the line runs through.
    /// </summary>
    /// <summary>
    /// Drops a corner that lies on the line between its neighbours: it turns the edge nowhere
    /// and only makes the outline look stepped where it is straight.
    /// </summary>
    private static List<CadStructurePoint2> DropCollinear(List<CadStructurePoint2> points)
    {
        var changed = true;
        while (changed && points.Count > 3)
        {
            changed = false;
            for (var index = 0; index < points.Count; index++)
            {
                var before = points[(index - 1 + points.Count) % points.Count];
                var after = points[(index + 1) % points.Count];
                if (PointToSegment(before, after, points[index]) > 1.0) continue;
                points.RemoveAt(index);
                changed = true;
                break;
            }
        }
        return points;
    }

    private static List<CadStructurePoint2> SmoothNotches(
        List<CadStructurePoint2> points,
        double maximumDepthMm)
    {
        if (maximumDepthMm <= 0.0 || points.Count < 4) return points;

        var changed = true;
        while (changed && points.Count >= 4)
        {
            changed = false;
            for (var start = 0; start < points.Count && !changed; start++)
            {
                var before = points[start];
                // A detour off a straight run can turn several corners before it rejoins -- going
                // out, along and back is three of them, and a column drawn by its four faces gives
                // four. Looking only one corner ahead left every one of them in the edge, so the
                // run is followed until it comes back to the line it left.
                var maximumCorners = Math.Min(6, points.Count - 2);
                for (var span = 1; span <= maximumCorners; span++)
                {
                    var after = points[(start + span + 1) % points.Count];
                    // The run the edge was on before the detour, and the run it is on after: those are what
                    // have to line up. Comparing the step into the detour with the step out of it instead
                    // compared two edges of the beam end itself, which run parallel to each other and square
                    // to the wall -- so a real notch never matched and the edge kept every one of them.
                    var beforeRun = points[(start - 1 + points.Count) % points.Count];
                    var afterRun = points[(start + span + 2) % points.Count];
                    if (!SameDirection(beforeRun, before, after, afterRun)) continue;

                    // Every corner of the detour sits close to the line the edge would take, and
                    // the detour is short -- a column is about that wide -- or it is a feature of
                    // the plan and closing it would cut a real corner off the floor.
                    var shallow = true;
                    for (var step = 1; step <= span && shallow; step++)
                        shallow = PointToSegment(before, after, points[(start + step) % points.Count])
                            <= maximumDepthMm;
                    if (!shallow) continue;

                    var wide = false;
                    for (var step = 1; step < span && !wide; step++)
                        wide = points[(start + step) % points.Count]
                            .DistanceTo(points[(start + step + 1) % points.Count]) > maximumDepthMm * 4.0;
                    if (wide) continue;

                    for (var step = span; step >= 1; step--)
                        points.RemoveAt((start + step) % points.Count);
                    changed = true;
                    break;
                }
            }
        }
    return points;
    }

    /// <summary>
    /// Whether the edge arrives at a step and leaves it running the same way, which is what makes
    /// the step a detour off a straight run rather than a corner of the plan.
    /// </summary>
    private static bool SameDirection(
        CadStructurePoint2 arriveFrom,
        CadStructurePoint2 arriveAt,
        CadStructurePoint2 leaveFrom,
        CadStructurePoint2 leaveAt)
    {
        var inX = arriveAt.X - arriveFrom.X;
        var inY = arriveAt.Y - arriveFrom.Y;
        var outX = leaveAt.X - leaveFrom.X;
        var outY = leaveAt.Y - leaveFrom.Y;
        var inLength = Math.Sqrt(inX * inX + inY * inY);
        var outLength = Math.Sqrt(outX * outX + outY * outY);
        if (inLength < 1e-9 || outLength < 1e-9) return false;

        // Within about five degrees of one another counts as the same run.
        var alignment = (inX * outX + inY * outY) / (inLength * outLength);
        return alignment >= 0.996;
    }

    private static double PointToSegment(
        CadStructurePoint2 from, CadStructurePoint2 to, CadStructurePoint2 point)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9) return point.DistanceTo(from);
        var along = Math.Max(0.0, Math.Min(1.0,
            ((point.X - from.X) * dx + (point.Y - from.Y) * dy) / lengthSquared));
        return point.DistanceTo(new CadStructurePoint2(from.X + dx * along, from.Y + dy * along));
    }

    /// <summary>
    /// Removes the stretches the walk retraced. Whenever a vertex appears twice, everything
    /// between the two visits was entered and left again by the same edges, so it belongs to a
    /// spur rather than to the outline.
    /// </summary>
    private static List<int> RemoveExcursions(List<int> loop)
    {
        var result = new List<int>();
        var seen = new Dictionary<int, int>();
        foreach (var vertex in loop)
        {
            if (seen.TryGetValue(vertex, out var first))
            {
                for (var index = result.Count - 1; index > first; index--)
                {
                    seen.Remove(result[index]);
                    result.RemoveAt(index);
                }
                continue;
            }
            seen[vertex] = result.Count;
            result.Add(vertex);
        }
        return result;
    }

    /// <summary>
    /// The next edge when walking round the outside. Tracing a bay turns as tightly as it can at
    /// each junction; staying on the outline means doing the opposite and taking the widest turn,
    /// so the walk never steps into the drawing.
    /// </summary>
    private static int NextAroundOutside(
        IReadOnlyList<CadStructurePoint2> vertices,
        IReadOnlyList<List<int>> adjacency,
        int at,
        int cameFrom)
    {
        var neighbours = adjacency[at];
        if (neighbours.Count == 0) return -1;
        if (neighbours.Count == 1) return neighbours[0];

        var incoming = Math.Atan2(
            vertices[cameFrom].Y - vertices[at].Y,
            vertices[cameFrom].X - vertices[at].X);

        var best = -1;
        var bestTurn = double.MinValue;
        foreach (var neighbour in neighbours)
        {
            if (neighbour == cameFrom) continue;
            var outgoing = Math.Atan2(
                vertices[neighbour].Y - vertices[at].Y,
                vertices[neighbour].X - vertices[at].X);
            var turn = incoming - outgoing;
            while (turn <= 0) turn += Math.PI * 2;
            while (turn > Math.PI * 2) turn -= Math.PI * 2;
            if (turn <= bestTurn) continue;
            bestTurn = turn;
            best = neighbour;
        }
        // A dead end is walked back out of, which is how a spur off the outline is skipped.
        return best < 0 ? cameFrom : best;
    }

    public static IReadOnlyList<CadSlabLoop> BuildFaces(
        IReadOnlyList<CadStructureSegment> segmentsMm,
        double snapToleranceMm,
        out int unclosedVertexCount)
    {
        var split = SplitAtIntersections(segmentsMm);
        var vertices = new List<CadStructurePoint2>();
        var edges = new List<(int A, int B)>();

        foreach (var segment in split)
        {
            var a = SnapVertex(vertices, segment.Start, snapToleranceMm);
            var b = SnapVertex(vertices, segment.End, snapToleranceMm);
            if (a == b) continue;
            if (edges.Any(edge => (edge.A == a && edge.B == b) || (edge.A == b && edge.B == a)))
                continue;
            edges.Add((a, b));
        }

        var adjacency = new List<List<int>>();
        for (var index = 0; index < vertices.Count; index++) adjacency.Add(new List<int>());
        foreach (var (a, b) in edges)
        {
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        // A vertex with a single edge cannot close anything: it is the free end of a line that
        // overruns a corner or stops short of one. Reporting them tells the user whether the snap
        // tolerance is too small rather than leaving a region silently missing.
        unclosedVertexCount = adjacency.Count(list => list.Count == 1);

        return TraceFaces(vertices, adjacency);
    }

    /// <summary>
    /// Splits every segment where another segment crosses it, so a crossing always becomes a
    /// vertex. Without this two lines that cross in the middle stay unconnected and the areas they
    /// bound are never found.
    /// </summary>
    private static IReadOnlyList<CadStructureSegment> SplitAtIntersections(
        IReadOnlyList<CadStructureSegment> segments)
    {
        var result = new List<CadStructureSegment>();
        for (var index = 0; index < segments.Count; index++)
        {
            var current = segments[index];
            var direction = current.End - current.Start;
            var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length < Epsilon) continue;

            var stations = new List<double> { 0.0, length };
            for (var other = 0; other < segments.Count; other++)
            {
                if (other == index) continue;
                var crossing = Intersect(current, segments[other]);
                if (crossing is null) continue;
                var station = ((crossing.Value - current.Start).X * direction.X
                               + (crossing.Value - current.Start).Y * direction.Y) / length;
                if (station > Epsilon && station < length - Epsilon) stations.Add(station);
            }

            stations.Sort();
            var unit = new CadStructurePoint2(direction.X / length, direction.Y / length);
            for (var piece = 0; piece < stations.Count - 1; piece++)
            {
                var from = stations[piece];
                var to = stations[piece + 1];
                if (to - from < Epsilon) continue;
                result.Add(current with
                {
                    Start = current.Start + unit * from,
                    End = current.Start + unit * to
                });
            }
        }
        return result;
    }

    private static CadStructurePoint2? Intersect(CadStructureSegment first, CadStructureSegment second)
    {
        var r = first.End - first.Start;
        var s = second.End - second.Start;
        var denominator = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(denominator) < Epsilon) return null;

        var offset = second.Start - first.Start;
        var t = (offset.X * s.Y - offset.Y * s.X) / denominator;
        var u = (offset.X * r.Y - offset.Y * r.X) / denominator;
        if (t < -Epsilon || t > 1 + Epsilon || u < -Epsilon || u > 1 + Epsilon) return null;
        return first.Start + r * t;
    }

    private static int SnapVertex(
        List<CadStructurePoint2> vertices,
        CadStructurePoint2 point,
        double toleranceMm)
    {
        for (var index = 0; index < vertices.Count; index++)
            if (vertices[index].DistanceTo(point) <= toleranceMm) return index;
        vertices.Add(point);
        return vertices.Count - 1;
    }

    /// <summary>
    /// Traces every bounded face by following each directed edge and turning as far clockwise as
    /// possible at each vertex. Each directed edge belongs to exactly one face, so visiting them
    /// all yields every face once. The unbounded outer face comes out with the opposite winding
    /// and is dropped.
    /// </summary>
    /// <summary>
    /// Cuts an area along the outlines drawn inside it, giving the pieces that make it up.
    ///
    /// Where an outline stands clear of the area's own sides, the two are traced separately and one
    /// lies over the other -- the area whole, and the outline within it. The area itself is then not
    /// a piece of the cut: it is the outline, plus what the outline leaves, and the latter is the
    /// area carrying the outline as a hole.
    /// </summary>
    public static IReadOnlyList<CadPlanarPiece> Subdivide(
        IReadOnlyList<CadStructurePoint2> outlineMm,
        IReadOnlyList<IReadOnlyList<CadStructurePoint2>> cutsMm,
        double snapToleranceMm)
    {
        var whole = new CadSlabLoop(outlineMm.ToArray());
        if (cutsMm.Count == 0) return new[] { new CadPlanarPiece(whole, new List<CadSlabLoop>()) };

        var segments = new List<CadStructureSegment>();
        var id = 0;
        void Trace(IReadOnlyList<CadStructurePoint2> loop)
        {
            for (var index = 0; index < loop.Count; index++)
                segments.Add(new CadStructureSegment(
                    --id, loop[index], loop[(index + 1) % loop.Count], "CUT", string.Empty));
        }

        Trace(outlineMm);
        foreach (var cut in cutsMm) Trace(cut);

        var faces = BuildFaces(segments, snapToleranceMm, out _)
            .Where(face => face.AreaMm2 >= 10_000.0)
            .OrderByDescending(face => face.AreaMm2)
            .ToArray();
        if (faces.Length == 0) return new[] { new CadPlanarPiece(whole, new List<CadSlabLoop>()) };

        // A face standing inside another is not a piece beside it but a piece taken out of it.
        var pieces = new List<CadPlanarPiece>();
        foreach (var face in faces)
        {
            var container = pieces.FirstOrDefault(piece =>
                piece.Outer.AreaMm2 > face.AreaMm2
                && Encloses(piece.Outer, face)
                && !piece.Holes.Any(hole => Encloses(hole, face)));
            if (container is null)
            {
                pieces.Add(new CadPlanarPiece(face, new List<CadSlabLoop>()));
                continue;
            }

            container.Holes.Add(face);
            pieces.Add(new CadPlanarPiece(face, new List<CadSlabLoop>()));
        }

        return pieces;
    }

    /// <summary>
    /// Whether one loop lies wholly within another.
    /// </summary>
    private static bool Encloses(CadSlabLoop outer, CadSlabLoop inner) =>
        inner.VerticesMm.All(point => PointInLoop(outer.VerticesMm, point));

    private static bool PointInLoop(IReadOnlyList<CadStructurePoint2> loop, CadStructurePoint2 point)
    {
        var inside = false;
        for (int index = 0, previous = loop.Count - 1; index < loop.Count; previous = index++)
        {
            var a = loop[index];
            var b = loop[previous];
            if (a.Y > point.Y != b.Y > point.Y
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static IReadOnlyList<CadSlabLoop> TraceFaces(
        IReadOnlyList<CadStructurePoint2> vertices,
        IReadOnlyList<List<int>> adjacency)
    {
        var visited = new HashSet<(int From, int To)>();
        var faces = new List<CadSlabLoop>();

        for (var from = 0; from < adjacency.Count; from++)
        foreach (var to in adjacency[from])
        {
            if (!visited.Add((from, to))) continue;

            var loop = new List<int> { from };
            var currentFrom = from;
            var currentTo = to;

            while (true)
            {
                loop.Add(currentTo);
                var next = NextClockwise(vertices, adjacency, currentTo, currentFrom);
                if (next < 0) break;
                if (currentTo == from && next == to) break;
                if (!visited.Add((currentTo, next))) break;
                currentFrom = currentTo;
                currentTo = next;
                if (loop.Count > adjacency.Count * 4) break;
            }

            if (loop.Count < 4) continue;
            if (loop[^1] == loop[0]) loop.RemoveAt(loop.Count - 1);
            if (loop.Count < 3) continue;

            var candidate = new CadSlabLoop(loop.Select(index => vertices[index]).ToArray());
            // The outer face is traced the other way round, so its signed area has the opposite
            // sign to every bounded face and it is the one loop to discard.
            if (candidate.SignedAreaMm2 <= 0) continue;
            faces.Add(candidate);
        }

        return faces;
    }

    private static int NextClockwise(
        IReadOnlyList<CadStructurePoint2> vertices,
        IReadOnlyList<List<int>> adjacency,
        int at,
        int cameFrom)
    {
        var neighbours = adjacency[at];
        if (neighbours.Count == 0) return -1;
        if (neighbours.Count == 1) return neighbours[0];

        var incoming = Math.Atan2(
            vertices[cameFrom].Y - vertices[at].Y,
            vertices[cameFrom].X - vertices[at].X);

        var best = -1;
        var bestTurn = double.MaxValue;
        foreach (var neighbour in neighbours)
        {
            var outgoing = Math.Atan2(
                vertices[neighbour].Y - vertices[at].Y,
                vertices[neighbour].X - vertices[at].X);
            var turn = incoming - outgoing;
            while (turn <= 0) turn += Math.PI * 2;
            while (turn > Math.PI * 2) turn -= Math.PI * 2;
            if (turn >= bestTurn) continue;
            bestTurn = turn;
            best = neighbour;
        }
        return best;
    }
}

/// <summary>
/// A piece of a subdivided area: its edge, and whatever is cut out of it.
/// </summary>
public sealed record CadPlanarPiece(CadSlabLoop Outer, List<CadSlabLoop> Holes)
{
    public double AreaMm2 => Outer.AreaMm2 - Holes.Sum(hole => hole.AreaMm2);
}
