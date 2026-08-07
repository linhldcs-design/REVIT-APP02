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
