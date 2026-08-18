using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services;

public readonly record struct FootingLineInterval(double StartMm, double EndMm);

/// <summary>Cắt một đường thép ngang bằng mesh bê tông kín, hỗ trợ footprint tam giác/lồi/lõm.</summary>
public static class FootingSectionClipper
{
    private const double Epsilon = 1e-7;
    private const double MergeToleranceMm = 0.5;

    public static IReadOnlyList<FootingLineInterval> Clip(
        IReadOnlyList<FootingPreviewTriangle> concrete, bool alongX, double crossMm, double zMm)
    {
        if (concrete.Count == 0) return [];
        var minAxis = concrete.SelectMany(Points).Min(p => alongX ? p.Xmm : p.Ymm) - 1000;
        var origin = alongX ? new Vector(minAxis, crossMm, zMm) : new Vector(crossMm, minAxis, zMm);
        var direction = alongX ? new Vector(1, 0, 0) : new Vector(0, 1, 0);
        var hits = new List<double>();

        foreach (var triangle in concrete)
            if (TryIntersect(origin, direction, triangle, out var distance) && distance >= -Epsilon)
                hits.Add(minAxis + distance);

        if (hits.Count < 2) return [];
        hits.Sort();
        var unique = new List<double>(hits.Count);
        foreach (var hit in hits)
            if (unique.Count == 0 || Math.Abs(hit - unique[^1]) > MergeToleranceMm)
                unique.Add(hit);

        // Mesh kín phải có số giao điểm chẵn. Nếu tia đi đúng qua cạnh/đỉnh gây số lẻ, bỏ điểm cuối không ghép cặp.
        var result = new List<FootingLineInterval>(unique.Count / 2);
        for (var i = 1; i < unique.Count; i += 2)
            if (unique[i] - unique[i - 1] > MergeToleranceMm)
                result.Add(new FootingLineInterval(unique[i - 1], unique[i]));
        return result;
    }

    private static bool TryIntersect(Vector origin, Vector direction, FootingPreviewTriangle triangle, out double t)
    {
        var a = Vector.From(triangle.A);
        var b = Vector.From(triangle.B);
        var c = Vector.From(triangle.C);
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Cross(direction, edge2);
        var determinant = Dot(edge1, p);
        if (Math.Abs(determinant) < Epsilon) { t = 0; return false; }
        var inverse = 1 / determinant;
        var s = origin - a;
        var u = inverse * Dot(s, p);
        if (u < -Epsilon || u > 1 + Epsilon) { t = 0; return false; }
        var q = Cross(s, edge1);
        var v = inverse * Dot(direction, q);
        if (v < -Epsilon || u + v > 1 + Epsilon) { t = 0; return false; }
        t = inverse * Dot(edge2, q);
        return true;
    }

    private static IEnumerable<PreviewPoint3D> Points(FootingPreviewTriangle triangle)
    {
        yield return triangle.A; yield return triangle.B; yield return triangle.C;
    }

    private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vector Cross(Vector a, Vector b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    private readonly record struct Vector(double X, double Y, double Z)
    {
        public static Vector From(PreviewPoint3D p) => new(p.Xmm, p.Ymm, p.Zmm);
        public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }
}
