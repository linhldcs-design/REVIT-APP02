namespace RevitAPP.Core.Models;

/// <summary>Vector đơn vị 3D thuần, dùng cho hệ trục cục bộ của nhịp dầm.</summary>
public readonly record struct GeometryVector3D(double X, double Y, double Z)
{
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public GeometryVector3D Normalized()
    {
        var length = Length;
        return length < 1e-12 ? this : new GeometryVector3D(X / length, Y / length, Z / length);
    }

    /// <summary>Tích có hướng với trục đứng (0,0,1).</summary>
    public GeometryVector3D CrossWithUp() => new(Y, -X, 0);
}

/// <summary>
/// Hệ trục cục bộ của một nhịp dầm, đơn vị mm: <see cref="Along"/> dọc trục, <see cref="Across"/>
/// ngang tiết diện (phương b), <see cref="Up"/> theo chiều cao (phương h).
/// Bản độc lập Revit của hệ trục mà builder dùng để dựng curve, nên preview đặt thanh vào đúng
/// vị trí như thép thật.
/// </summary>
public sealed class PureSpanFrame
{
    private readonly double _lateralOffsetMm;

    /// <exception cref="InvalidOperationException">Nhịp dài 0 hoặc dầm gần thẳng đứng.</exception>
    public PureSpanFrame(
        GeometryPoint3D startMm,
        GeometryPoint3D endMm,
        double widthMm,
        double heightMm,
        double topElevationMm,
        double lateralOffsetMm = 0,
        int spanIndex = 0)
    {
        StartMm = startMm;
        EndMm = endMm;

        var axis = new GeometryVector3D(endMm.Xmm - startMm.Xmm, endMm.Ymm - startMm.Ymm, endMm.Zmm - startMm.Zmm);
        LengthMm = axis.Length;
        if (LengthMm < 1e-6)
            throw new InvalidOperationException($"Nhịp dầm chiều dài 0 (Span {spanIndex}) — không thể tạo thép.");

        Along = axis.Normalized();

        // Phương ngang tiết diện = trục dầm × phương đứng. Dầm thẳng đứng → cross ≈ 0 → không hỗ trợ.
        var across = Along.CrossWithUp();
        if (across.Length < 1e-6)
            throw new InvalidOperationException($"Dầm gần thẳng đứng (Span {spanIndex}) — chỉ hỗ trợ dầm ngang.");

        Across = across.Normalized();
        Up = new GeometryVector3D(0, 0, 1);
        _lateralOffsetMm = lateralOffsetMm;

        WidthMm = widthMm;
        HeightMm = heightMm;
        TopElevationMm = topElevationMm;
    }

    public GeometryPoint3D StartMm { get; }
    public GeometryPoint3D EndMm { get; }
    public GeometryVector3D Along { get; }
    public GeometryVector3D Across { get; }
    public GeometryVector3D Up { get; }
    public double WidthMm { get; }
    public double HeightMm { get; }
    public double TopElevationMm { get; }
    public double LengthMm { get; }

    /// <summary>
    /// Điểm trên TÂM tiết diện bê tông tại tham số dọc t∈[0,1], ở cao độ mặt trên thật.
    /// Dịch theo lệch ngang để bù justification dầm → thép căn đúng giữa bê tông.
    /// </summary>
    public GeometryPoint3D AxisTop(double t) => new(
        StartMm.Xmm + Along.X * (LengthMm * t) + Across.X * _lateralOffsetMm,
        StartMm.Ymm + Along.Y * (LengthMm * t) + Across.Y * _lateralOffsetMm,
        TopElevationMm);

    /// <summary>Điểm lệch <paramref name="lateralMm"/> ngang và <paramref name="verticalMm"/> đứng so với tâm mặt trên.</summary>
    public GeometryPoint3D PointAt(double t, double lateralMm, double verticalMm)
    {
        var center = AxisTop(t);
        return new GeometryPoint3D(
            center.Xmm + Across.X * lateralMm + Up.X * verticalMm,
            center.Ymm + Across.Y * lateralMm + Up.Y * verticalMm,
            center.Zmm + Across.Z * lateralMm + Up.Z * verticalMm);
    }

    /// <summary>Điểm cách đầu nhịp <paramref name="stationMm"/> dọc trục.</summary>
    public GeometryPoint3D PointAtStation(double stationMm, double lateralMm, double verticalMm) =>
        PointAt(LengthMm < 1e-9 ? 0 : stationMm / LengthMm, lateralMm, verticalMm);
}
