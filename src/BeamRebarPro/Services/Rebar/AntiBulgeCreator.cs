using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services.Rebar;

/// <summary>
///     Creates side/skin reinforcement for deep beams. Longitudinal side bars follow the beam span.
///     Tie bars are distributed inside each span only, so they do not run through columns.
/// </summary>
public sealed class AntiBulgeCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;
    private readonly CoverSettings _cover;

    public AntiBulgeCreator(Document document, RebarFamilyValidator families, CoverSettings cover)
    {
        _document = document;
        _families = families;
        _cover = cover;
    }

    public int Create(Element host, SpanFrame frame, AntiBulgeConfig config,
        double leftHalfFeet, double rightHalfFeet, List<string> warnings)
    {
        if (!config.Enabled) return 0;

        var heightMm = frame.HeightFeet * 304.8;
        if (heightMm <= config.HeightThresholdMm) return 0;

        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bo qua thep chong phinh D{config.Diameter.Millimeters}: thieu RebarBarType.");
            return 0;
        }

        var tieType = _families.GetBarType(config.TieDiameter);
        if (tieType == null)
        {
            warnings.Add($"Bo qua tie chong phinh D{config.TieDiameter.Millimeters}: thieu RebarBarType.");
            return 0;
        }
        var tieHook = _families.GetHookType(HookAngle.Deg180);

        var coverSide = _cover.SideMm / 304.8;
        var coverTop = _cover.TopMm / 304.8;
        var coverBottom = _cover.BottomMm / 304.8;
        var bar = config.Diameter.Feet;
        var tieBar = config.TieDiameter.Feet;
        var usableHeight = frame.HeightFeet - coverTop - coverBottom;
        var rows = Math.Max(1, config.Count);

        var halfWidth = Math.Max(0, frame.WidthFeet / 2 - coverSide - bar / 2);
        var tieHalfWidth = Math.Max(0, frame.WidthFeet / 2 - coverSide - tieBar / 2);

        // KHÔNG băng qua cột: thanh dọc + tie lùi khỏi MÉP CỘT (tim ± nửa bề rộng cột) + cover, giống đai.
        var spanLen = frame.LengthFeet;
        var startInset = leftHalfFeet + coverSide;
        var endInset = rightHalfFeet + coverSide;
        var longitudinalEmbedFeet = Math.Max(0, config.ColumnEmbedMm) / 304.8;
        var longitudinalStartInset = longitudinalEmbedFeet > 1e-6
            ? Math.Max(0, leftHalfFeet - longitudinalEmbedFeet)
            : startInset;
        var longitudinalEndInset = longitudinalEmbedFeet > 1e-6
            ? Math.Max(0, rightHalfFeet - longitudinalEmbedFeet)
            : endInset;
        var startT = spanLen > 1e-6 ? longitudinalStartInset / spanLen : 0;
        var endT = spanLen > 1e-6 ? 1 - longitudinalEndInset / spanLen : 1;
        if (endT <= startT) return 0;

        var created = 0;
        var rowVerticals = new List<double>();

        for (var r = 1; r <= rows; r++)
        {
            var vertical = -(coverTop + usableHeight * r / (rows + 1));
            rowVerticals.Add(vertical);
            foreach (var lateral in new[] { -halfWidth, halfWidth })
            {
                var p0 = frame.AxisTop(startT) + frame.Across * lateral + frame.Up * vertical;
                var p1 = frame.AxisTop(endT) + frame.Across * lateral + frame.Up * vertical;
                if (TryCreateStraight(host, barType, frame.Up, p0, p1, $"thep chong phinh D{config.Diameter.Millimeters}", warnings))
                    created++;
            }
        }

        var spacingFeet = config.SpacingMm / 304.8;
        // Tie cũng lùi khỏi mép cột (giống thanh dọc) → không băng qua cột.
        var tieStart = startInset;
        var tieEnd = Math.Max(tieStart, frame.LengthFeet - endInset);
        if (spacingFeet <= 1e-6 || tieEnd <= tieStart || tieHalfWidth <= 1e-6)
            return created;

        // Tie dời LÊN để thanh ngang nằm ở MÉP TRÊN 2 thanh dọc, móc 180° quặp xuống ôm thanh (như mẫu).
        // tieV = vertical - tieDropFeet; drop ÂM → tie lên (vertical bớt âm). Lên nửa (thanh dọc + tie).
        var tieDropFeet = -(bar + tieBar) / 2;

        // Mỗi hàng tie: tạo 1 tie tại đầu nhịp rồi rải bằng MAXIMUM SPACING (Revit tự chia đều sao cho
        // bước ≤ SpacingMm) thay vì đặt từng thanh thủ công.
        foreach (var vertical in rowVerticals)
        {
            var tieV = vertical - tieDropFeet;
            var p0 = frame.AxisTop(0) + frame.Along * tieStart - frame.Across * tieHalfWidth + frame.Up * tieV;
            var p1 = frame.AxisTop(0) + frame.Along * tieStart + frame.Across * tieHalfWidth + frame.Up * tieV;
            created += CreateTieRowMaxSpacing(host, tieType, tieHook, frame, p0, p1,
                tieEnd - tieStart, spacingFeet, config.TieDiameter, warnings);
        }

        return created;
    }

    /// <summary>Tạo 1 hàng tie và rải dọc nhịp theo MAXIMUM SPACING (bước ≤ spacingFeet, Revit chia đều).</summary>
    private int CreateTieRowMaxSpacing(Element host, RebarBarType barType, RebarHookType? hookType,
        SpanFrame frame, XYZ leftPoint, XYZ rightPoint, double rowLengthFeet, double spacingFeet,
        RebarDiameter diameter, List<string> warnings)
    {
        try
        {
            IList<Curve> curves = [Line.CreateBound(leftPoint, rightPoint)];
            var rebar = RebarCompat.CreateFromCurves(
                _document, RebarStyle.Standard, barType, hookType, hookType, host,
                frame.Along, curves,
                right: false, useExistingShapeIfPossible: true);

            if (rowLengthFeet > spacingFeet && rebar.IsRebarShapeDriven())
                rebar.GetShapeDrivenAccessor()
                    .SetLayoutAsMaximumSpacing(spacingFeet, rowLengthFeet, true, true, true);

            return 1;
        }
        catch (Exception ex)
        {
            warnings.Add($"Loi tao hang tie chong phinh D{diameter.Millimeters}: {ex.Message}");
            return 0;
        }
    }


    private bool TryCreateStraight(Element host, RebarBarType barType, XYZ normal, XYZ p0, XYZ p1,
        string label, List<string> warnings)
    {
        try
        {
            IList<Curve> curves = [Line.CreateBound(p0, p1)];
            RebarCompat.CreateFromCurves(
                _document, RebarStyle.Standard, barType, null, null, host,
                normal, curves,
                right: true, useExistingShapeIfPossible: true);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"Loi tao {label}: {ex.Message}");
            return false;
        }
    }
}
