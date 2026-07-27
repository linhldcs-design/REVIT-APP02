using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Core.Services;

/// <summary>Dựng một path dầm duy nhất từ input không có thứ tự.</summary>
public static class BeamChainBuilder
{
    public static BeamChainBuildResult Build(
        IReadOnlyList<BeamSpanInput> inputs,
        BeamChainTolerance tolerance)
    {
        if (inputs == null) throw new ArgumentNullException(nameof(inputs));
        if (tolerance == null) throw new ArgumentNullException(nameof(tolerance));
        ValidateTolerance(tolerance);

        if (inputs.Count == 0)
            return Failure(BeamChainErrorCode.Empty, "Phải chọn ít nhất một dầm.");

        var invalid = inputs.FirstOrDefault(span =>
            span.SourceId <= 0 || span.WidthFeet <= 0 || span.HeightFeet <= 0 ||
            span.Start.DistanceTo(span.End) <= tolerance.EndpointFeet);
        if (invalid != null)
            return Failure(BeamChainErrorCode.InvalidGeometry,
                $"Dầm {invalid.SourceId} có hình học hoặc kích thước không hợp lệ.", invalid.SourceId);
        if (inputs.Select(span => span.SourceId).Distinct().Count() != inputs.Count)
            return Failure(BeamChainErrorCode.InvalidGeometry, "SourceId của mỗi dầm phải là duy nhất.");

        var nodes = BuildNodes(inputs, tolerance.EndpointFeet);
        var degrees = nodes.Select(node => node.Edges.Count).ToList();
        if (degrees.Any(degree => degree > 2))
            return Failure(BeamChainErrorCode.Branch,
                "Các dầm tạo thành nhánh. V1 chỉ hỗ trợ một chuỗi dầm không phân nhánh.");

        var endpoints = nodes.Where(node => node.Edges.Count == 1).ToList();
        if (inputs.Count > 1 && endpoints.Count == 0)
            return Failure(BeamChainErrorCode.Cycle,
                "Các dầm tạo thành vòng kín. V1 chỉ hỗ trợ một chuỗi dầm dạng path.");
        if (endpoints.Count != 2)
            return Failure(BeamChainErrorCode.Disconnected,
                "Các dầm không tạo thành một chuỗi liên tục duy nhất.");

        var startNode = endpoints.OrderBy(node => node.Point.X)
            .ThenBy(node => node.Point.Y)
            .ThenBy(node => node.Point.Z)
            .First();
        var ordered = Traverse(startNode, inputs.Count);
        if (ordered.Count != inputs.Count)
            return Failure(BeamChainErrorCode.Disconnected,
                "Các dầm không tạo thành một chuỗi liên tục duy nhất.");

        var chainStart = ordered[0].Start;
        var chainEnd = ordered[^1].End;
        var axis = Subtract(chainEnd, chainStart);
        var axisXyLength = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
        if (axisXyLength <= tolerance.EndpointFeet)
            return Failure(BeamChainErrorCode.InvalidGeometry,
                "Chuỗi dầm không có trục mặt bằng hợp lệ.");

        foreach (var span in ordered)
        {
            foreach (var point in new[] { span.Start, span.End })
            {
                if (Math.Abs(point.Z - chainStart.Z) > tolerance.ElevationFeet)
                    return Failure(BeamChainErrorCode.DifferentElevation,
                        $"Dầm {span.Input.SourceId} khác cao độ chuỗi vượt tolerance.", span.Input.SourceId);

                if (PerpendicularDistanceXy(point, chainStart, axis) > tolerance.AlignmentFeet)
                    return Failure(BeamChainErrorCode.NotCollinear,
                        $"Dầm {span.Input.SourceId} không đồng trục với chuỗi.", span.Input.SourceId);
            }
        }

        var models = new List<BeamSpanModel>(ordered.Count);
        var total = 0d;
        for (var index = 0; index < ordered.Count; index++)
        {
            var span = ordered[index];
            var length = span.Start.DistanceTo(span.End);
            models.Add(new BeamSpanModel(span.Input.SourceId, index, span.Start, span.End, length,
                span.Input.WidthFeet, span.Input.HeightFeet,
                span.Input.HostId == 0 ? span.Input.SourceId : span.Input.HostId));
            total += length;
        }

        return new BeamChainBuildResult(new BeamChainModel(models, chainStart, chainEnd, total), []);
    }

