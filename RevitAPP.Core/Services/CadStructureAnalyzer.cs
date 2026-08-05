using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.Core.Services;

/// <summary>
/// Pure CAD geometry analysis shared by the preview and the Revit placement boundary.
/// It detects rectangular column outlines first; all unconsumed straight segments remain
/// available to the existing Grid review workflow.
/// </summary>
public static class CadStructureAnalyzer
{
    public const double MinimumColumnSizeMm = 100.0;
    public const double MaximumColumnSizeMm = 2000.0;
    public const int MaximumSegmentCount = 20000;

    private const double EndpointToleranceMm = 1.0;
    private const double AngleToleranceDegrees = 2.0;
    private const double DiagonalToleranceMm = 5.0;
    private const int MaximumNodeDegree = 32;
    private const int MaximumSearchSteps = 250000;

    public static CadStructureAnalysis Analyze(CadStructureTransferPackage package)
    {
        if (package.SchemaVersion != CadStructureTransferPackage.CurrentSchemaVersion)
            return Invalid($"Schema CAD {package.SchemaVersion} không được hỗ trợ.");
        if (package.Segments.Count == 0)
            return Invalid("Vùng chọn CAD không có LINE/POLYLINE/BLOCK dùng được.");

        if (package.Segments.Count > MaximumSegmentCount)
            return Invalid($"The CAD selection contains too many segments ({package.Segments.Count:N0}). "
                           + $"Split the selection into batches of at most {MaximumSegmentCount:N0} segments.");

        double scale;
        try
        {
            scale = CadGridUnitConverter.MillimetresPerDrawingUnit(package.InsUnits);
        }
        catch (InvalidDataException exception)
        {
            return Invalid(exception.Message);
        }

        if (!Finite(package.SourceAnchor)) return Invalid("Điểm móc CAD không hợp lệ.");

        var scaled = package.Segments
            .Where(segment => Finite(segment.Start) && Finite(segment.End))
            .Select(segment => segment with
            {
                Start = segment.Start * scale,
                End = segment.End * scale
            })
            .Where(segment => segment.Start.DistanceTo(segment.End) >= 1.0)
            .ToArray();

        if (scaled.Length == 0) return Invalid("Không có hình học CAD hợp lệ sau khi đổi đơn vị.");

        var minX = scaled.Min(segment => Math.Min(segment.Start.X, segment.End.X));
        var minY = scaled.Min(segment => Math.Min(segment.Start.Y, segment.End.Y));
        var origin = new CadStructurePoint2(minX, minY);
        var relative = scaled.Select(segment => segment with
        {
            Start = segment.Start - origin,
            End = segment.End - origin
        }).ToArray();

        var columns = DetectRectangles(relative, out var searchLimited);
        if (searchLimited)
            return Invalid("Hình học CAD giao nhau quá phức tạp để nhận dạng an toàn. Hãy chia nhỏ vùng chọn hoặc làm sạch các LINE trùng nhau.");
        var consumed = new HashSet<int>(columns.SelectMany(column => column.SourceSegmentIds));
        var grids = relative.Where(segment => !consumed.Contains(segment.Id)).ToArray();
        var anchor = package.SourceAnchor * scale - origin;

        var warnings = new List<string>();
        var unreadable = package.Segments.Count - scaled.Length;
        if (unreadable > 0) warnings.Add($"Bỏ qua {unreadable} segment CAD không hợp lệ hoặc quá ngắn.");
        if (columns.Count == 0) warnings.Add("Không nhận dạng được rectangle cột trong vùng chọn.");

        return new CadStructureAnalysis(origin, anchor, grids, columns, warnings, null);
    }

    private static CadStructureAnalysis Invalid(string error) =>
        new(default, default, Array.Empty<CadStructureSegment>(),
            Array.Empty<CadColumnCandidate>(), Array.Empty<string>(), error);

