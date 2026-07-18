using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services.Rebar;

/// <summary>
///     Tạo thép dọc (thép chủ) cho một nhịp hoặc chạy suốt nhiều nhịp: các thanh chạy dọc trục dầm,
///     phân bố đều theo phương ngang tiết diện, đặt ở mặt trên/dưới sau khi trừ lớp bảo vệ. Hỗ trợ neo
///     vào gối (kéo dài curve) và uốn móc hai đầu qua RebarHookType.
/// </summary>
public sealed class LongitudinalBarCreator
{
    private const double SupportToleranceFeet = 60.0 / 304.8;

    private readonly Document _document;
    private readonly RebarFamilyValidator _families;
    private readonly CoverSettings _cover;
    private readonly double _stirrupClearFeet;

    /// <summary><paramref name="stirrupDiameterMm"/> = đường kính cốt đai. Thép chủ lùi thêm 1 đường kính
    ///     đai để nằm BÊN TRONG đai (không đè lên đường đai).</summary>
    public LongitudinalBarCreator(Document document, RebarFamilyValidator families, CoverSettings cover,
        double stirrupDiameterMm = 0)
    {
        _document = document;
        _families = families;
        _cover = cover;
        _stirrupClearFeet = stirrupDiameterMm / 304.8;
    }

    /// <summary>
    ///     Tạo một lớp thép dọc trên một nhịp. <paramref name="atTop"/> true = mặt trên, false = mặt dưới.
    ///     Neo: kéo dài <c>anchorFeet</c> mỗi đầu nhưng clamp trong host. Hook: áp dụng nếu config Enabled.
    /// </summary>
    public int Create(Element host, SpanFrame frame, MainBarConfig config,
        bool atTop, double extraVerticalOffsetFeet, List<string> warnings)
    {
        if (!config.Enabled) return 0;

        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua thép chủ D{config.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        var hookStart = config.HookStart.Enabled ? _families.GetHookType(config.HookStart.Angle) : null;
        var hookEnd = config.HookEnd.Enabled ? _families.GetHookType(config.HookEnd.Angle) : null;
        var anchorFeet = config.AnchorLengthMm / 304.8;
        var leftBendDownFeet = atTop
            ? Math.Max(0, (config.AnchorLeftMm > 0 ? config.AnchorLeftMm : config.TopEndBendDownLengthMm) / 304.8)
            : Math.Max(0, config.AnchorLeftMm / 304.8);
        var rightBendDownFeet = atTop
            ? Math.Max(0, (config.AnchorRightMm > 0 ? config.AnchorRightMm : config.TopEndBendDownLengthMm) / 304.8)
            : Math.Max(0, config.AnchorRightMm / 304.8);
        var fallbackInset = Math.Min(anchorFeet, _cover.SideMm / 304.8);
        var leftInsetFeet = config.AnchorXLeftMm > 0 ? config.AnchorXLeftMm / 304.8 : fallbackInset;
        var rightInsetFeet = config.AnchorXRightMm > 0 ? config.AnchorXRightMm / 304.8 : fallbackInset;

        return CreateBars(host, frame, barType, config.Diameter, config.Count, atTop,
            extraVerticalOffsetFeet, startT: 0, endT: 1, leftInsetFeet, rightInsetFeet,
            leftBendDownFeet, rightBendDownFeet, config.PositionInSection,
            hookStart, hookEnd, warnings);
    }

    public int CreateRange(Element host, SpanFrame frame, MainBarConfig config,
        bool atTop, double extraVerticalOffsetFeet, double startT, double endT, List<string> warnings)
    {
        if (!config.Enabled) return 0;

        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua thép chủ D{config.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        var hookStart = config.HookStart.Enabled ? _families.GetHookType(config.HookStart.Angle) : null;
        var hookEnd = config.HookEnd.Enabled ? _families.GetHookType(config.HookEnd.Angle) : null;
        var leftInsetFeet = Math.Max(0, config.AnchorXLeftMm / 304.8);
        var rightInsetFeet = Math.Max(0, config.AnchorXRightMm / 304.8);
        if (leftInsetFeet <= 1e-6 && startT <= 0) leftInsetFeet = _cover.SideMm / 304.8;
        if (rightInsetFeet <= 1e-6 && endT >= 1) rightInsetFeet = _cover.SideMm / 304.8;
        var leftBendDownFeet = atTop
            ? Math.Max(0, (config.AnchorLeftMm > 0 ? config.AnchorLeftMm : config.TopEndBendDownLengthMm) / 304.8)
            : Math.Max(0, config.AnchorLeftMm / 304.8);
        var rightBendDownFeet = atTop
            ? Math.Max(0, (config.AnchorRightMm > 0 ? config.AnchorRightMm : config.TopEndBendDownLengthMm) / 304.8)
            : Math.Max(0, config.AnchorRightMm / 304.8);

        return CreateBars(host, frame, barType, config.Diameter, config.Count, atTop,
            extraVerticalOffsetFeet, startT, endT, leftInsetFeet, rightInsetFeet,
            leftBendDownFeet, rightBendDownFeet, config.PositionInSection,
            hookStart, hookEnd, warnings);
    }

    /// <summary>Overload cho thép gia cường cắt ngắn (không hook/anchor, dùng tham số chiều dài tường minh).</summary>
    public int CreateSegment(Element host, SpanFrame frame, RebarDiameter diameter, int count,
        bool atTop, double extraVerticalOffsetFeet, double startT, double endT,
        string positionInSection, int mainCount, List<string> warnings,
        bool forceFixedNumberAcrossWidth = false)
    {
        var barType = _families.GetBarType(diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua thép gia cường D{diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        (startT, endT) = ClampSegmentInsideHost(frame, startT, endT);
        if (endT <= startT)
        {
            warnings.Add($"Bỏ qua thép gia cường D{diameter.Millimeters}: chiều dài đoạn quá ngắn sau khi trừ cover.");
            return 0;
        }

        var (vertical, usableHalf) = Vertical(frame, diameter, atTop, extraVerticalOffsetFeet);

        // Thép gia cường mặc định đặt XEN KẼ theo khe giữa các thanh chủ.
        // Ví dụ main 3 cây, add 2 cây, Position "0,1" => 2 khe giữa 3 cây chủ.
        var gapOffsets = forceFixedNumberAcrossWidth
            ? []
            : GetGapOffsets(positionInSection, mainCount, count, usableHalf);
        if (gapOffsets.Count > 0)
        {
            if (count >= 2)
            {
                var ordered = gapOffsets.OrderBy(x => x).ToList();
                var p0 = PointAt(frame, startT, ordered[0], vertical);
                var p1 = PointAt(frame, endT, ordered[0], vertical);
                var layoutDistance = ordered[^1] - ordered[0];
                return TryCreateStraightSet(host, barType, frame, p0, p1, null, null, diameter, count, usableHalf, warnings,
                    layoutDistance)
                    ? count
                    : 0;
            }

            var created = 0;
            foreach (var lateral in gapOffsets)
            {
                var p0 = PointAt(frame, startT, lateral, vertical);
                var p1 = PointAt(frame, endT, lateral, vertical);
                if (TryCreateStraightSet(host, barType, frame, p0, p1, null, null, diameter, 1, 0, warnings))
                    created++;
            }
            return created;
        }

        var lateralSet = FirstLateral(usableHalf, count);
        var setP0 = PointAt(frame, startT, lateralSet, vertical);
        var setP1 = PointAt(frame, endT, lateralSet, vertical);
        return TryCreateStraightSet(host, barType, frame, setP0, setP1, null, null, diameter, count, usableHalf, warnings)
            ? count
            : 0;
    }

    public int CreateSegmentWithEndBends(Element host, SpanFrame frame, RebarDiameter diameter, int count,
        bool atTop, double extraVerticalOffsetFeet, double startT, double endT,
        bool bendStartDown, bool bendEndDown, double startDownFeet, double endDownFeet,
        string positionInSection, int mainCount, List<string> warnings,
        bool forceFixedNumberAcrossWidth = false)
    {
        var barType = _families.GetBarType(diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua thép gia cường D{diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        (startT, endT) = ClampSegmentInsideHost(frame, startT, endT);
        if (endT <= startT)
        {
            warnings.Add($"Bỏ qua thép gia cường D{diameter.Millimeters}: chiều dài đoạn quá ngắn sau khi trừ cover.");
            return 0;
        }

        var (vertical, usableHalf) = Vertical(frame, diameter, atTop, extraVerticalOffsetFeet);

        // Theo khe giữa thanh chủ để thép gia cường xen kẽ với thép chủ (xem CreateSegment).
        var gapOffsets = forceFixedNumberAcrossWidth
            ? []
            : GetGapOffsets(positionInSection, mainCount, count, usableHalf);
        if (gapOffsets.Count > 0)
        {
            if (count >= 2)
            {
                var ordered = gapOffsets.OrderBy(x => x).ToList();
                var p0 = PointAt(frame, startT, ordered[0], vertical);
                var p1 = PointAt(frame, endT, ordered[0], vertical);
                var layoutDistance = ordered[^1] - ordered[0];
                return TryCreateSegmentWithEndBendsSet(host, barType, frame, p0, p1,
                        bendStartDown, bendEndDown, startDownFeet, endDownFeet, diameter, count, usableHalf, warnings,
                        layoutDistance)
                    ? count
                    : 0;
            }

            var created = 0;
            foreach (var lateral in gapOffsets)
            {
                var p0 = PointAt(frame, startT, lateral, vertical);
                var p1 = PointAt(frame, endT, lateral, vertical);
                if (TryCreateSegmentWithEndBendsSet(host, barType, frame, p0, p1,
                        bendStartDown, bendEndDown, startDownFeet, endDownFeet, diameter, 1, 0, warnings))
                    created++;
            }
            return created;
        }

        var lateralSet = FirstLateral(usableHalf, count);
        var setP0 = PointAt(frame, startT, lateralSet, vertical);
        var setP1 = PointAt(frame, endT, lateralSet, vertical);
        return TryCreateSegmentWithEndBendsSet(host, barType, frame, setP0, setP1,
                bendStartDown, bendEndDown, startDownFeet, endDownFeet, diameter, count, usableHalf, warnings)
            ? count
            : 0;
    }

    /// <summary>
    ///     Vị trí ngang của thép gia cường đặt theo KHE giữa thanh chủ. positionInSection = danh sách chỉ số
    ///     khe (0 = khe đầu giữa thanh chủ 1-2, ...). Nếu không đủ khe thì fallback về chia đều trong lòng,
    ///     tránh hai biên là vị trí thường có thép chủ. mainCount = số thanh chủ → mainCount-1 khe.
    /// </summary>
    private static List<double> GetGapOffsets(string positionInSection, int mainCount, int addCount, double usableHalf)
    {
        var offsets = new List<double>();
        if (addCount <= 0) return offsets;

        if (mainCount <= 2)
            return EvenInteriorOffsets(addCount, usableHalf);

        var gaps = mainCount - 1; // số khe giữa các thanh chủ
        var parts = string.IsNullOrWhiteSpace(positionInSection)
            ? Enumerable.Range(0, addCount).Select(i => i.ToString()).ToArray()
            : positionInSection.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (!int.TryParse(part.Trim(), out var gapIdx)) continue;
            if (gapIdx >= gaps && addCount > gaps)
                return EvenInteriorOffsets(addCount, usableHalf);

            gapIdx = Math.Clamp(gapIdx, 0, gaps - 1);
            // Tâm khe gapIdx: giữa thanh chủ gapIdx và gapIdx+1. Thanh chủ i tại -usableHalf + i*step.
            var step = mainCount <= 1 ? 0 : usableHalf * 2 / (mainCount - 1);
            var lateral = mainCount <= 1 ? 0 : -usableHalf + (gapIdx + 0.5) * step;
            if (!offsets.Any(o => Math.Abs(o - lateral) < 1e-9))
                offsets.Add(lateral);
        }

        return offsets.Count == addCount ? offsets : EvenInteriorOffsets(addCount, usableHalf);
    }

    private static List<double> EvenInteriorOffsets(int count, double usableHalf)
    {
        var offsets = new List<double>();
        if (count <= 0) return offsets;

        var step = usableHalf * 2 / (count + 1);
        for (var i = 0; i < count; i++)
            offsets.Add(-usableHalf + (i + 1) * step);
        return offsets;
    }

    private List<double> GetLateralOffsets(string positionInSection, int mainCount, double usableHalf)
    {
        var offsets = new List<double>();
        if (string.IsNullOrWhiteSpace(positionInSection))
            return offsets;

        var parts = positionInSection.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), out var idx))
            {
                var denom = Math.Max(1, mainCount - 1);
                var lateral = mainCount <= 1 ? 0 : -usableHalf + idx * (usableHalf * 2) / denom;
                offsets.Add(lateral);
            }
        }
        return offsets;
    }

    private int CreateBars(Element host, SpanFrame frame, RebarBarType barType, RebarDiameter diameter,
        int count, bool atTop, double extraVerticalOffsetFeet, double startT, double endT,
        double leftInsetFeet, double rightInsetFeet,
        double leftBendDownFeet, double rightBendDownFeet, string positionInSection,
        RebarHookType? hookStart, RebarHookType? hookEnd, List<string> warnings)
    {
        if (count <= 0) return 0;

        var (vertical, usableHalf) = Vertical(frame, diameter, atTop, extraVerticalOffsetFeet);

        startT = Math.Clamp(startT, 0, 1);
        endT = Math.Clamp(endT, 0, 1);
        var segmentLen = (endT - startT) * frame.LengthFeet;
        if (segmentLen <= SupportToleranceFeet)
        {
            warnings.Add($"Bỏ qua thép chủ D{diameter.Millimeters}: Start/End Point quá gần nhau.");
            return 0;
        }

        var maxEachInset = Math.Max(0, segmentLen / 2 - _cover.SideMm / 304.8);
        leftInsetFeet = Math.Min(Math.Max(0, leftInsetFeet), maxEachInset);
        rightInsetFeet = Math.Min(Math.Max(0, rightInsetFeet), maxEachInset);

        // Thép chủ THẲNG (không bẻ xuống) → tạo 1 rebar tại mép trái rồi FIXED NUMBER rải đều về phía
        // trong dầm. KHÔNG dùng PositionInSection (gây lòi ngoài). Chiều rải verify bằng vị trí bar thật.
        if (leftBendDownFeet <= 0 && rightBendDownFeet <= 0)
        {
            var lat = FirstLateral(usableHalf, count); // -usableHalf (mép trái)
            var p0 = frame.AxisTop(startT) + frame.Across * lat + frame.Up * vertical + frame.Along * leftInsetFeet;
            var p1 = frame.AxisTop(endT) + frame.Across * lat + frame.Up * vertical - frame.Along * rightInsetFeet;
            return TryCreateStraightSet(host, barType, frame, p0, p1, hookStart, hookEnd, diameter, count, usableHalf, warnings)
                ? count : 0;
        }

        // Thép chủ có BẺ XUỐNG đầu (polyline): tạo 1 rebar set tại mép trái rồi FIXED NUMBER rải đều
        // (giống nhánh thẳng) — để thành 1 set Quantity=count, không phải nhiều thanh single.
        var latBent = FirstLateral(usableHalf, count);
        var bp0 = frame.AxisTop(startT) + frame.Across * latBent + frame.Up * vertical + frame.Along * leftInsetFeet;
        var bp1 = frame.AxisTop(endT) + frame.Across * latBent + frame.Up * vertical - frame.Along * rightInsetFeet;
        return TryCreateMainBentEndSet(host, barType, frame, bp0, bp1, atTop, leftBendDownFeet, rightBendDownFeet, diameter, count, usableHalf, warnings)
            ? count : 0;
    }

    /// <summary>Các vị trí ngang phân bố ĐỀU của count cây trong [-usableHalf, +usableHalf].</summary>
    private static IEnumerable<double> EvenLaterals(double usableHalf, int count)
    {
        if (count <= 1) { yield return 0; yield break; }
        var step = usableHalf * 2 / (count - 1);
        for (var i = 0; i < count; i++)
            yield return -usableHalf + i * step;
    }

    private bool TryCreateMainBentEndSet(Element host, RebarBarType barType, SpanFrame frame, XYZ p0Top, XYZ p1Top,
        bool atTop, double requestedLeftDownFeet, double requestedRightDownFeet, RebarDiameter diameter, int count, double usableHalf, List<string> warnings)
    {
        try
        {
            var coverTop = _cover.TopMm / 304.8;
            var coverBottom = _cover.BottomMm / 304.8;
            var maxDownFeet = Math.Max(0, frame.HeightFeet - coverTop - coverBottom - diameter.Feet);
            var leftDownFeet = Math.Min(requestedLeftDownFeet, maxDownFeet);
            var rightDownFeet = Math.Min(requestedRightDownFeet, maxDownFeet);
            if (leftDownFeet <= 1e-6 && rightDownFeet <= 1e-6)
                return TryCreateStraightSet(host, barType, frame, p0Top, p1Top, null, null, diameter, count, usableHalf, warnings);

            var curves = new List<Curve>();
            var bendDirection = atTop ? -frame.Up : frame.Up;
            if (leftDownFeet > 1e-6)
            {
                var p0Down = p0Top + bendDirection * leftDownFeet;
                curves.Add(Line.CreateBound(p0Down, p0Top));
            }

            curves.Add(Line.CreateBound(p0Top, p1Top));

            if (rightDownFeet > 1e-6)
            {
                var p1Down = p1Top + bendDirection * rightDownFeet;
                curves.Add(Line.CreateBound(p1Top, p1Down));
            }

            var rebar = RebarCompat.CreateFromCurves(
                _document, RebarStyle.Standard, barType, null, null, host,
                frame.Across, curves,
                right: true, useExistingShapeIfPossible: true);
            SetFixedNumberLayout(rebar, count, usableHalf, frame, warnings);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"Lỗi tạo 1 thanh thép chủ trên bẻ xuống D{diameter.Millimeters}: {ex.Message}");
            return false;
        }
    }