    private static List<Node> BuildNodes(IReadOnlyList<BeamSpanInput> inputs, double endpointTolerance)
    {
        var endpoints = inputs
            .SelectMany(input => new[]
            {
                new Endpoint(input, true, input.Start),
                new Endpoint(input, false, input.End)
            })
            .OrderBy(item => item.Point.X)
            .ThenBy(item => item.Point.Y)
            .ThenBy(item => item.Point.Z)
            .ThenBy(item => item.Input.SourceId)
            .ThenBy(item => item.IsStart ? 0 : 1)
            .ToList();
        var union = new UnionFind(endpoints.Count);
        for (var first = 0; first < endpoints.Count; first++)
        for (var second = first + 1; second < endpoints.Count; second++)
        {
            if (endpoints[first].Point.DistanceTo(endpoints[second].Point) <= endpointTolerance)
                union.Join(first, second);
        }

        var nodesByRoot = new Dictionary<int, Node>();
        for (var index = 0; index < endpoints.Count; index++)
        {
            var root = union.Find(index);
            if (!nodesByRoot.ContainsKey(root))
            {
                // endpoints đã sort nên điểm đầu tiên của component là representative ổn định theo thứ tự pick.
                nodesByRoot[root] = new Node(endpoints[index].Point);
            }
        }

        var endpointNodes = new Dictionary<(long SourceId, bool IsStart), Node>();
        for (var index = 0; index < endpoints.Count; index++)
            endpointNodes[(endpoints[index].Input.SourceId, endpoints[index].IsStart)] =
                nodesByRoot[union.Find(index)];

        foreach (var input in inputs.OrderBy(item => item.SourceId))
        {
            var start = endpointNodes[(input.SourceId, true)];
            var end = endpointNodes[(input.SourceId, false)];
            var edge = new Edge(input, start, end);
            start.Edges.Add(edge);
            end.Edges.Add(edge);
        }
        return nodesByRoot.Values.ToList();
    }

    private static List<OrientedEdge> Traverse(Node start, int expectedCount)
    {
        var result = new List<OrientedEdge>(expectedCount);
        var visited = new HashSet<Edge>();
        var current = start;
        while (result.Count < expectedCount)
        {
            var edge = current.Edges.FirstOrDefault(candidate => !visited.Contains(candidate));
            if (edge == null) break;
            visited.Add(edge);
            var next = ReferenceEquals(edge.Start, current) ? edge.End : edge.Start;
            result.Add(new OrientedEdge(edge.Input, current.Point, next.Point));
            current = next;
        }
        return result;
    }

    private static double PerpendicularDistanceXy(Point3 point, Point3 origin, Point3 axis)
    {
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        var denominator = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
        return Math.Abs(dx * axis.Y - dy * axis.X) / denominator;
    }

    private static Point3 Subtract(Point3 left, Point3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static void ValidateTolerance(BeamChainTolerance tolerance)
    {
        if (tolerance.EndpointFeet < 0 || tolerance.AlignmentFeet < 0 || tolerance.ElevationFeet < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance không được âm.");
    }

    private static BeamChainBuildResult Failure(BeamChainErrorCode code, string message, long? sourceId = null) =>
        new(null, [new BeamChainError(code, message, sourceId)]);

    private sealed class Node(Point3 point)
    {
        public Point3 Point { get; } = point;
        public List<Edge> Edges { get; } = [];
    }

    private sealed record Edge(BeamSpanInput Input, Node Start, Node End);
    private sealed record OrientedEdge(BeamSpanInput Input, Point3 Start, Point3 End);
    private sealed record Endpoint(BeamSpanInput Input, bool IsStart, Point3 Point);

    private sealed class UnionFind(int count)
    {
        private readonly int[] _parent = Enumerable.Range(0, count).ToArray();

        public int Find(int item)
        {
            while (_parent[item] != item)
            {
                _parent[item] = _parent[_parent[item]];
                item = _parent[item];
            }
            return item;
        }

        public void Join(int first, int second)
        {
            var firstRoot = Find(first);
            var secondRoot = Find(second);
            if (firstRoot == secondRoot) return;
            // endpoints được sort; root nhỏ nhất luôn thắng để representative deterministic.
            if (firstRoot < secondRoot) _parent[secondRoot] = firstRoot;
            else _parent[firstRoot] = secondRoot;
        }
    }
}
