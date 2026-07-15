namespace RevitAPP.Core.Services;

/// <summary>
///     Toán billboard quad thuần (không phụ thuộc Revit) — tính 4 góc quad camera-facing
///     quanh 1 điểm, cho phép test offset chính xác.
/// </summary>
public static class BillboardMath
{
    /// <summary>
    ///     4 góc quad quanh tâm (cx,cy,cz) theo vector right + up (đã chuẩn hóa) và bán kính half.
    ///     Thứ tự: BL, BR, TR, TL (để tạo 2 tam giác BL-BR-TR, BL-TR-TL).
    /// </summary>
    public static (double X, double Y, double Z)[] QuadCorners(
        double cx, double cy, double cz,
        (double X, double Y, double Z) right,
        (double X, double Y, double Z) up,
        double half)
    {
        var rx = right.X * half;
        var ry = right.Y * half;
        var rz = right.Z * half;
        var ux = up.X * half;
        var uy = up.Y * half;
        var uz = up.Z * half;

        return new[]
        {
            (cx - rx - ux, cy - ry - uy, cz - rz - uz), // BL
            (cx + rx - ux, cy + ry - uy, cz + rz - uz), // BR
            (cx + rx + ux, cy + ry + uy, cz + rz + uz), // TR
            (cx - rx + ux, cy - ry + uy, cz - rz + uz)  // TL
        };
    }

    /// <summary>
    ///     N đỉnh đa giác đều (billboard) quanh tâm theo right+up, bán kính r.
    ///     Trả tâm ở [0], rồi N đỉnh viền [1..N] — để dựng triangle-fan (N tam giác).
    /// </summary>
    public static (double X, double Y, double Z)[] PolygonFan(
        double cx, double cy, double cz,
        (double X, double Y, double Z) right,
        (double X, double Y, double Z) up,
        double r, int sides)
    {
        var result = new (double, double, double)[sides + 1];
        result[0] = (cx, cy, cz); // tâm
        for (var i = 0; i < sides; i++)
        {
            var a = 2.0 * Math.PI * i / sides;
            var cos = Math.Cos(a) * r;
            var sin = Math.Sin(a) * r;
            result[i + 1] = (
                cx + right.X * cos + up.X * sin,
                cy + right.Y * cos + up.Y * sin,
                cz + right.Z * cos + up.Z * sin);
        }
        return result;
    }

    /// <summary>Tích có hướng a × b (cho right = view × up).</summary>
    public static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>Chuẩn hóa vector; trả (0,0,0) nếu độ dài ~0.</summary>
    public static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        var len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-9 ? (0, 0, 0) : (v.X / len, v.Y / len, v.Z / len);
    }
}
