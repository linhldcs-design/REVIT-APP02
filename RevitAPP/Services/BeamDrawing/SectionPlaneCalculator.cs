using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Tính <see cref="BoundingBoxXYZ"/> (kèm Transform) cho <c>ViewSection.CreateSection</c>:
///     - Sectional Elevation: nhìn ngang vuông góc trục dầm, thấy toàn chiều dài.
///     - Cross Section: cắt vuông góc trục dầm tại tỉ lệ t∈[0,1] dọc dầm.
///
///     Quy ước section box Transform: BasisX = phương "phải" trên view, BasisY = phương "lên",
///     BasisZ = phương NHÌN (ra khỏi mặt cắt về phía mắt). Mặt phẳng cắt tại Origin, chiều sâu [Min.Z, Max.Z].
/// </summary>
public sealed class SectionPlaneCalculator
{
    private const double MarginFeet = 1.5;        // ~450mm — đệm mặt cắt dọc (chừa chỗ tag/dim)
    // Đệm mặt cắt ngang CÂN ĐỐI 4 phía ~0.5ft (~150mm) — crop ôm gọn tiết diện như đích DK2-1
    // (margin đều ~0.49ft trên/dưới/trái/phải). Tag rải theo mốc từ đỉnh dầm nên không cần crop cao thừa.
    private const double CrossMarginFeet = 0.5;
    private const double CrossHeightMarginFeet = 0.5;
    private const double HalfDepthFeet = 2.0;     // ~600mm — chiều sâu cắt mặt cắt dọc
    // Revit hiển thị Far Clip Offset = Max.Z - Min.Z, nên mỗi nửa box chỉ bằng 75 mm để tổng đúng 150 mm.
    private static readonly double CrossHalfDepthFeet =
        BeamSectionBoxMath.HalfDepthFeet(BeamSectionBoxMath.CrossFarClipOffsetMm);

    public BoundingBoxXYZ CreateSectionalBox(BeamGeometry beam)
    {
        var dir = Direction(beam);
        var up = XYZ.BasisZ;
        var viewDir = dir.CrossProduct(up).Normalize();

        var midAxis = (ToXyz(beam.Start) + ToXyz(beam.End)) * 0.5;
        var center = new XYZ(midAxis.X, midAxis.Y, (beam.TopZFeet + beam.BottomZFeet) * 0.5);

        var halfLength = beam.LengthFeet * 0.5 + MarginFeet;
        var halfHeight = (beam.TopZFeet - beam.BottomZFeet) * 0.5 + MarginFeet;

        return BuildBox(center, dir, up, viewDir, halfLength, halfHeight, HalfDepthFeet);
    }

    public BoundingBoxXYZ CreateCrossSectionBox(BeamGeometry beam, double t)
    {
        var dir = Direction(beam);
        var up = XYZ.BasisZ;
        var right = up.CrossProduct(dir).Normalize();

        var axisPoint = ToXyz(beam.Start) + dir * (beam.LengthFeet * t);
        var center = new XYZ(axisPoint.X, axisPoint.Y, (beam.TopZFeet + beam.BottomZFeet) * 0.5);

        var halfWidth = beam.WidthFeet * 0.5 + CrossMarginFeet;
        var halfHeight = (beam.TopZFeet - beam.BottomZFeet) * 0.5 + CrossHeightMarginFeet;

        return BuildBox(center, right, up, dir, halfWidth, halfHeight, CrossHalfDepthFeet);
    }

    private static XYZ Direction(BeamGeometry beam) => (ToXyz(beam.End) - ToXyz(beam.Start)).Normalize();

    private static XYZ ToXyz(Point3 p) => new(p.X, p.Y, p.Z);

    private static BoundingBoxXYZ BuildBox(XYZ center, XYZ basisX, XYZ basisY, XYZ viewDir,
        double halfX, double halfY, double halfDepth)
    {
        var transform = Transform.Identity;
        transform.Origin = center;
        transform.BasisX = basisX.Normalize();
        transform.BasisY = basisY.Normalize();
        transform.BasisZ = viewDir.Normalize();

        return new BoundingBoxXYZ
        {
            Transform = transform,
            Min = new XYZ(-halfX, -halfY, -halfDepth),
            Max = new XYZ(halfX, halfY, halfDepth)
        };
    }
}
