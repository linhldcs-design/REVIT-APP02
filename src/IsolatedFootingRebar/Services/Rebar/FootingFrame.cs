using Autodesk.Revit.DB;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services.Rebar;

/// <summary>
///     Hệ trục cục bộ của móng để dựng curve thép. Khác beam (1 trục dọc), móng cần 2 phương ngang
///     <see cref="DirX"/>, <see cref="DirY"/> + <see cref="Up"/>. Gốc tại TÂM đáy đế. Toạ độ chuẩn hóa:
///     u∈[0,1] dọc DirX (0 = mép -X, 1 = mép +X), v∈[0,1] dọc DirY, vertical = cao độ Z tuyệt đối (feet).
/// </summary>
public sealed class FootingFrame
{
    private readonly XYZ _baseCenter;

    public FootingFrame(FootingGeometry geometry)
    {
        _baseCenter = new XYZ(geometry.BaseCenter.X, geometry.BaseCenter.Y, geometry.BaseCenter.Z);
        Up = XYZ.BasisZ;
        DirX = new XYZ(geometry.DirX.X, geometry.DirX.Y, geometry.DirX.Z).Normalize();
        // Dựng lại DirY trực giao từ DirX (không tin DirY truyền vào có vuông góc) → mặt phẳng curve thép
        // và phương rải luôn nhất quán, CreateFromCurves không lệch hướng.
        DirY = Up.CrossProduct(DirX).Normalize();
        WidthXFeet = geometry.WidthXFeet;
        WidthYFeet = geometry.WidthYFeet;
        BottomZFeet = geometry.BottomZFeet;
        BaseTopZFeet = geometry.BaseTopZFeet;
    }

    public XYZ DirX { get; }
    public XYZ DirY { get; }
    public XYZ Up { get; }
    public double WidthXFeet { get; }
    public double WidthYFeet { get; }
    public double BottomZFeet { get; }
    public double BaseTopZFeet { get; }

    /// <summary>Điểm trong hệ móng: u,v∈[0,1] chuẩn hóa theo bề rộng đế, <paramref name="zFeet"/> = cao độ Z
    ///     tuyệt đối (feet). Gốc giữa đế nên dịch (u-0.5)/(v-0.5) ra mép.</summary>
    public XYZ PointAt(double u, double v, double zFeet)
    {
        var offsetX = DirX * ((u - 0.5) * WidthXFeet);
        var offsetY = DirY * ((v - 0.5) * WidthYFeet);
        var planar = _baseCenter + offsetX + offsetY;
        return new XYZ(planar.X, planar.Y, zFeet);
    }
}
