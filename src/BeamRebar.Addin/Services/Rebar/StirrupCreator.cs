using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamRebar.Core.Models;

namespace BeamRebar.Addin.Services.Rebar;

/// <summary>
///     Tạo cốt đai cho một nhịp. TwoEnds: 3 vùng — End1 (@A1), giữa (@A2), End2 (@A1); vùng đai dày
///     mỗi đầu = EndZoneLength (mặc định L/4). Uniform: một vùng @A1 suốt nhịp. Mỗi vùng là một Rebar
///     shape-driven với layout NumberWithSpacing. Đai = polyline chữ nhật trong tiết diện trừ cover.
/// </summary>
public sealed class StirrupCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;
    private readonly CoverSettings _cover;

    public StirrupCreator(Document document, RebarFamilyValidator families, CoverSettings cover)
    {
        _document = document;
        _families = families;
        _cover = cover;
    }

    public int Create(Element host, SpanFrame frame, StirrupConfig config, List<string> warnings)
    {
        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua cốt đai D{config.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        var length = frame.LengthFeet;
        var endZone = config.EndZoneLengthMm > 0 ? config.EndZoneLengthMm / 304.8 : length / 4;
        endZone = Math.Min(endZone, length / 2);

        var created = 0;
        if (config.Mode == StirrupMode.Uniform)
        {
            created += CreateZone(host, frame, barType, 0, length, config.SpacingEndMm / 304.8, true, warnings);
        }
        else
        {
            var a1 = config.SpacingEndMm / 304.8;
            var a2 = config.SpacingMidMm / 304.8;
            // includeFirst=false ở vùng giữa & cuối: thanh ranh giới đã thuộc vùng trước → tránh đếm trùng.
            created += CreateZone(host, frame, barType, 0, endZone, a1, true, warnings);
            created += CreateZone(host, frame, barType, endZone, length - endZone, a2, false, warnings);
            created += CreateZone(host, frame, barType, length - endZone, length, a1, false, warnings);
        }

        return created;
    }

    private int CreateZone(Element host, SpanFrame frame, RebarBarType barType,
        double fromFeet, double toFeet, double spacingFeet, bool includeFirst, List<string> warnings)
    {
        var zoneLength = toFeet - fromFeet;
        if (zoneLength <= 1e-6 || spacingFeet <= 1e-6) return 0;

        var count = Math.Max(1, (int)Math.Floor(zoneLength / spacingFeet) + 1);
        // Bỏ thanh đầu khi vùng kế tiếp (đã có thanh ranh giới ở vùng trước) → đặt thanh đầu lùi 1 bước.
        var startFeet = includeFirst ? fromFeet : fromFeet + spacingFeet;
        if (!includeFirst) count -= 1;
        if (count <= 0) return 0;
        var tFrom = startFeet / frame.LengthFeet;

        try
        {
            var profile = BuildStirrupProfile(frame, tFrom);
            var rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                _document, RebarStyle.StirrupTie, barType, null, null, host,
                frame.Along, profile, RebarHookOrientation.Right, RebarHookOrientation.Right,
                useExistingShapeIfPossible: true, createNewShape: true);

            if (rebar.IsRebarShapeDriven())
            {
                var accessor = rebar.GetShapeDrivenAccessor();
                accessor.SetLayoutAsNumberWithSpacing(
                    count, spacingFeet, barsOnNormalSide: true,
                    includeFirstBar: true, includeLastBar: true);
            }
            return count;
        }
        catch (Exception ex)
        {
            warnings.Add($"Lỗi tạo cốt đai (vùng {fromFeet:F2}-{toFeet:F2}ft): {ex.Message}");
            return 0;
        }
    }

    /// <summary>Polyline chữ nhật kín của đai trong tiết diện tại tham số dọc t, trừ lớp bảo vệ.</summary>
    private IList<Curve> BuildStirrupProfile(SpanFrame frame, double t)
    {
        var coverSide = _cover.SideMm / 304.8;
        var coverTop = _cover.TopMm / 304.8;
        var coverBottom = _cover.BottomMm / 304.8;

        var halfW = frame.WidthFeet / 2 - coverSide;
        var top = -coverTop;
        var bottom = -(frame.HeightFeet - coverBottom);

        var center = frame.AxisTop(t);
        var tl = center + frame.Across * -halfW + frame.Up * top;
        var tr = center + frame.Across * halfW + frame.Up * top;
        var br = center + frame.Across * halfW + frame.Up * bottom;
        var bl = center + frame.Across * -halfW + frame.Up * bottom;

        return
        [
            Line.CreateBound(tl, tr),
            Line.CreateBound(tr, br),
            Line.CreateBound(br, bl),
            Line.CreateBound(bl, tl)
        ];
    }
}
