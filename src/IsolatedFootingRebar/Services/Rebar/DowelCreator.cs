using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services.Rebar;

/// <summary>
///     Tạo thanh kê đỡ lưới thép lớp trên (bar chair / chân chó). Mỗi thanh hình ghế (chữ Π): hai chân
///     ngang tựa lên lưới đáy, thân đứng cao bằng khoảng cách hai lưới, đỉnh đỡ lưới trên. Kích thước ghế
///     (bề ngang đỉnh, chiều dài chân) tùy chỉnh. Mỗi hàng (theo DirX) là MỘT rebar set rải đều theo bước
///     dọc DirY (MaximumSpacing) — không tạo từng thanh đơn lẻ.
/// </summary>
public sealed class DowelCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;

    public DowelCreator(Document document, RebarFamilyValidator families)
    {
        _document = document;
        _families = families;
    }

    public int Create(Element host, FootingFrame frame, PedestalBox? pedestal,
        VerticalBarConfig config, CoverSettings cover,
        double bottomMeshStackFeet, double topMeshStackFeet, List<string> warnings)
    {
        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua thép kê D{config.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        var sideCover = cover.SideMm / 304.8;
        var usableX = frame.WidthXFeet - 2 * sideCover;
        var usableY = frame.WidthYFeet - 2 * sideCover;
        if (usableX <= 1e-6 || usableY <= 1e-6)
        {
            warnings.Add("Bỏ qua thép kê: móng quá nhỏ so với lớp bảo vệ.");
            return 0;
        }

        var bar = config.Diameter.Feet;
        // Chân kê nằm TRÊN cụm lưới đáy (cover + bề dày 2 lớp lưới + nửa thanh kê).
        // Đỉnh kê nằm DƯỚI cụm lưới trên (tương tự, tính từ mặt trên xuống).
        var footZ = frame.BottomZFeet + cover.BottomMm / 304.8 + bottomMeshStackFeet + bar / 2;
        var topZ = frame.BaseTopZFeet - cover.TopMm / 304.8 - topMeshStackFeet - bar / 2;
        if (topZ - footZ <= bar)
        {
            warnings.Add("Bỏ qua thép kê: chiều cao giữa lưới đáy và lưới trên quá nhỏ.");
            return 0;
        }

        // Bề ngang đỉnh ghế: từ WidthMm (0 → tự suy = chiều cao thân). Chiều dài chân ngang = HookLengthMm.
        var topSpanFeet = config.WidthMm > 0 ? config.WidthMm / 304.8 : topZ - footZ;
        topSpanFeet = Math.Min(topSpanFeet, usableX);
        var footFeet = config.HookLengthMm / 304.8;

        var spacingXFeet = config.SpacingXMm / 304.8;
        var spacingYFeet = config.UseSpacing && config.SpacingYMm > 0
            ? config.SpacingYMm / 304.8
            : usableY / Math.Max(1, config.CountY - 1);

        // Lùi mép: thanh kê KHÔNG đặt sát thành móng. Lùi vào trong nửa bước (tối thiểu 100mm), tính cả
        // đoạn chân ngang chìa ra (footFeet) để chân không chạm/vượt mép.
        var marginX = Math.Max(spacingXFeet / 2, 100.0 / 304.8) + footFeet + topSpanFeet / 2;
        var marginY = Math.Max(spacingYFeet / 2, 100.0 / 304.8);

        var fieldX = frame.WidthXFeet - 2 * (sideCover + marginX); // dải đặt TÂM hàng theo X.
        var fieldY = frame.WidthYFeet - 2 * (sideCover + marginY); // dải rải theo Y.
        if (fieldX <= 1e-6 || fieldY <= 1e-6)
        {
            warnings.Add("Bỏ qua thép kê: móng quá nhỏ để lùi thanh kê khỏi mép.");
            return 0;
        }

        // Số hàng kê theo X sau khi đã lùi mép.
        var nx = config.UseSpacing
            ? FootingMath.SpacingToCount(FootingMath.FeetToMm(fieldX), config.SpacingXMm)
            : Math.Max(1, config.CountX);
        if (nx < 1) nx = 1;

        var startUx = (sideCover + marginX) / frame.WidthXFeet;
        var fieldUx = fieldX / frame.WidthXFeet;
        var halfTopUx = topSpanFeet / 2 / frame.WidthXFeet;

        var startVy = (sideCover + marginY) / frame.WidthYFeet;

        var created = 0;
        for (var ix = 0; ix < nx; ix++)
        {
            var u = nx == 1 ? 0.5 : startUx + fieldUx * ix / (nx - 1);
            var uLeft = u - halfTopUx;
            var uRight = u + halfTopUx;

            if (CreateChairRow(host, frame, barType, uLeft, uRight, footZ, topZ, footFeet,
                    spacingYFeet, fieldY, startVy, config.Diameter, warnings))
                created++;
        }

        return created;
    }

    /// <summary>
    ///     Một HÀNG ghế kê tại vị trí <paramref name="uLeft"/>..<paramref name="uRight"/> (theo DirX),
    ///     dựng tại mép -Y rồi rải đều theo DirY (MaximumSpacing) phủ hết dải usableY. Ghế nằm trong mặt
    ///     phẳng (DirX, Z) nên đồng phẳng — rải theo DirY (pháp tuyến mặt phẳng).
    /// </summary>
    private bool CreateChairRow(Element host, FootingFrame frame, RebarBarType barType,
        double uLeft, double uRight, double footZ, double topZ, double footFeet,
        double spacingYFeet, double fieldYFeet, double startVy, RebarDiameter diameter,
        List<string> warnings)
    {
        var v0 = startVy; // thanh đầu đã lùi khỏi mép -Y một khoảng margin.

        var pLegLeft = frame.PointAt(uLeft, v0, footZ);
        var pUpLeft = frame.PointAt(uLeft, v0, topZ);
        var pUpRight = frame.PointAt(uRight, v0, topZ);
        var pLegRight = frame.PointAt(uRight, v0, footZ);
        var footLeftEnd = pLegLeft - frame.DirX * footFeet;
        var footRightEnd = pLegRight + frame.DirX * footFeet;

        IList<Curve> curves =
        [
            Line.CreateBound(footLeftEnd, pLegLeft), // chân ngang trái (tựa lưới đáy)
            Line.CreateBound(pLegLeft, pUpLeft),     // thân đứng trái
            Line.CreateBound(pUpLeft, pUpRight),     // đỉnh ngang (đỡ lưới trên)
            Line.CreateBound(pUpRight, pLegRight),   // thân đứng phải
            Line.CreateBound(pLegRight, footRightEnd) // chân ngang phải
        ];

        Autodesk.Revit.DB.Structure.Rebar rebar;
        try
        {
            rebar = RebarCompat.CreateFromCurves(
                _document, RebarStyle.Standard, barType, null, null, host,
                frame.DirY, curves,
                right: true, useExistingShapeIfPossible: false);
        }
        catch (Exception ex)
        {
            warnings.Add($"Lỗi tạo thép kê D{diameter.Millimeters}: {ex.Message}");
            return false;
        }

        if (rebar.IsRebarShapeDriven() && fieldYFeet > spacingYFeet)
        {
            try
            {
                rebar.GetShapeDrivenAccessor()
                    .SetLayoutAsMaximumSpacing(spacingYFeet, fieldYFeet, true, true, true);
                rebar.Document.Regenerate();
            }
            catch (Exception ex)
            {
                warnings.Add($"Lỗi rải thép kê D{diameter.Millimeters}: {ex.Message}");
            }
        }

        return true;
    }
}
