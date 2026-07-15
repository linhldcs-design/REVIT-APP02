using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services.Rebar;

/// <summary>
///     Đai C giữ thép gia cường lớp 2 (khi ≥3 cây): thanh ngang + 2 hook 180° quặp lên ôm 1 cặp thanh gia
///     cường kề nhau, rải dọc nhịp theo bước. Đai C LÙI khỏi mép cột (không xuyên ngang cột), giống đai chính.
/// </summary>
public sealed class AdditionalTieCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;
    private readonly CoverSettings _cover;

    public AdditionalTieCreator(Document document, RebarFamilyValidator families, CoverSettings cover)
    {
        _document = document;
        _families = families;
        _cover = cover;
    }

    /// <summary>
    ///     Tạo đai C cho 1 lớp thép gia cường. <paramref name="config"/> phải có TieCSpacingMm &gt; 0, Count ≥ 3,
    ///     Layer 2. <paramref name="atTop"/> true = thép gia cường trên (đai ở mặt trên), false = dưới.
    ///     <paramref name="leftHalfFeet"/>/<paramref name="rightHalfFeet"/> = nửa bề rộng cột 2 đầu → lùi khỏi cột.
    /// </summary>
    public int Create(Element host, SpanFrame frame, AdditionalBarConfig config, bool atTop,
        double layerOffsetFeet, double leftHalfFeet, double rightHalfFeet, List<string> warnings)
    {
        if (config.TieCSpacingMm <= 0 || config.TieCDiameterMm <= 0 || config.Count < 3 || config.Layer != 2)
            return 0;

        var tieDia = new RebarDiameter((int)Math.Round(config.TieCDiameterMm));
        var tieType = _families.GetBarType(tieDia);
        if (tieType == null)
        {
            warnings.Add($"Bo qua dai C giu thep gia cuong D{tieDia.Millimeters}: thieu RebarBarType.");
            return 0;
        }
        var hook = _families.GetHookType(HookAngle.Deg180);

        var coverSide = _cover.SideMm / 304.8;
        var coverTop = _cover.TopMm / 304.8;
        var coverBottom = _cover.BottomMm / 304.8;
        var barFeet = config.Diameter.Feet;       // đường kính thép gia cường (cho vị trí ngang).
        var tieBar = tieDia.Feet;

        // Cao độ thép gia cường lớp 2 (theo atTop + layerOffset).
        var vertical = atTop
            ? -(coverTop + barFeet / 2 + layerOffsetFeet)
            : -(frame.HeightFeet - coverBottom - barFeet / 2 - layerOffsetFeet);

        // Vị trí ngang các thanh gia cường: phân bố đều trong usableHalf (thép nằm trong đai chính).
        var usableHalf = Math.Max(0, frame.WidthFeet / 2 - coverSide - tieBar - barFeet / 2);
        var n = config.Count;
        double LatOf(int k) => n <= 1 ? 0 : -usableHalf + k * (2 * usableHalf / (n - 1));

        var spanLen = frame.LengthFeet;
        var spacingFeet = config.TieCSpacingMm / 304.8;
        if (spacingFeet <= 1e-6) return 0;

        // Thanh ngang tie nằm ở mặt thép gia cường về phía GIỮA DẦM (top: mép DƯỚI thanh GC; bottom: mép
        // TRÊN), móc 180° quặp ngược ÔM TRỌN thanh. tie cách tâm thanh GC = nửa D thép GC + nửa D tie.
        // (Top: thép GC nằm DƯỚI thép chủ → tie phải xuống, KHÔNG lên kẻo ôm nhầm thép chủ.)
        var offsetToInner = barFeet / 2 + tieBar / 2;
        // Z ÂM: trừ = xuống (thấp), cộng = lên (cao).
        var tieV = atTop
            ? vertical - offsetToInner - barFeet / 4   // top: tie XUỐNG (thấp hơn) để ôm khít.
            : vertical - barFeet / 4;                   // bottom: lên xíu (đã đúng).

        // 1 đai C ôm từ CÂY BIÊN NGOÀI đến CÂY BIÊN NGOÀI (thanh 0 → n-1). Cạnh đai MỞ RỘNG ra ngoài tâm
        // thanh biên = nửa đường kính thép gia cường + nửa đường kính đai → đai BAO TRỌN thanh (thanh nằm trong).
        var grow = barFeet / 2 + tieBar / 2;
        var latL = LatOf(0) - grow;
        var latR = LatOf(n - 1) + grow;

        // Đai C chỉ rải trong VÙNG có thép gia cường (top: 2 đoạn gối; bottom: 1 đoạn giữa nhịp), KHÔNG suốt nhịp.
        var created = 0;
        foreach (var (rs, re) in AdditionalBarRanges(frame, config, atTop, leftHalfFeet, rightHalfFeet))
        {
            var rowLen = re - rs;
            if (rowLen <= spacingFeet) continue;
            var p0 = frame.AxisTop(rs / spanLen) + frame.Across * latL + frame.Up * tieV;
            var p1 = frame.AxisTop(rs / spanLen) + frame.Across * latR + frame.Up * tieV;
            created += CreateTieRow(host, tieType, hook, frame, p0, p1, rowLen, spacingFeet, tieDia, warnings);
        }
        return created;
    }

    /// <summary>Các đoạn dọc (feet) có thép gia cường lớp 2 — khớp cách AdditionalBarCreator cắt thanh.
    ///     Top (quanh gối): 2 đoạn 2 đầu (0.25L mỗi bên từ mép cột). Bottom (giữa nhịp): 1 đoạn 1/8..7/8 L thông thủy
    ///     hoặc theo Length/Anchor.</summary>
    private static IEnumerable<(double Start, double End)> AdditionalBarRanges(SpanFrame frame,
        AdditionalBarConfig config, bool atTop, double leftHalfFeet, double rightHalfFeet)
    {
        var spanLen = frame.LengthFeet;
        var clearStart = leftHalfFeet;
        var clearEnd = spanLen - rightHalfFeet;
        var clearLen = clearEnd - clearStart;
        if (clearLen <= 1e-6) yield break;

        if (!atTop) // BOTTOM: 1 đoạn giữa nhịp — KHỚP công thức AdditionalBarCreator.CreateMidspan (đọc Length/Anchor).
        {
            double s, e;
            if (config.LeftLengthMm > 0 || config.RightLengthMm > 0 || config.AnchorLeftMm != 0 || config.AnchorRightMm != 0)
            {
                // Đai C theo ĐÚNG vùng thép gia cường người dùng sửa: 2 đầu thanh cách mép cột Anchor.
                s = clearStart + config.AnchorLeftMm / 304.8;
                e = clearEnd - config.AnchorRightMm / 304.8;
            }
            else if (config.LengthMm > 0)
            {
                var len = Math.Min(clearLen, config.LengthMm / 304.8);
                var mid = (clearStart + clearEnd) / 2;
                s = mid - len / 2; e = mid + len / 2;
            }
            else
            {
                s = clearStart + clearLen / 8.0;
                e = clearStart + clearLen * 7.0 / 8.0;
            }
            if (e > s) yield return (s, e);
        }
        else // TOP: 2 đoạn quanh 2 gối — KHỚP CreateTopAdditionalAtSupports: gối ± (HalfWidth + extend),
             // extend = Left/Right Length người dùng sửa (hoặc ratio×span). Đai C đi từ ĐẦU NHỊP (qua gối)
             // vào trong 1 đoạn extend (vì thép GC top vắt qua gối; trong 1 span chỉ thấy nửa phía nhịp này).
        {
            // extend mỗi bên (feet). LeftLength dùng cho gối ĐẦU span, RightLength cho gối CUỐI span.
            var leftExtend = config.LeftLengthMm > 0 ? config.LeftLengthMm / 304.8
                           : (config.LengthMm > 0 ? config.LengthMm / 304.8 : spanLen * 0.25);
            var rightExtend = config.RightLengthMm > 0 ? config.RightLengthMm / 304.8
                            : (config.LengthMm > 0 ? config.LengthMm / 304.8 : spanLen * 0.25);

            // Đoạn quanh gối ĐẦU span: từ đầu nhịp (0) đến (mép cột phải của gối đầu + extend).
            var endHead = Math.Min(spanLen, leftHalfFeet + leftExtend);
            if (endHead > 0) yield return (0, endHead);

            // Đoạn quanh gối CUỐI span: từ (mép cột trái của gối cuối − extend) đến cuối nhịp.
            var startTail = Math.Max(0, spanLen - rightHalfFeet - rightExtend);
            if (startTail < spanLen && startTail > endHead) yield return (startTail, spanLen);
        }
    }

    private int CreateTieRow(Element host, RebarBarType barType, RebarHookType? hookType, SpanFrame frame,
        XYZ leftPoint, XYZ rightPoint, double rowLengthFeet, double spacingFeet, RebarDiameter diameter,
        List<string> warnings)
    {
        try
        {
            IList<Curve> curves = [Line.CreateBound(leftPoint, rightPoint)];
            var rebar = RebarCompat.CreateFromCurves(
                _document, RebarStyle.Standard, barType, hookType, hookType, host,
                frame.Along, curves,
                right: true, useExistingShapeIfPossible: true);

            if (rowLengthFeet > spacingFeet && rebar.IsRebarShapeDriven())
                rebar.GetShapeDrivenAccessor()
                    .SetLayoutAsMaximumSpacing(spacingFeet, rowLengthFeet, true, true, true);

            return 1;
        }
        catch (Exception ex)
        {
            warnings.Add($"Loi tao dai C giu thep gia cuong D{diameter.Millimeters}: {ex.Message}");
            return 0;
        }
    }
}