    private static IReadOnlyList<CadColumnCandidate> DetectRectangles(
        IReadOnlyList<CadStructureSegment> segments,
        out bool searchLimited)
    {
        var limited = false;
        var eligibleGroups = segments
            .Where(segment =>
            {
                var length = segment.Start.DistanceTo(segment.End);
                return length >= MinimumColumnSizeMm * 0.75
                       && length <= MaximumColumnSizeMm * 1.25;
            })
            .GroupBy(EdgeSignature, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var eligible = eligibleGroups.Select(group => group.First()).ToArray();
        var duplicateIdsByRepresentative = eligibleGroups.ToDictionary(
            group => group.First().Id,
            group => group.Select(segment => segment.Id).Distinct().ToArray());

        var adjacency = new Dictionary<PointKey, List<CadStructureSegment>>();
        foreach (var segment in eligible)
        {
            Add(adjacency, Key(segment.Start), segment);
            Add(adjacency, Key(segment.End), segment);
        }

        var results = new List<CadColumnCandidate>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var searchSteps = 0;
        foreach (var first in eligible)
        {
            if (limited) break;
            var start = first.Start;
            Search(start, first.End, start,
                new List<CadStructureSegment> { first },
                new List<CadStructurePoint2> { start, first.End });

            void Search(
                CadStructurePoint2 cycleStart,
                CadStructurePoint2 current,
                CadStructurePoint2 initial,
                List<CadStructureSegment> edges,
                List<CadStructurePoint2> points)
            {
                if (++searchSteps > MaximumSearchSteps)
                {
                    limited = true;
                    return;
                }
                if (edges.Count == 4)
                {
                    if (current.DistanceTo(cycleStart) > EndpointToleranceMm) return;
                    var corners = points.Take(4).ToArray();
                    if (!TryCreateCandidate(results.Count + 1, corners, edges, out var candidate)) return;
                    var signature = string.Join("-", edges.Select(edge => edge.Id).OrderBy(id => id));
                    if (!signatures.Add(signature)) return;
                    var duplicateIndex = results.FindIndex(existing => SameColumn(existing, candidate));
                    if (duplicateIndex >= 0)
                    {
                        var existing = results[duplicateIndex];
                        results[duplicateIndex] = existing with
                        {
                            SourceSegmentIds = existing.SourceSegmentIds
                                .Concat(candidate.SourceSegmentIds)
                                .Distinct()
                                .ToArray()
                        };
                        return;
                    }
                    results.Add(candidate);
                    return;
                }

                var nextEdges = Neighbours(adjacency, current).ToArray();
                if (nextEdges.Length > MaximumNodeDegree)
                {
                    limited = true;
                    return;
                }
                foreach (var edge in nextEdges)
                {
                    if (edges.Any(existing => existing.Id == edge.Id)) continue;
                    CadStructurePoint2 next;
                    if (edge.Start.DistanceTo(current) <= EndpointToleranceMm) next = edge.End;
                    else if (edge.End.DistanceTo(current) <= EndpointToleranceMm) next = edge.Start;
                    else continue;
                    if (edges.Count < 3 && points.Take(points.Count - 1)
                            .Any(point => point.DistanceTo(next) <= EndpointToleranceMm)) continue;

                    edges.Add(edge);
                    points.Add(next);
                    Search(cycleStart, next, initial, edges, points);
                    points.RemoveAt(points.Count - 1);
                    edges.RemoveAt(edges.Count - 1);
                }
            }
        }

        searchLimited = limited;
        return results.OrderBy(column => column.CenterMm.X)
            .ThenBy(column => column.CenterMm.Y)
            .Select((column, index) => column with
            {
                Id = index + 1,
                SourceSegmentIds = column.SourceSegmentIds
                    .SelectMany(id => duplicateIdsByRepresentative.TryGetValue(id, out var ids)
                        ? ids
                        : new[] { id })
                    .Distinct()
                    .ToArray()
            })
            .ToArray();
    }

    private static bool TryCreateCandidate(
        int id,
        IReadOnlyList<CadStructurePoint2> corners,
        IReadOnlyList<CadStructureSegment> edges,
        out CadColumnCandidate candidate)
    {
        candidate = null!;
        if (corners.Count != 4) return false;
        if (edges.Select(edge => edge.Layer ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) return false;
        if (edges.Select(edge => edge.SourcePath ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) return false;

        var vectors = new CadStructurePoint2[4];
        var lengths = new double[4];
        for (var index = 0; index < 4; index++)
        {
            vectors[index] = corners[(index + 1) % 4] - corners[index];
            lengths[index] = Math.Sqrt(vectors[index].X * vectors[index].X + vectors[index].Y * vectors[index].Y);
            if (lengths[index] < MinimumColumnSizeMm || lengths[index] > MaximumColumnSizeMm) return false;
        }

        var sineTolerance = Math.Sin(AngleToleranceDegrees * Math.PI / 180.0);
        for (var index = 0; index < 4; index++)
        {
            var next = (index + 1) % 4;
            var dot = Math.Abs(vectors[index].X * vectors[next].X + vectors[index].Y * vectors[next].Y)
                      / (lengths[index] * lengths[next]);
            if (dot > sineTolerance) return false;
        }

        if (Math.Abs(lengths[0] - lengths[2]) > DiagonalToleranceMm
            || Math.Abs(lengths[1] - lengths[3]) > DiagonalToleranceMm) return false;
        var diagonalA = corners[0].DistanceTo(corners[2]);
        var diagonalB = corners[1].DistanceTo(corners[3]);
        if (Math.Abs(diagonalA - diagonalB) > DiagonalToleranceMm) return false;

        var center = new CadStructurePoint2(
            corners.Average(point => point.X),
            corners.Average(point => point.Y));

        // Width follows the edge closest to the CAD X direction. This keeps b/h stable
        // when the same rectangle is drawn clockwise, counter-clockwise, or mirrored.
        var angle0 = UndirectedAngle(vectors[0]);
        var angle1 = UndirectedAngle(vectors[1]);
        var distance0 = DistanceToHorizontal(angle0);
        var distance1 = DistanceToHorizontal(angle1);
        var firstIsWidth = Math.Abs(distance0 - distance1) <= 1e-9
            ? lengths[0] >= lengths[1]
            : distance0 < distance1;
        var width = firstIsWidth ? lengths[0] : lengths[1];
        var height = firstIsWidth ? lengths[1] : lengths[0];
        var angle = firstIsWidth ? angle0 : angle1;

        var layer = edges.Select(edge => edge.Layer).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        var path = edges.Select(edge => edge.SourcePath).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        var text = edges.Select(edge => edge.SourceText).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        candidate = new CadColumnCandidate(
            id, corners.ToArray(), center, width, height, angle,
            layer, path, text, edges.Select(edge => edge.Id).Distinct().ToArray());
        return true;
    }

    private static bool SameColumn(CadColumnCandidate first, CadColumnCandidate second) =>
        first.CenterMm.DistanceTo(second.CenterMm) <= EndpointToleranceMm
        && Math.Abs(first.WidthMm - second.WidthMm) <= EndpointToleranceMm
        && Math.Abs(first.HeightMm - second.HeightMm) <= EndpointToleranceMm;

    private static double UndirectedAngle(CadStructurePoint2 vector)
    {
        var angle = Math.Atan2(vector.Y, vector.X) * 180.0 / Math.PI;
        while (angle < 0) angle += 180.0;
        while (angle >= 180.0) angle -= 180.0;
        return angle;
    }

    private static double DistanceToHorizontal(double angle) => Math.Min(angle, 180.0 - angle);

    private static bool Finite(CadStructurePoint2 point) =>
        !double.IsNaN(point.X) && !double.IsInfinity(point.X)
        && !double.IsNaN(point.Y) && !double.IsInfinity(point.Y);

    private static PointKey Key(CadStructurePoint2 point) => new(
        (long)Math.Round(point.X / EndpointToleranceMm),
        (long)Math.Round(point.Y / EndpointToleranceMm));

    private static void Add(
        IDictionary<PointKey, List<CadStructureSegment>> adjacency,
        PointKey key,
        CadStructureSegment segment)
    {
        if (!adjacency.TryGetValue(key, out var list))
        {
            list = new List<CadStructureSegment>();
            adjacency.Add(key, list);
        }
        list.Add(segment);
    }

    private static IEnumerable<CadStructureSegment> Neighbours(
        IReadOnlyDictionary<PointKey, List<CadStructureSegment>> adjacency,
        CadStructurePoint2 point)
    {
        var center = Key(point);
        var seen = new HashSet<int>();
        for (var x = center.X - 1; x <= center.X + 1; x++)
        for (var y = center.Y - 1; y <= center.Y + 1; y++)
        {
            if (!adjacency.TryGetValue(new PointKey(x, y), out var edges)) continue;
            foreach (var edge in edges)
                if (seen.Add(edge.Id)) yield return edge;
        }
    }

    private static string EdgeSignature(CadStructureSegment segment)
    {
        var first = Key(segment.Start);
        var second = Key(segment.End);
        if (first.X > second.X || first.X == second.X && first.Y > second.Y)
            (first, second) = (second, first);
        return $"{first.X},{first.Y}:{second.X},{second.Y}|{segment.Layer}|{segment.SourcePath}";
    }

    private readonly record struct PointKey(long X, long Y);
}