    private bool TryCreateSegmentWithEndBendsSet(Element host, RebarBarType barType, SpanFrame frame, XYZ p0Top, XYZ p1Top,
        bool bendStartDown, bool bendEndDown, double startDownFeet, double endDownFeet, RebarDiameter diameter,
        int count, double usableHalf, List<string> warnings, double? layoutDistanceFeet = null)
    {
        try
        {
            var coverTop = _cover.TopMm / 304.8;
            var coverBottom = _cover.BottomMm / 304.8;
            var maxDownFeet = Math.Max(0, frame.HeightFeet - coverTop - coverBottom - diameter.Feet);
            var startDown = Math.Min(Math.Max(0, startDownFeet), maxDownFeet);
            var endDown = Math.Min(Math.Max(0, endDownFeet), maxDownFeet);

            if ((startDown <= 1e-6 && endDown <= 1e-6) || (!bendStartDown && !bendEndDown))
                return TryCreateStraightSet(host, barType, frame, p0Top, p1Top, null, null, diameter, count, usableHalf, warnings);

            var curves = new List<Curve>();
            if (bendStartDown && startDown > 1e-6)
            {
                var p0Down = p0Top - frame.Up * startDown;
                curves.Add(Line.CreateBound(p0Down, p0Top));
            }
            curves.Add(Line.CreateBound(p0Top, p1Top));
            if (bendEndDown && endDown > 1e-6)
            {
                var p1Down = p1Top - frame.Up * endDown;
                curves.Add(Line.CreateBound(p1Top, p1Down));
            }

            var rebar = RebarCompat.CreateFromCurves(
                _document, RebarStyle.Standard, barType, null, null, host,
                frame.Across, curves,
                right: true, useExistingShapeIfPossible: true);
            SetFixedNumberLayout(rebar, count, usableHalf, frame, warnings, layoutDistanceFeet);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"Lỗi tạo 1 thanh thép gia cường bẻ móc D{diameter.Millimeters}: {ex.Message}");
            return false;
        }
    }

    private bool TryCreateStraightSet(Element host, RebarBarType barType, SpanFrame frame, XYZ p0, XYZ p1,
        RebarHookType? hookStart, RebarHookType? hookEnd, RebarDiameter diameter,
        int count, double usableHalf, List<string> warnings, double? layoutDistanceFeet = null)
    {
        try
        {
            IList<Curve> curves = [Line.CreateBound(p0, p1)];
            var rebar = RebarCompat.CreateFromCurves(
                _document, RebarStyle.Standard, barType, hookStart, hookEnd, host,
                frame.Across, curves,
                right: true, useExistingShapeIfPossible: true);
            SetFixedNumberLayout(rebar, count, usableHalf, frame, warnings, layoutDistanceFeet);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"Lỗi tạo 1 thanh thép dọc D{diameter.Millimeters}: {ex.Message}");
            return false;
        }
    }

    private (double Vertical, double UsableHalf) Vertical(SpanFrame frame, RebarDiameter diameter,
        bool atTop, double extraVerticalOffsetFeet)
    {
        var barFeet = diameter.Feet;
        // Thép chủ nằm TRONG đai → lùi thêm 1 đường kính đai so với cover bê tông (không đè lên đường đai).
        var coverSide = _cover.SideMm / 304.8 + _stirrupClearFeet;
        var coverTop = _cover.TopMm / 304.8 + _stirrupClearFeet;
        var coverBottom = _cover.BottomMm / 304.8 + _stirrupClearFeet;

        // Tâm thanh cách mặt cover + nửa đường kính (+ offset lớp 2). atTop dùng hệ tọa độ từ mặt trên (Z giảm xuống).
        var vertical = atTop
            ? -(coverTop + barFeet / 2 + extraVerticalOffsetFeet)
            : -(frame.HeightFeet - coverBottom - barFeet / 2 - extraVerticalOffsetFeet);

        var usableHalf = Math.Max(0, frame.WidthFeet / 2 - coverSide - barFeet / 2);
        return (vertical, usableHalf);
    }

    private static double FirstLateral(double usableHalf, int count)
        => count == 1 ? 0 : -usableHalf;

    private static bool IsDefaultPositionSequence(string positionInSection, int count)
    {
        if (string.IsNullOrWhiteSpace(positionInSection) || count <= 0)
            return false;

        var parts = positionInSection.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p.Trim(), out var idx) ? idx : -1)
            .ToList();
        if (parts.Count != count)
            return false;

        for (var i = 0; i < count; i++)
        {
            if (parts[i] != i)
                return false;
        }

        return true;
    }

    private static void SetFixedNumberLayout(Autodesk.Revit.DB.Structure.Rebar rebar, int count, double usableHalf, SpanFrame frame,
        List<string> warnings, double? layoutDistanceFeet = null)
    {
        if (count <= 1 || usableHalf <= 1e-6) return;

        if (!rebar.IsRebarShapeDriven()) return;

        var accessor = rebar.GetShapeDrivenAccessor();
        var layoutDistance = layoutDistanceFeet is > 1e-6 ? layoutDistanceFeet.Value : usableHalf * 2;
        try
        {
            // Thanh đầu tiên luôn được dựng tại mép -Across (FirstLateral = -usableHalf), còn normal khi
            // CreateFromCurves là frame.Across. Vì vậy rải về normal side sẽ đi vào trong tiết diện rồi
            // tới mép +Across. Không regenerate để "đo lại" từng set: Revit 2025 chạy các Rebar Updater
            // ở mỗi lần regenerate; khi tạo hàng loạt, các updater có thể cùng sửa một rebar và làm Revit
            // treo/crash. Transaction commit sẽ regenerate đúng một lần sau khi toàn bộ set đã hoàn tất.
            accessor.SetLayoutAsFixedNumber(count, layoutDistance, barsOnNormalSide: true,
                includeFirstBar: true, includeLastBar: true);
        }
        catch
        {
            // Một số shape không cho fixed-number; giữ nguyên (Single) còn hơn fail cả thanh.
        }
    }

    private static XYZ PointAt(SpanFrame frame, double t, double lateral, double vertical)
        => frame.AxisTop(t) + frame.Across * lateral + frame.Up * vertical;

    private (double StartT, double EndT) ClampSegmentInsideHost(SpanFrame frame, double startT, double endT)
    {
        var insetT = Math.Min(_cover.SideMm / 304.8 / frame.LengthFeet, 0.05);
        if (startT <= 0) startT = insetT;
        if (endT >= 1) endT = 1 - insetT;
        return (startT, endT);
    }
}
