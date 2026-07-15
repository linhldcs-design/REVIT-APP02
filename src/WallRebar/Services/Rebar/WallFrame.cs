using Autodesk.Revit.DB;
using WallRebar.Models;

namespace WallRebar.Services.Rebar;

/// <summary>
///     Bọc <see cref="WallGeometry"/> để quy đổi tọa độ tham số (along, up, thickness) ra điểm XYZ tuyệt đối.
///     along/up tính theo feet từ gốc; thicknessOffset = khoảng cách từ MẶT A (mặt gốc) qua bề dày.
/// </summary>
public sealed class WallFrame
{
    private readonly WallGeometry _g;

    public WallFrame(WallGeometry geometry) => _g = geometry;

    public WallGeometry Geometry => _g;
    public XYZ DirAlong => _g.DirAlong;
    public XYZ DirUp => _g.DirUp;
    public XYZ DirThickness => _g.DirThickness;
    public double LengthFeet => _g.LengthFeet;
    public double HeightFeet => _g.HeightFeet;
    public double ThicknessFeet => _g.ThicknessFeet;

    /// <summary>Điểm tại (alongFeet dọc chiều dài, upFeet dọc chiều cao, thicknessOffsetFeet qua bề dày).</summary>
    public XYZ PointAt(double alongFeet, double upFeet, double thicknessOffsetFeet)
        => _g.Origin
           + _g.DirAlong * alongFeet
           + _g.DirUp * upFeet
           + _g.DirThickness * thicknessOffsetFeet;
}
