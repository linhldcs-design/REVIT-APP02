using Autodesk.Revit.DB;
using BeamRebar.Core.Models;

namespace BeamRebar.Addin.Services.Rebar;

/// <summary>
///     Hệ trục cục bộ của một nhịp để dựng curve thép: <see cref="Along"/> dọc trục dầm,
///     <see cref="Across"/> ngang tiết diện (phương b), <see cref="Up"/> theo chiều cao (phương h).
///     Cao độ mặt trên/dưới lấy từ giá trị THẬT (bounding box) thay vì suy từ Z trục → thép nằm
///     đúng trong host. Ném <see cref="InvalidOperationException"/> nếu dầm thẳng đứng/độ dài 0.
/// </summary>
public sealed class SpanFrame
{
    public SpanFrame(Span span, double topElevationFeet, double bottomElevationFeet)
    {
        StartFeet = new XYZ(span.Start.X, span.Start.Y, span.Start.Z);
        EndFeet = new XYZ(span.End.X, span.End.Y, span.End.Z);

        var axis = EndFeet - StartFeet;
        if (axis.GetLength() < 1e-6)
            throw new InvalidOperationException($"Nhịp dầm có chiều dài bằng 0 (Span {span.Index}) — không thể tạo thép.");

        Along = axis.Normalize();

        // Phương ngang tiết diện = trục dầm × phương đứng. Dầm thẳng đứng (cột) → cross ≈ 0 → bỏ qua.
        var across = Along.CrossProduct(XYZ.BasisZ);
        if (across.GetLength() < 1e-6)
            throw new InvalidOperationException($"Dầm gần như thẳng đứng (Span {span.Index}) — v1 chỉ hỗ trợ dầm ngang.");

        Across = across.Normalize();
        Up = XYZ.BasisZ;

        WidthFeet = span.Section.WidthMm / 304.8;
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

    /// <summary>Điểm trên trục dầm (XY) tại tham số dọc t∈[0,1], nâng/hạ về đúng cao độ mặt trên thật.</summary>
    public XYZ AxisTop(double t)
    {
        var alongXy = StartFeet + Along * (LengthFeet * t);
        return new XYZ(alongXy.X, alongXy.Y, TopElevationFeet);
    }
}
