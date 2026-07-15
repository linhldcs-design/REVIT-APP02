using Autodesk.Revit.DB;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services.Rebar;

/// <summary>
///     Hệ trục cục bộ của một nhịp để dựng curve thép: <see cref="Along"/> dọc trục dầm, <see cref="Across"/>
///     ngang tiết diện (phương b), <see cref="Up"/> theo chiều cao (phương h). Cao độ mặt trên/dưới lấy từ
///     giá trị THẬT (bounding box) thay vì suy từ Z trục → thép nằm đúng trong host. Ném
///     <see cref="InvalidOperationException"/> nếu dầm thẳng đứng hoặc độ dài 0.
/// </summary>
public sealed class SpanFrame
{
    private readonly double _lateralOffsetFeet;

    public SpanFrame(Span span, double topElevationFeet, double bottomElevationFeet, double lateralOffsetFeet = 0)
    {
        StartFeet = new XYZ(span.Start.X, span.Start.Y, span.Start.Z);
        EndFeet = new XYZ(span.End.X, span.End.Y, span.End.Z);

        var axis = EndFeet - StartFeet;
        if (axis.GetLength() < 1e-6)
            throw new InvalidOperationException($"Nhịp dầm chiều dài 0 (Span {span.Index}) — không thể tạo thép.");

        Along = axis.Normalize();

        // Phương ngang tiết diện = trục dầm × phương đứng. Dầm thẳng đứng → cross ≈ 0 → không hỗ trợ.
        var across = Along.CrossProduct(XYZ.BasisZ);
        if (across.GetLength() < 1e-6)
            throw new InvalidOperationException($"Dầm gần thẳng đứng (Span {span.Index}) — chỉ hỗ trợ dầm ngang.");

        Across = across.Normalize();
        Up = XYZ.BasisZ;
        _lateralOffsetFeet = lateralOffsetFeet;

        // Chiều rộng (phương ngang) lấy từ THAM SỐ FAMILY — bbox ngang của dầm có thể rộng hơn b thật.
        WidthFeet = span.Section.WidthMm / 304.8;

        // Chiều cao = h param family; mặt trên = Z location line (dầm Top-justified). Reader đã tính sẵn
        // top/bottom theo cách này → cover top/bottom khớp mép bê tông thật, đai không lòi lên/xuống.
        HeightFeet = topElevationFeet - bottomElevationFeet;
        TopElevationFeet = topElevationFeet;
    }

    public XYZ StartFeet { get; }
    public XYZ EndFeet { get; }
    public XYZ Along { get; }
    public XYZ Across { get; }
    public XYZ Up { get; }
    public double WidthFeet { get; }
    public double HeightFeet { get; }
    public double TopElevationFeet { get; }

    public double LengthFeet => StartFeet.DistanceTo(EndFeet);

    /// <summary>Điểm trên TÂM tiết diện bê tông (XY) tại tham số dọc t∈[0,1], ở cao độ mặt trên thật.
    ///     Dịch theo lệch ngang để bù justification dầm → thép căn đúng giữa bê tông.</summary>
    public XYZ AxisTop(double t)
    {
        var alongXy = StartFeet + Along * (LengthFeet * t) + Across * _lateralOffsetFeet;
        return new XYZ(alongXy.X, alongXy.Y, TopElevationFeet);
    }
}
