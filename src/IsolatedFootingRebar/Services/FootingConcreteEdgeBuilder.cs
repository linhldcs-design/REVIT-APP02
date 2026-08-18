using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services;

/// <summary>
///     Lọc lưới tam giác bê tông thành wireframe sạch: bỏ đường chéo nội bộ giữa hai tam giác đồng phẳng,
///     chỉ giữ biên hở và cạnh gãy hình học.
/// </summary>
public static class FootingConcreteEdgeBuilder
{
    private const double QuantizationMm = 0.1;
    private static readonly double SharpDotLimit = Math.Cos(8 * Math.PI / 180);

    public static IReadOnlyList<FootingPreviewEdge> Build(IReadOnlyList<FootingPreviewTriangle> triangles)
    {
        var edges = new Dictionary<EdgeKey, EdgeInfo>();
        foreach (var triangle in triangles)
        {
            var normal = Normal(triangle);
            Add(edges, triangle.A, triangle.B, normal);
            Add(edges, triangle.B, triangle.C, normal);
            Add(edges, triangle.C, triangle.A, normal);
        }

        return edges.Values
            .Where(edge => edge.FaceCount == 1 || edge.IsSharp)
            .Select(edge => new FootingPreviewEdge(edge.A, edge.B))
            .ToArray();
    }

    private static void Add(Dictionary<EdgeKey, EdgeInfo> edges, PreviewPoint3D a, PreviewPoint3D b, Vector normal)
    {
        var key = new EdgeKey(VertexKey.From(a), VertexKey.From(b));
        if (!edges.TryGetValue(key, out var edge))
        {
            edges[key] = new EdgeInfo(a, b, normal);
            return;
        }

        edge.AddFace(normal);
    }

    private static Vector Normal(FootingPreviewTriangle triangle)
    {
        var ab = new Vector(triangle.B.Xmm - triangle.A.Xmm, triangle.B.Ymm - triangle.A.Ymm, triangle.B.Zmm - triangle.A.Zmm);
        var ac = new Vector(triangle.C.Xmm - triangle.A.Xmm, triangle.C.Ymm - triangle.A.Ymm, triangle.C.Zmm - triangle.A.Zmm);
        var cross = new Vector(
            ab.Y * ac.Z - ab.Z * ac.Y,
            ab.Z * ac.X - ab.X * ac.Z,
            ab.X * ac.Y - ab.Y * ac.X);
        var length = Math.Sqrt(cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z);
        return length < 1e-9 ? default : new Vector(cross.X / length, cross.Y / length, cross.Z / length);
    }

    private sealed class EdgeInfo
    {
        private readonly Vector _firstNormal;

        public EdgeInfo(PreviewPoint3D a, PreviewPoint3D b, Vector firstNormal)
        {
            A = a; B = b; _firstNormal = firstNormal; FaceCount = 1;
        }

        public PreviewPoint3D A { get; }
        public PreviewPoint3D B { get; }
        public int FaceCount { get; private set; }
        public bool IsSharp { get; private set; }

        public void AddFace(Vector normal)
        {
            FaceCount++;
            var dot = Math.Abs(_firstNormal.X * normal.X + _firstNormal.Y * normal.Y + _firstNormal.Z * normal.Z);
            if (dot < SharpDotLimit) IsSharp = true;
        }
    }

    private readonly record struct Vector(double X, double Y, double Z);

    private readonly record struct VertexKey(long X, long Y, long Z)
    {
        public static VertexKey From(PreviewPoint3D point) => new(
            (long)Math.Round(point.Xmm / QuantizationMm),
            (long)Math.Round(point.Ymm / QuantizationMm),
            (long)Math.Round(point.Zmm / QuantizationMm));
    }

    private readonly record struct EdgeKey
    {
        public EdgeKey(VertexKey a, VertexKey b)
        {
            if (Compare(a, b) <= 0) { A = a; B = b; }
            else { A = b; B = a; }
        }

        public VertexKey A { get; }
        public VertexKey B { get; }

        private static int Compare(VertexKey a, VertexKey b)
        {
            var x = a.X.CompareTo(b.X); if (x != 0) return x;
            var y = a.Y.CompareTo(b.Y); return y != 0 ? y : a.Z.CompareTo(b.Z);
        }
    }
}
