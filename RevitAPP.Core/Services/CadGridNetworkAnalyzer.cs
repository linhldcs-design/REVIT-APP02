using RevitAPP.Core.Models.CadGrid;

namespace RevitAPP.Core.Services;

public static class CadGridNetworkAnalyzer
{
    public static CadGridNetworkAnalysis Analyze(
        IReadOnlyList<CadGridSegment2> segments,
        double angularToleranceDegrees = 0.1,
        double minimumCrossingAngleDegrees = 5,
        double intersectionToleranceMm = 1,
        double spacingConsistencyToleranceMm = 1)
    {
        if (segments.Count < 2)
            return CadGridNetworkAnalysis.Invalid("Cần ít nhất hai line CAD giao nhau.");

        var angularTolerance = DegreesToRadians(angularToleranceDegrees);
        var clusters = BuildDirectionClusters(segments, angularTolerance);
        var selectedPair = SelectBestClusterPair(
            clusters,
            minimumCrossingAngleDegrees,
            intersectionToleranceMm);
        if (selectedPair is null)
            return CadGridNetworkAnalysis.Invalid("Không tìm thấy giao điểm giữa hai phương line CAD.");

        var (firstCluster, secondCluster, intersections) = selectedPair.Value;
        var connectedIds = intersections
            .SelectMany(item => new[] { item.FirstSegmentId, item.SecondSegmentId })
            .ToHashSet();
        var thirdDirectionCrossesNetwork = clusters
            .Where(cluster => !ReferenceEquals(cluster, firstCluster)
                              && !ReferenceEquals(cluster, secondCluster))
            .SelectMany(cluster => cluster.Segments)
            .Any(extra => firstCluster.Segments
                              .Concat(secondCluster.Segments)
                              .Where(networkSegment => connectedIds.Contains(networkSegment.Id))
                              .Any(networkSegment => CadGridGeometry.TryIntersectBounded(
                                  extra,
                                  networkSegment,
                                  intersectionToleranceMm,
                                  out _)));
        if (thirdDirectionCrossesNetwork)
            return CadGridNetworkAnalysis.Invalid(
                "Phát hiện họ line thứ ba giao với mạng trục; hãy quét lại chỉ hai phương trục.");

        var firstIds = firstCluster.Segments
            .Where(segment => connectedIds.Contains(segment.Id))
            .Select(segment => segment.Id)
            .ToHashSet();
        var secondIds = secondCluster.Segments
            .Where(segment => connectedIds.Contains(segment.Id))
            .Select(segment => segment.Id)
            .ToHashSet();

        if (firstIds.Count == 0 || secondIds.Count == 0)
            return CadGridNetworkAnalysis.Invalid("Mạng giao điểm không có đủ hai họ trục.");

        var firstFamily = BuildFamily(
            firstIds,
            secondIds,
            intersections,
            targetIsFirst: true,
            firstCluster.RepresentativeAngle,
            spacingConsistencyToleranceMm);
        if (firstFamily is null)
            return CadGridNetworkAnalysis.Invalid(
                "Không có line chuẩn phương 2 giao với toàn bộ trục phương 1.");

        var secondFamily = BuildFamily(
            secondIds,
            firstIds,
            intersections,
            targetIsFirst: false,
            secondCluster.RepresentativeAngle,
            spacingConsistencyToleranceMm);
        if (secondFamily is null)
            return CadGridNetworkAnalysis.Invalid(
                "Không có line chuẩn phương 1 giao với toàn bộ trục phương 2.");

        var includedIds = firstFamily.OrderedSegmentIds
            .Concat(secondFamily.OrderedSegmentIds)
            .ToHashSet();
        var skipped = segments
            .Select(segment => segment.Id)
            .Where(id => !includedIds.Contains(id))
            .OrderBy(id => id)
            .ToArray();

        return CadGridNetworkAnalysis.Valid(
            new CadGridNetwork(firstFamily, secondFamily, intersections, skipped));
    }

