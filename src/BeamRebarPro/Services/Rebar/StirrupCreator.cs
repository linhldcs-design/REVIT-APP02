using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services.Rebar;

/// <summary>
///     Tạo cốt đai chữ nhật kín dọc nhịp. TwoEnds: ba vùng (End1 @A1, Mid @A2, End2 @A1) với chiều dài
///     vùng dày = EndZoneLengthMm hoặc tự suy L/4 mỗi đầu. Uniform: một vùng @A1 suốt nhịp. Mỗi vùng là
///     một Rebar với layout số lượng theo bước.
/// </summary>
public sealed class StirrupCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;
    private readonly CoverSettings _cover;

    private sealed record SecondaryStirrupRange(
        double StationFeet,
        double HalfWidthFeet,
        double LeftFaceFeet,
        double RightFaceFeet,
        double LeftStartFeet,
        double LeftEndFeet,
        double RightStartFeet,
        double RightEndFeet);

    public StirrupCreator(Document document, RebarFamilyValidator families, CoverSettings cover)
    {
        _document = document;
        _families = families;
        _cover = cover;
    }

    public int Create(Element host, SpanFrame frame, StirrupConfig config, List<string> warnings,
        IReadOnlyList<SecondaryStirrupStation>? secondaryStationsFeet = null, int mainBarCount = 0,
        double mainBarDiameterMm = 0)
    {
        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua cốt đai D{config.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        // Móc neo đai 135° (TCVN) — lấy hook type nếu document có, để đai khép kín có móc ở góc.
        var hook = _families.GetHookType(HookAngle.Deg135);

        var length = frame.LengthFeet;
        const double secondaryTieSpacingMm = 50.0;
        var secondaryTieSpacingFeet = secondaryTieSpacingMm / 304.8;
        var secondaryRanges = BuildSecondaryRanges(secondaryStationsFeet, length, secondaryTieSpacingFeet, config.Diameter, warnings);
        var created = 0;
        if (config.Mode == StirrupMode.Uniform)
            created += CreateZone(host, frame, barType, hook, 0, length, config.SpacingEndMm, config.Diameter, warnings, secondaryRanges);
        else
            created += CreateTwoEnds(host, frame, barType, hook, config, length, warnings, secondaryRanges);

        // Đai tăng cường (TCVN) tại vị trí dầm phụ gác lên dầm chính: cụm đai dày bước 1/2 (tối thiểu 100mm)
        // trải ±chiều cao dầm quanh mỗi dầm phụ. Đai treo này chịu lực tập trung do dầm phụ truyền xuống.
        if (secondaryRanges.Count > 0)
        {
            var secondaryZoneLengthFeet = 3 * secondaryTieSpacingFeet;
            foreach (var range in secondaryRanges)
            {
                created += CreateMaxSpacingZone(host, frame, barType, hook, range.LeftEndFeet,
                    secondaryTieSpacingFeet, secondaryZoneLengthFeet, barsOnNormalSide: false,
                    label: "trai", config.Diameter, warnings);
                created += CreateMaxSpacingZone(host, frame, barType, hook, range.RightStartFeet,
                    secondaryTieSpacingFeet, secondaryZoneLengthFeet, barsOnNormalSide: true,
                    label: "phai", config.Diameter, warnings);
            }
        }

        // Đai phụ (Additional Stirrup): đai con ôm các thanh chủ giữa, rải cùng vùng/bước với đai chính.
        var addStirrupCount = 0;
        foreach (var add in config.AdditionalStirrups)
        {
            if (!add.Enabled) continue;
            addStirrupCount += CreateAdditionalStirrup(host, frame, config, add, mainBarCount, mainBarDiameterMm, length, warnings, secondaryRanges);
        }
        created += addStirrupCount;
        if (config.AdditionalStirrups.Count > 0)
            warnings.Add($"[Dai phu] yeu cau {config.AdditionalStirrups.Count} loai, mainBarCount={mainBarCount}, da tao {addStirrupCount} cum dai phu.");

        return created;
    }

    /// <summary>Đai phụ chữ nhật con ôm dải thanh chủ [FromBarIndex..ToBarIndex] (theo phương ngang), cao
    ///     bằng đai chính, rải cùng vùng đai chính (TwoEnds/Uniform).</summary>
    private int CreateAdditionalStirrup(Element host, SpanFrame frame, StirrupConfig config,
        AdditionalStirrupConfig add, int mainBarCount, double mainBarDiameterMm, double length, List<string> warnings,
        IReadOnlyList<SecondaryStirrupRange> blockedRanges)
    {
        var barType = _families.GetBarType(add.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua đai phụ D{add.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }
        if (mainBarCount < 3) return 0; // <3 thanh không cần đai phụ.

        // Vị trí ngang thanh chủ thứ k — KHỚP công thức của LongitudinalBarCreator.Vertical: thép chủ nằm
        // TRONG đai → usableHalf = W/2 - coverSide - đường kính đai - nửa đường kính THÉP CHỦ.
        var coverSide = _cover.SideMm / 304.8;
        var stirrupClearFeet = config.Diameter.Feet; // thép chủ lùi trong đai 1 đường kính đai.
        var mainBarFeet = (mainBarDiameterMm > 0 ? mainBarDiameterMm : config.Diameter.Millimeters) / 304.8;
        var usableHalf = Math.Max(0, frame.WidthFeet / 2 - coverSide - stirrupClearFeet - mainBarFeet / 2);
        double LateralOfBar(int k) => mainBarCount <= 1 ? 0 : -usableHalf + k * (2 * usableHalf / (mainBarCount - 1));

        var isClosed = add.Type == AdditionalStirrupType.Closed;

        // Lồng kín: ôm dải [Start..End]. Móc C: chỉ 1 VỊ TRÍ THANH (StartBar) — móc quặp 1 cột thanh.
        var startIdx = Math.Clamp(add.StartBar - 1, 0, mainBarCount - 1);
        var endIdx = isClosed
            ? Math.Clamp(add.EndBar - 1, startIdx, mainBarCount - 1)
            : startIdx; // móc C: 1 thanh.

        var latLeft = LateralOfBar(startIdx);
        var latRight = LateralOfBar(endIdx);
        if (latRight - latLeft < 1e-6) latRight = latLeft + mainBarFeet;

        // ĐAI LỒNG KÍN: cạnh đai phải BAO NGOÀI thanh chủ, không đè lên tâm. Mở rộng mỗi cạnh ra ngoài
        // = nửa đường kính thép chủ + nửa đường kính đai phụ → thép chủ nằm gọn trong đai.
        if (isClosed)
        {
            var grow = mainBarFeet / 2 + add.Diameter.Feet / 2;
            latLeft -= grow;
            latRight += grow;
        }

        // Móc: đai LỒNG KÍN dùng 135° (góc khép kín như đai chính); đai MÓC C dùng 180° (2 đầu quặp ôm thanh).
        var addHook = _families.GetHookType(isClosed ? HookAngle.Deg135 : HookAngle.Deg180);

        // Closed = đai kín (StirrupTie). Móc C = cây thẳng đứng + móc 180° (Standard).
        var style = isClosed ? RebarStyle.StirrupTie : RebarStyle.Standard;

        var created = 0;
        {
            var profileFactory = (double t) => isClosed
                ? StirrupProfileNarrow(frame, add.Diameter, t, latLeft, latRight)
                : StirrupProfileCHook(frame, add.Diameter, t, latLeft, latRight);
            if (config.Mode == StirrupMode.Uniform)
                created += CreateNarrowZone(host, frame, barType, addHook, 0, length, config.SpacingEndMm, add.Diameter, profileFactory, warnings, blockedRanges, style);
            else
            {
                var endZoneFeet = config.EndZoneLengthMm > 0 ? config.EndZoneLengthMm / 304.8 : length / 4;
                endZoneFeet = Math.Min(endZoneFeet, length / 2);
                created += CreateNarrowZone(host, frame, barType, addHook, 0, endZoneFeet, config.SpacingEndMm, add.Diameter, profileFactory, warnings, blockedRanges, style);
                created += CreateNarrowZone(host, frame, barType, addHook, endZoneFeet, length - endZoneFeet, config.SpacingMidMm, add.Diameter, profileFactory, warnings, blockedRanges, style);
                created += CreateNarrowZone(host, frame, barType, addHook, length - endZoneFeet, length, config.SpacingEndMm, add.Diameter, profileFactory, warnings, blockedRanges, style);
            }
        }
        return created;
    }

    private List<SecondaryStirrupRange> BuildSecondaryRanges(
        IReadOnlyList<SecondaryStirrupStation>? secondaryStationsFeet,
        double spanLengthFeet,
        double spacingFeet,
        RebarDiameter diameter,
        List<string> warnings)
    {
        if (secondaryStationsFeet is not { Count: > 0 }) return [];

        var ranges = new List<SecondaryStirrupRange>();
        foreach (var secondary in secondaryStationsFeet)
        {
            var s = secondary.StationFeet;
            if (s <= 0 || s >= spanLengthFeet) continue;

            var leftFace = s - Math.Max(0, secondary.HalfWidthFeet);
            var rightFace = s + Math.Max(0, secondary.HalfWidthFeet);
            var clearFeet = spacingFeet + diameter.Feet / 2.0;
            var leftEnd = leftFace - clearFeet;
            var leftStart = leftEnd - 3 * spacingFeet;
            var rightStart = rightFace + clearFeet;
            var rightEnd = rightStart + 3 * spacingFeet;
            if (leftStart <= 0 || rightEnd >= spanLengthFeet) continue;

            ranges.Add(new SecondaryStirrupRange(s, secondary.HalfWidthFeet, leftFace, rightFace,
                leftStart, leftEnd, rightStart, rightEnd));
        }

        return ranges
            .OrderBy(r => r.LeftStartFeet)
            .ToList();
    }

    private int CreateTwoEnds(Element host, SpanFrame frame, RebarBarType barType, RebarHookType? hook,
        StirrupConfig config, double length, List<string> warnings, IReadOnlyList<SecondaryStirrupRange> blockedRanges)
    {

        var fallbackEndZoneFeet = config.EndZoneLengthMm > 0
            ? config.EndZoneLengthMm / 304.8
            : length / 4;
        var endZoneStartFeet = config.EndZoneStartMm > 0
            ? config.EndZoneStartMm / 304.8
            : fallbackEndZoneFeet;
        var endZoneEndFeet = config.EndZoneEndMm > 0
            ? config.EndZoneEndMm / 304.8
            : fallbackEndZoneFeet;

        endZoneStartFeet = Math.Min(endZoneStartFeet, length / 2);
        endZoneEndFeet = Math.Min(endZoneEndFeet, length / 2);
        if (endZoneStartFeet + endZoneEndFeet > length)
        {
            var scale = length / (endZoneStartFeet + endZoneEndFeet);
            endZoneStartFeet *= scale;
            endZoneEndFeet *= scale;
        }

        // [CHAN DOAN tam]
        warnings.Add($"[DBG] stirrupFrame dai={Math.Round(length * 304.8)}mm, endZoneStart={Math.Round(endZoneStartFeet * 304.8)}mm, endZoneEnd={Math.Round(endZoneEndFeet * 304.8)}mm, so blockedRange={blockedRanges.Count}");
        foreach (var b in blockedRanges)
            warnings.Add($"[DBG] blocked: L[{Math.Round(b.LeftStartFeet * 304.8)}->{Math.Round(b.LeftEndFeet * 304.8)}] R[{Math.Round(b.RightStartFeet * 304.8)}->{Math.Round(b.RightEndFeet * 304.8)}]");

        var created = 0;
        created += CreateZone(host, frame, barType, hook, 0, endZoneStartFeet, config.SpacingEndMm, config.Diameter, warnings, blockedRanges);
        created += CreateZone(host, frame, barType, hook, endZoneStartFeet, length - endZoneEndFeet, config.SpacingMidMm, config.Diameter, warnings, blockedRanges);
        created += CreateZone(host, frame, barType, hook, length - endZoneEndFeet, length, config.SpacingEndMm, config.Diameter, warnings, blockedRanges);
        return created;
    }

    // Một vùng đai: đặt đai chuẩn tại fromFeet, rải số lượng theo bước tới toFeet.
    private int CreateZone(Element host, SpanFrame frame, RebarBarType barType, RebarHookType? hook,
        double fromFeet, double toFeet, double spacingMm, RebarDiameter diameter, List<string> warnings,
        IReadOnlyList<SecondaryStirrupRange> blockedRanges)
    {
        var created = 0;
        var cursor = fromFeet;
        foreach (var blocked in blockedRanges)
        {
            var blockStart = blocked.LeftStartFeet;
            var blockEnd = blocked.RightEndFeet;
            if (blockEnd <= cursor || blockStart >= toFeet) continue;

            created += CreateZoneUnblocked(host, frame, barType, hook, cursor, Math.Min(blockStart, toFeet),
                spacingMm, diameter, warnings);
            cursor = Math.Max(cursor, blockEnd);
            if (cursor >= toFeet) break;
        }

        created += CreateZoneUnblocked(host, frame, barType, hook, cursor, toFeet, spacingMm, diameter, warnings);
        return created;
    }

    private int CreateZoneUnblocked(Element host, SpanFrame frame, RebarBarType barType, RebarHookType? hook,
        double fromFeet, double toFeet, double spacingMm, RebarDiameter diameter, List<string> warnings)
    {
        var zoneLen = toFeet - fromFeet;
        if (zoneLen <= 1e-6) return 0;

        var spacingFeet = spacingMm / 304.8;

        var t = fromFeet / frame.LengthFeet;
        var profile = StirrupProfile(frame, diameter, t);

        // Thử tạo đai có móc neo 135°; nếu Revit từ chối (vài cấu hình không cho hook trên tie kín)
        // thì tạo lại đai kín không móc để vẫn có cốt đai.
        var rebar = TryCreate(host, barType, hook, frame.Along, profile, diameter, warnings)
                    ?? TryCreate(host, barType, null, frame.Along, profile, diameter, warnings);
        if (rebar is null) return 0;

        // MaximumSpacing: Revit chia đều PHỦ HẾT vùng [from,to] với bước ≤ spacing, đai đầu tại `from` và
        // đai cuối tại `to`. (NumberWithSpacing dùng bước CỐ ĐỊNH → cụm đai ngắn hơn vùng, dồn về `from`,
        // hở ở `to` = mép gối → đai cách mép gối dư.)
        if (rebar.IsRebarShapeDriven())
            rebar.GetShapeDrivenAccessor()
                .SetLayoutAsMaximumSpacing(spacingFeet, zoneLen, true, true, true);

        return 1;
    }

    private int CreateMaxSpacingZone(Element host, SpanFrame frame, RebarBarType barType, RebarHookType? hook,
        double firstBarFeet, double spacingFeet, double zoneLengthFeet, bool barsOnNormalSide,
        string label, RebarDiameter diameter, List<string> warnings)
    {
        var lastBarFeet = barsOnNormalSide
            ? firstBarFeet + zoneLengthFeet
            : firstBarFeet - zoneLengthFeet;
        if (Math.Min(firstBarFeet, lastBarFeet) <= 0 || Math.Max(firstBarFeet, lastBarFeet) >= frame.LengthFeet) return 0;

        var t = firstBarFeet / frame.LengthFeet;
        var profile = StirrupProfile(frame, diameter, t);
        var rebar = TryCreate(host, barType, hook, frame.Along, profile, diameter, warnings)
                    ?? TryCreate(host, barType, null, frame.Along, profile, diameter, warnings);
        if (rebar is null) return 0;

        if (rebar.IsRebarShapeDriven())
        {
            rebar.GetShapeDrivenAccessor()
                .SetLayoutAsMaximumSpacing(spacingFeet, zoneLengthFeet, barsOnNormalSide, true, true);
        }

        return 1;
    }

    private Autodesk.Revit.DB.Structure.Rebar? TryCreate(Element host, RebarBarType barType,
        RebarHookType? hook, XYZ normal, IList<Curve> profile, RebarDiameter diameter, List<string> warnings,
        RebarStyle style = RebarStyle.StirrupTie)
    {
        try
        {
            // Cả hai móc cùng quặp về phía trong lòng đai (Left/Left) thay vì chìa ra ngoài tiết diện.
            return RebarCompat.CreateFromCurves(
                _document, style, barType, hook, hook, host,
                normal, profile,
                right: false, useExistingShapeIfPossible: true);
        }
        catch (Exception ex)
        {
            if (hook is null)
                warnings.Add($"Lỗi tạo vùng đai D{diameter.Millimeters}: {ex.Message}");
            return null;
        }
    }

    // Khung đai chữ nhật kín trong tiết diện (trừ cover) tại vị trí dọc t, mặt phẳng vuông góc trục dầm.
    private IList<Curve> StirrupProfile(SpanFrame frame, RebarDiameter diameter, double t)
    {
        var coverSide = _cover.SideMm / 304.8;
        var coverTop = _cover.TopMm / 304.8;
        var coverBottom = _cover.BottomMm / 304.8;
        var bar = diameter.Feet;

        var halfWidth = frame.WidthFeet / 2 - coverSide - bar / 2;
        var top = -(coverTop + bar / 2);
        var bottom = -(frame.HeightFeet - coverBottom - bar / 2);

        var center = frame.AxisTop(t);
        XYZ Corner(double lateral, double vertical) => center + frame.Across * lateral + frame.Up * vertical;

        var tl = Corner(-halfWidth, top);
        var tr = Corner(halfWidth, top);
        var br = Corner(halfWidth, bottom);
        var bl = Corner(-halfWidth, bottom);

        return
        [
            Line.CreateBound(tl, tr),
            Line.CreateBound(tr, br),
            Line.CreateBound(br, bl),
            Line.CreateBound(bl, tl)
        ];
    }

    /// <summary>Đai phụ chữ nhật HẸP: cạnh trái/phải tại latLeft/latRight (vị trí thanh chủ giữa), cao bằng
    ///     đai chính. Móc 135° ôm thanh.</summary>
    private IList<Curve> StirrupProfileNarrow(SpanFrame frame, RebarDiameter diameter, double t,
        double latLeft, double latRight)
    {
        var coverTop = _cover.TopMm / 304.8;
        var coverBottom = _cover.BottomMm / 304.8;
        var bar = diameter.Feet;

        var top = -(coverTop + bar / 2);
        var bottom = -(frame.HeightFeet - coverBottom - bar / 2);

        var center = frame.AxisTop(t);
        XYZ Corner(double lateral, double vertical) => center + frame.Across * lateral + frame.Up * vertical;

        var tl = Corner(latLeft, top);
        var tr = Corner(latRight, top);
        var br = Corner(latRight, bottom);
        var bl = Corner(latLeft, bottom);

        return
        [
            Line.CreateBound(tl, tr),
            Line.CreateBound(tr, br),
            Line.CreateBound(br, bl),
            Line.CreateBound(bl, tl)
        ];
    }

    /// <summary>Thép phụ C = 1 CÂY THẲNG ĐỨNG (top→bottom) tại vị trí 1 cột thanh, mỗi đầu 1 MÓC 180° quặp
    ///     ôm thanh chủ. (Không phải đai chữ C kín — tránh lỗi tạo đai hở.)</summary>
    private IList<Curve> StirrupProfileCHook(SpanFrame frame, RebarDiameter diameter, double t,
        double latLeft, double latRight)
    {
        var coverTop = _cover.TopMm / 304.8;
        var coverBottom = _cover.BottomMm / 304.8;
        var bar = diameter.Feet;

        var top = -(coverTop + bar / 2);
        var bottom = -(frame.HeightFeet - coverBottom - bar / 2);

        // Vị trí ngang: 1 cột thanh (latLeft ≈ latRight cho móc C). Lấy trung bình cho chắc.
        var lat = (latLeft + latRight) / 2;
        var center = frame.AxisTop(t);
        var pTop = center + frame.Across * lat + frame.Up * top;
        var pBot = center + frame.Across * lat + frame.Up * bottom;

        // 1 đường thẳng đứng; móc 180° 2 đầu áp khi CreateFromCurves (hook 180°).
        return [Line.CreateBound(pTop, pBot)];
    }

    /// <summary>Tạo 1 vùng đai phụ [from,to] với profile hẹp (profileFactory), rải MaximumSpacing phủ hết,
    ///     bỏ qua phần chồng blockedRanges (vùng dầm phụ).</summary>
    private int CreateNarrowZone(Element host, SpanFrame frame, RebarBarType barType, RebarHookType? hook,
        double fromFeet, double toFeet, double spacingMm, RebarDiameter diameter,
        Func<double, IList<Curve>> profileFactory, List<string> warnings,
        IReadOnlyList<SecondaryStirrupRange> blockedRanges, RebarStyle style = RebarStyle.StirrupTie)
    {
        var created = 0;
        var cursor = fromFeet;
        foreach (var blocked in blockedRanges.OrderBy(b => b.LeftStartFeet))
        {
            if (blocked.RightEndFeet <= cursor || blocked.LeftStartFeet >= toFeet) continue;
            created += CreateNarrowSegment(host, frame, barType, hook, cursor, Math.Min(blocked.LeftStartFeet, toFeet), spacingMm, diameter, profileFactory, warnings, style);
            cursor = Math.Max(cursor, blocked.RightEndFeet);
            if (cursor >= toFeet) break;
        }
        created += CreateNarrowSegment(host, frame, barType, hook, cursor, toFeet, spacingMm, diameter, profileFactory, warnings, style);
        return created;
    }

    private int CreateNarrowSegment(Element host, SpanFrame frame, RebarBarType barType, RebarHookType? hook,
        double fromFeet, double toFeet, double spacingMm, RebarDiameter diameter,
        Func<double, IList<Curve>> profileFactory, List<string> warnings, RebarStyle style = RebarStyle.StirrupTie)
    {
        var zoneLen = toFeet - fromFeet;
        if (zoneLen <= 1e-6) return 0;
        var spacingFeet = spacingMm / 304.8;
        var profile = profileFactory(fromFeet / frame.LengthFeet);

        var rebar = TryCreate(host, barType, hook, frame.Along, profile, diameter, warnings, style)
                    ?? TryCreate(host, barType, null, frame.Along, profile, diameter, warnings, style);
        if (rebar is null) return 0;
        if (rebar.IsRebarShapeDriven())
            rebar.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(spacingFeet, zoneLen, true, true, true);
        return 1;
    }
}
