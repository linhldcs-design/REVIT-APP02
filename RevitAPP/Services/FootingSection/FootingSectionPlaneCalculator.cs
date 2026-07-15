using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.FootingSection;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Tính <see cref="BoundingBoxXYZ"/> cho mặt cắt đứng móng: mặt phẳng cắt đi NGAY GIỮA TIM móng.
///     Quan trọng: <c>ViewSection.CreateSection</c> đặt cut plane tại MẶT Max.Z của box (mặt gần mắt),
///     KHÔNG phải tâm box. Nên phải dời tâm box đi -HalfDepth theo phương nhìn để mặt Max.Z rơi đúng tim.
///     BasisX = hướng cắt (phải trên view), BasisY = lên (Z), BasisZ = phương NHÌN.
///     Far Clip Offset (= tổng chiều sâu box) ép về 500mm; toàn bộ 500mm nằm phía SAU tim (về phía xa mắt).
/// </summary>
public sealed class FootingSectionPlaneCalculator
{
    /// <summary>Far Clip Offset đích của mặt cắt móng (mm) = tổng chiều sâu section box.</summary>
    public const double FarClipOffsetMm = 500.0;

    private const double WidthMarginFeet = 0.5;   // ~150mm đệm 2 bên bề rộng
    private const double BottomMarginFeet = 150.0 / 304.8;
    private const double BreakLineBelowTopFeet = 150.0 / 304.8;
    private const double CropAboveBreakLineFeet = 50.0 / 304.8;
    // Nửa chiều sâu = FarClip/2 → tổng đúng 500mm (Revit hiển thị Far Clip Offset = Max.Z - Min.Z).
    private static readonly double HalfDepthFeet = BeamSectionBoxMath.HalfDepthFeet(FarClipOffsetMm);

    public BoundingBoxXYZ CreateBox(FootingSectionGeometry footing)
    {
        var cutDir = new XYZ(footing.CutDirection.X, footing.CutDirection.Y, footing.CutDirection.Z).Normalize();
        var up = XYZ.BasisZ;
        var viewDir = cutDir.CrossProduct(up).Normalize();

        var viewBottomZ = footing.ViewBottomZFeet ?? footing.BottomZFeet;
        var viewTopZ = footing.ViewTopZFeet ?? footing.TopZFeet;
        var centerZ = (viewTopZ + viewBottomZ) * 0.5;
        var tim = new XYZ(footing.Center.X, footing.Center.Y, centerZ);
        // Cut plane = mặt Min.Z (local Z=-HalfDepth) của box theo phương nhìn (verify thật: Revit lấy Min.Z,
        // không phải Max.Z). Dời tâm box +HalfDepth theo viewDir để mặt cắt trùng ĐÚNG tim móng.
        var boxOrigin = tim + viewDir * HalfDepthFeet;

        var halfWidth = footing.WidthFeet * 0.5 + WidthMarginFeet;
        var localBottom = viewBottomZ - centerZ - BottomMarginFeet;
        // Crop top chỉ vượt qua break line cột 50mm: (TopZ - 150mm) + 50mm.
        var localTop = viewTopZ - centerZ - BreakLineBelowTopFeet + CropAboveBreakLineFeet;

        var transform = Transform.Identity;
        transform.Origin = boxOrigin;
        transform.BasisX = cutDir;
        transform.BasisY = up;
        transform.BasisZ = viewDir;

        return new BoundingBoxXYZ
        {
            Transform = transform,
            Min = new XYZ(-halfWidth, localBottom, -HalfDepthFeet),
            Max = new XYZ(halfWidth, localTop, HalfDepthFeet)
        };
    }
}
