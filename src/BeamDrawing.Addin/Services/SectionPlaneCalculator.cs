using Autodesk.Revit.DB;

namespace BeamDrawing.Addin.Services;

/// <summary>
///     Tính <see cref="BoundingBoxXYZ"/> (kèm Transform) cho <c>ViewSection.CreateSection</c>:
///     - Sectional Elevation: nhìn vuông góc trục dầm theo phương ngang, thấy toàn chiều dài dầm.
///     - Cross Section: cắt vuông góc trục dầm tại vị trí t∈[0,1] dọc dầm.
///
///     Quy ước Revit cho section box Transform:
///       BasisX = phương "phải" trên view, BasisY = phương "lên" trên view,
///       BasisZ = phương NHÌN (view direction, hướng ra khỏi mặt phẳng cắt về phía mắt).
///     Min/Max của bbox nằm trong hệ toạ độ Transform đó; mặt phẳng cắt tại Origin, chiều sâu
///     cắt là khoảng [Min.Z, Max.Z] (giá trị âm = phía sau mặt cắt theo hướng nhìn).
/// </summary>
public sealed class SectionPlaneCalculator
{
    /// <summary>Đệm cho mặt cắt dọc (rộng hơn để chứa tag/dimension dọc nhịp).</summary>
    private const double MarginFeet = 1.5; // ~450mm

    /// <summary>Đệm cho mặt cắt ngang — nhỏ để view ôm sát tiết diện (chừa chỗ tag bên phải).</summary>
    private const double CrossMarginFeet = 0.33; // ~100mm

    /// <summary>Chiều sâu cắt mỗi bên mặt phẳng cho mặt cắt dọc.</summary>
    private const double HalfDepthFeet = 2.0; // ~600mm

    /// <summary>Chiều sâu cắt mỏng cho mặt cắt ngang — chỉ thấy 1 lát tiết diện, không nhìn xuyên dài.</summary>
    private const double CrossHalfDepthFeet = 0.4; // ~120mm

    /// <summary>
    ///     Mặt cắt dọc: mắt nhìn ngang vuông góc trục dầm; trục X view = hướng dầm, trục Y view = thẳng đứng.
    /// </summary>
    public BoundingBoxXYZ CreateSectionalBox(BeamGeometry beam)
    {
        var dir = beam.Direction;                              // hướng dầm (ngang)
        var up = XYZ.BasisZ;                                   // thẳng đứng
        var viewDir = dir.CrossProduct(up).Normalize();        // phương nhìn ngang, vuông góc trục dầm

        // Tâm dầm: giữa trục dầm theo chiều dài, giữa chiều cao theo Z.
        var midAxis = (beam.Start + beam.End) * 0.5;
        var center = new XYZ(midAxis.X, midAxis.Y, (beam.TopElevationFeet + beam.BottomElevationFeet) * 0.5);

        var halfLength = beam.LengthFeet * 0.5 + MarginFeet;
        var halfHeight = (beam.TopElevationFeet - beam.BottomElevationFeet) * 0.5 + MarginFeet;

        return BuildBox(center, dir, up, viewDir, halfLength, halfHeight);
    }

    /// <summary>
    ///     Mặt cắt ngang tại vị trí t dọc dầm (t=0 đầu, t=1 cuối): mắt nhìn dọc theo trục dầm;
    ///     trục X view = phương ngang vuông góc trục dầm, trục Y view = thẳng đứng.
    /// </summary>
    public BoundingBoxXYZ CreateCrossSectionBox(BeamGeometry beam, double t)
    {
        var dir = beam.Direction;                              // trục dầm = phương nhìn
        var up = XYZ.BasisZ;
        var right = up.CrossProduct(dir).Normalize();          // phương ngang vuông góc trục dầm

        var axisPoint = beam.Start + dir * (beam.LengthFeet * t);
        var center = new XYZ(axisPoint.X, axisPoint.Y, (beam.TopElevationFeet + beam.BottomElevationFeet) * 0.5);

        // Ôm sát tiết diện: chừa nhiều bên phải cho tag (1.5x bề rộng), ít các phía khác.
        var halfWidth = ToFeet(beam.Section.WidthMm) * 0.5 + CrossMarginFeet;
        var halfHeight = (beam.TopElevationFeet - beam.BottomElevationFeet) * 0.5 + CrossMarginFeet;

        return BuildBox(center, right, up, dir, halfWidth, halfHeight, CrossHalfDepthFeet);
    }

    private static BoundingBoxXYZ BuildBox(XYZ center, XYZ basisX, XYZ basisY, XYZ viewDir,
        double halfX, double halfY, double halfDepth = HalfDepthFeet)
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

    private static double ToFeet(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
}