    private static CadGridNetworkFamily? BuildFamily(
        HashSet<int> targetIds,
        HashSet<int> referenceIds,
        IReadOnlyList<CadGridIntersection> intersections,
        bool targetIsFirst,
        double targetDirectionAngle,
        double spacingConsistencyToleranceMm)
    {
        var candidates = referenceIds
            .Select(referenceId =>
            {
                var hits = intersections
                    .Where(item => targetIsFirst
                        ? item.SecondSegmentId == referenceId && targetIds.Contains(item.FirstSegmentId)
                        : item.FirstSegmentId == referenceId && targetIds.Contains(item.SecondSegmentId))
                    .ToArray();
                return (ReferenceId: referenceId, Hits: hits);
            })
            .OrderByDescending(item => item.Hits.Length)
            .ThenBy(item => item.ReferenceId)
            .ToArray();

        var selected = candidates.FirstOrDefault();
        if (selected.Hits is null || selected.Hits.Length == 0) return null;

        var normalX = -Math.Sin(targetDirectionAngle);
        var normalY = Math.Cos(targetDirectionAngle);
        if (normalX < -1e-9 || Math.Abs(normalX) <= 1e-9 && normalY < 0)
        {
            normalX = -normalX;
            normalY = -normalY;
        }

        var ordered = new List<(int TargetId, double Position)>();
        foreach (var targetId in targetIds)
        {
            var positions = intersections
                .Where(hit => targetIsFirst
                    ? hit.FirstSegmentId == targetId
                    : hit.SecondSegmentId == targetId)
                .Select(hit => hit.Point.Xmm * normalX + hit.Point.Ymm * normalY)
                .ToArray();
            if (positions.Length == 0) continue;
            if (positions.Max() - positions.Min() > spacingConsistencyToleranceMm)
                return null;

            ordered.Add((targetId, positions.Average()));
        }

        ordered.Sort((left, right) => left.Position.CompareTo(right.Position));
        var distances = new double[Math.Max(0, ordered.Count - 1)];
        for (var index = 0; index < distances.Length; index++)
            distances[index] = Math.Abs(ordered[index + 1].Position - ordered[index].Position);

        return new CadGridNetworkFamily(
            ordered.Select(item => item.TargetId).ToArray(),
            distances,
            selected.ReferenceId);
    }

    private static List<DirectionCluster> BuildDirectionClusters(
        IEnumerable<CadGridSegment2> segments,
        double angularTolerance)
    {
        var clusters = new List<DirectionCluster>();

        foreach (var segment in segments.OrderBy(item => item.Id))
        {
            var angle = CadGridGeometry.CanonicalAngleRadians(segment);
            var cluster = clusters.FirstOrDefault(
                item => CadGridGeometry.AngleDistanceRadians(item.RepresentativeAngle, angle)
                        <= angularTolerance);
            if (cluster is null)
            {
                clusters.Add(new DirectionCluster(angle, segment));
                continue;
            }

            cluster.Add(angle, segment);
        }

        return clusters
            .OrderByDescending(cluster => cluster.Segments.Count)
            .ThenBy(cluster => cluster.RepresentativeAngle)
            .ToList();
    }

    private static (
        DirectionCluster First,
        DirectionCluster Second,
        List<CadGridIntersection> Intersections)? SelectBestClusterPair(
        IReadOnlyList<DirectionCluster> clusters,
        double minimumCrossingAngleDegrees,
        double intersectionToleranceMm)
    {
        (DirectionCluster First, DirectionCluster Second, List<CadGridIntersection> Intersections)?
            best = null;
        var minimumAngle = DegreesToRadians(minimumCrossingAngleDegrees);

        for (var firstIndex = 0; firstIndex < clusters.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < clusters.Count; secondIndex++)
            {
                var first = clusters[firstIndex];
                var second = clusters[secondIndex];
                if (CadGridGeometry.AngleDistanceRadians(
                        first.RepresentativeAngle,
                        second.RepresentativeAngle) < minimumAngle)
                    continue;

                var intersections = new List<CadGridIntersection>();
                foreach (var firstSegment in first.Segments)
                {
                    foreach (var secondSegment in second.Segments)
                    {
                        if (!CadGridGeometry.TryIntersectBounded(
                                firstSegment,
                                secondSegment,
                                intersectionToleranceMm,
                                out var point))
                            continue;

                        intersections.Add(new CadGridIntersection(
                            firstSegment.Id,
                            secondSegment.Id,
                            point));
                    }
                }

                if (intersections.Count == 0) continue;
                if (best is null
                    || intersections.Count > best.Value.Intersections.Count
                    || intersections.Count == best.Value.Intersections.Count
                    && first.Segments.Count + second.Segments.Count
                    > best.Value.First.Segments.Count + best.Value.Second.Segments.Count)
                    best = (first, second, intersections);
            }
        }

        return best;
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180d;

    private sealed class DirectionCluster
    {
        private double _sumCosine;
        private double _sumSine;

        public DirectionCluster(double angle, CadGridSegment2 segment)
        {
            Segments.Add(segment);
            AddAngle(angle);
        }

        public List<CadGridSegment2> Segments { get; } = new();

        public double RepresentativeAngle
        {
            get
            {
                var angle = Math.Atan2(_sumSine, _sumCosine) / 2d;
                return angle < 0 ? angle + Math.PI : angle;
            }
        }

        public void Add(double angle, CadGridSegment2 segment)
        {
            Segments.Add(segment);
            AddAngle(angle);
        }

        private void AddAngle(double angle)
        {
            _sumCosine += Math.Cos(2 * angle);
            _sumSine += Math.Sin(2 * angle);
        }
    }
}
