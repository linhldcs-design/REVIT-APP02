using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services.Rebar;

/// <summary>
///     Tạo một lưới thép phẳng (đáy hoặc trên) cho móng: các thanh thẳng song song theo 2 phương X/Y,
///     rải đều theo bước (hoặc số lượng) dọc phương vuông góc. Mỗi phương là một <see cref="Rebar"/> set
///     (<see cref="RebarStyle.Standard"/>). Hỗ trợ móc bẻ 2 đầu (lên cho lưới đáy, xuống cho lưới trên)
///     dài <c>HookLengthMm</c>. Lớp Y xếp chồng trên lớp X một đường kính để không đè nhau.
/// </summary>
public sealed class MeshBarCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;

    public MeshBarCreator(Document document, RebarFamilyValidator families)
    {
        _document = document;
        _families = families;
    }

    /// <summary>Tạo lưới (X + Y) tại mặt đáy (<paramref name="atTop"/>=false) hoặc mặt trên (=true).</summary>
    public int Create(Element host, FootingFrame frame, LayerBarConfig dirX, LayerBarConfig dirY,
        bool atTop, CoverSettings cover, List<string> warnings)
    {
        var created = 0;

        // Thứ tự xếp lớp: thanh phương X sát mặt (đáy/trên), thanh phương Y chồng lên trên 1 đường kính
        // (lùi thêm đúng đường kính thanh X bên dưới).
        var dxFeet = dirX.Diameter.Feet;

        if (dirX.Enabled)
        {
            var zX = LayerZ(frame, cover, atTop, dirX.Diameter, extraOffsetFeet: 0);
            if (WithinConcrete(frame, cover, zX, dirX.Diameter, warnings, "X"))
                created += CreateOneDirection(host, frame, dirX, alongX: true, zX, atTop, cover, warnings);
        }

        if (dirY.Enabled)
        {
            var zY = LayerZ(frame, cover, atTop, dirY.Diameter, extraOffsetFeet: dxFeet);
            if (WithinConcrete(frame, cover, zY, dirY.Diameter, warnings, "Y"))
                created += CreateOneDirection(host, frame, dirY, alongX: false, zY, atTop, cover, warnings);
        }

        return created;
    }

    /// <summary>
    ///     Tạo lưới giữa (mid layer) — <paramref name="layers"/> lớp lưới X+Y rải đều theo chiều cao đế,
    ///     giữa lưới đáy và lưới trên. Mỗi lớp xếp X dưới, Y trên (1 đường kính). Lớp nằm ngoài bê tông
    ///     (móng mỏng) bị bỏ qua + cảnh báo.
    /// </summary>
    public int CreateMid(Element host, FootingFrame frame, LayerBarConfig dirX, LayerBarConfig dirY,
        int layers, CoverSettings cover, List<string> warnings)
    {
        if (layers <= 0) return 0;

        var lo = frame.BottomZFeet + cover.BottomMm / 304.8;
        var hi = frame.BaseTopZFeet - cover.TopMm / 304.8;
        if (hi - lo <= 1e-6)
        {
            warnings.Add("Bỏ qua lưới giữa: móng quá mỏng (không còn khoảng bê tông giữa lớp bảo vệ).");
            return 0;
        }

        var created = 0;
        var dxFeet = dirX.Diameter.Feet;

        // layers lớp chia đều khoảng [lo, hi]: lớp i tại lo + (i+1)/(layers+1) · (hi-lo).
        for (var i = 0; i < layers; i++)
        {
            var zBase = lo + (hi - lo) * (i + 1) / (layers + 1);

            if (dirX.Enabled)
            {
                var zX = zBase + dirX.Diameter.Feet / 2;
                if (WithinConcrete(frame, cover, zX, dirX.Diameter, warnings, $"giữa-X lớp {i + 1}"))
                    created += CreateOneDirection(host, frame, dirX, alongX: true, zX, atTop: false, cover, warnings);
            }

            if (dirY.Enabled)
            {
                var zY = zBase + dxFeet + dirY.Diameter.Feet / 2;
                if (WithinConcrete(frame, cover, zY, dirY.Diameter, warnings, $"giữa-Y lớp {i + 1}"))
                    created += CreateOneDirection(host, frame, dirY, alongX: false, zY, atTop: false, cover, warnings);
            }
        }

        return created;
    }

    /// <summary>Cao độ tâm thanh (Z feet): lùi từ mặt đáy/trên một lớp bảo vệ + nửa đường kính + offset chồng lớp.</summary>
    private static double LayerZ(FootingFrame frame, CoverSettings cover, bool atTop,
        RebarDiameter diameter, double extraOffsetFeet)
    {
        var halfBar = diameter.Feet / 2;
        return atTop
            ? frame.BaseTopZFeet - cover.TopMm / 304.8 - halfBar - extraOffsetFeet
            : frame.BottomZFeet + cover.BottomMm / 304.8 + halfBar + extraOffsetFeet;
    }

    /// <summary>Tâm thanh (tính cả nửa đường kính) phải nằm trong bê tông đế giữa 2 lớp bảo vệ. Móng quá
    ///     mỏng → lớp trên/dưới chồng nhau → bỏ qua + cảnh báo thay vì đặt thép ra ngoài bê tông.</summary>
    private static bool WithinConcrete(FootingFrame frame, CoverSettings cover, double zFeet,
        RebarDiameter diameter, List<string> warnings, string label)
    {
        var halfBar = diameter.Feet / 2;
        var lo = frame.BottomZFeet + cover.BottomMm / 304.8;
        var hi = frame.BaseTopZFeet - cover.TopMm / 304.8;
        if (zFeet - halfBar >= lo - 1e-6 && zFeet + halfBar <= hi + 1e-6) return true;

        warnings.Add($"Bỏ qua lưới phương {label} D{diameter.Millimeters}: móng quá mỏng, " +
                     "thanh nằm ngoài vùng bê tông giữa lớp bảo vệ trên/dưới.");
        return false;
    }

    /// <summary>
    ///     Tạo 1 set thanh theo một phương. <paramref name="alongX"/>=true → thanh chạy dọc DirX, rải dọc
    ///     DirY; ngược lại. Thanh dài = bề rộng móng phương đó trừ 2 lớp bảo vệ cạnh.
    /// </summary>
    private int CreateOneDirection(Element host, FootingFrame frame, LayerBarConfig config,
        bool alongX, double zFeet, bool atTop, CoverSettings cover, List<string> warnings)
    {
        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua lưới D{config.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        var sideCoverFeet = cover.SideMm / 304.8;
        var runWidthFeet = (alongX ? frame.WidthXFeet : frame.WidthYFeet) - 2 * sideCoverFeet;
        var layoutWidthFeet = (alongX ? frame.WidthYFeet : frame.WidthXFeet) - 2 * sideCoverFeet;
        if (runWidthFeet <= 1e-6 || layoutWidthFeet <= 1e-6)
        {
            warnings.Add($"Bỏ qua lưới D{config.Diameter.Millimeters}: móng quá nhỏ so với lớp bảo vệ.");
            return 0;
        }

        // u,v của 2 đầu thanh (chuẩn hóa). sideInsetU/V = lớp bảo vệ cạnh quy ra tỉ lệ.
        var insetRun = sideCoverFeet / (alongX ? frame.WidthXFeet : frame.WidthYFeet);
        var insetLayout = sideCoverFeet / (alongX ? frame.WidthYFeet : frame.WidthXFeet);

        // Thanh đầu tiên đặt ở mép layout (v hoặc u = insetLayout). Hướng rải (normal) = phương vuông góc.
        var startU = alongX ? insetRun : insetLayout;
        var endU = alongX ? 1 - insetRun : insetLayout;
        var startV = alongX ? insetLayout : insetRun;
        var endV = alongX ? insetLayout : 1 - insetRun;

        var p0 = frame.PointAt(startU, startV, zFeet);
        var p1 = frame.PointAt(endU, endV, zFeet);

        // phương rải các thanh = vuông góc trục thanh; cũng là pháp tuyến mặt phẳng chứa thanh + đoạn móc đứng.
        var normal = alongX ? frame.DirY : frame.DirX;

        // Móc bẻ theo PHƯƠNG ĐỨNG (Z): thanh ngang + 2 đoạn bẻ đứng nằm cùng mặt phẳng (trục thanh, Z) →
        // đồng phẳng nên Revit giải được shape. KHÔNG dùng RebarHookType (móc xoay trong mặt phẳng ngang
        // gây "Can't solve Rebar Shape" với thanh nằm ngang).
        var curves = BuildBarCurves(frame, p0, p1, config, atTop, cover, zFeet);

        Autodesk.Revit.DB.Structure.Rebar? rebar;
        try
        {
            rebar = RebarCompat.CreateFromCurves(
                _document, RebarStyle.Standard, barType, null, null, host,
                normal, curves,
                right: true, useExistingShapeIfPossible: false);
        }
        catch (Exception ex)
        {
            warnings.Add($"Lỗi tạo lưới D{config.Diameter.Millimeters}: {ex.Message}");
            return 0;
        }

        ApplyLayout(rebar, config, layoutWidthFeet, warnings);
        return 1;
    }

    /// <summary>Dựng curve 1 thanh lưới: đường thẳng p0→p1; nếu bật móc, thêm đoạn bẻ ĐỨNG (lên cho lưới
    ///     đáy, xuống cho lưới trên) ở 2 đầu. Chiều dài móc clamp để không vượt khỏi bê tông theo Z.</summary>
    private static IList<Curve> BuildBarCurves(FootingFrame frame, XYZ p0, XYZ p1,
        LayerBarConfig config, bool atTop, CoverSettings cover, double zFeet)
    {
        if (!config.HookEnabled || config.HookLengthMm <= 0)
            return [Line.CreateBound(p0, p1)];

        // Móc bẻ lên (đáy) hoặc xuống (trên); clamp trong khoảng bê tông còn lại theo Z.
        var available = atTop
            ? zFeet - (frame.BottomZFeet + cover.BottomMm / 304.8)
            : (frame.BaseTopZFeet - cover.TopMm / 304.8) - zFeet;
        var hookFeet = Math.Min(config.HookLengthMm / 304.8, Math.Max(0, available));
        if (hookFeet <= 1e-6)
            return [Line.CreateBound(p0, p1)];

        var bend = (atTop ? -1 : 1) * hookFeet;
        var p0Hook = new XYZ(p0.X, p0.Y, p0.Z + bend);
        var p1Hook = new XYZ(p1.X, p1.Y, p1.Z + bend);

        return
        [
            Line.CreateBound(p0Hook, p0),
            Line.CreateBound(p0, p1),
            Line.CreateBound(p1, p1Hook)
        ];
    }

    /// <summary>Rải set theo bước (MaximumSpacing phủ hết dải) hoặc số lượng cố định (FixedNumber).</summary>
    private static void ApplyLayout(Autodesk.Revit.DB.Structure.Rebar rebar, LayerBarConfig config,
        double layoutWidthFeet, List<string> warnings)
    {
        if (!rebar.IsRebarShapeDriven()) return;
        var accessor = rebar.GetShapeDrivenAccessor();

        var useSpacing = config.UseSpacing && config.SpacingMm > 0;
        var spacingFeet = config.SpacingMm / 304.8;

        // Không có bước hợp lệ và số lượng < 2 → chỉ ra 1 thanh: lưới móng 1 thanh là sai → cảnh báo rõ.
        if (!useSpacing && config.Count < 2)
        {
            warnings.Add($"Lưới D{config.Diameter.Millimeters}: thiếu bước rải hợp lệ và số lượng < 2 — " +
                         "chỉ tạo 1 thanh. Kiểm tra lại Spacing/Number.");
            return;
        }

        try
        {
            void SetLayout(bool normalSide)
            {
                if (useSpacing)
                    accessor.SetLayoutAsMaximumSpacing(spacingFeet, layoutWidthFeet, normalSide, true, true);
                else
                    accessor.SetLayoutAsFixedNumber(config.Count, layoutWidthFeet, normalSide, true, true);
            }

            SetLayout(normalSide: true);
            rebar.Document.Regenerate();

            // Nếu rải sai chiều (ra ngoài host) → đảo barsOnNormalSide.
            if (BarsOverflow(rebar, layoutWidthFeet))
            {
                SetLayout(normalSide: false);
                rebar.Document.Regenerate();
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Lỗi rải lưới D{config.Diameter.Millimeters}: {ex.Message}");
        }
    }

    /// <summary>Kiểm tra set có thanh nào nằm xa thanh đầu hơn dải layout không (rải sai chiều).</summary>
    private static bool BarsOverflow(Autodesk.Revit.DB.Structure.Rebar rebar, double layoutWidthFeet)
    {
        var n = rebar.NumberOfBarPositions;
        if (n <= 1) return false;

        var first = FirstPoint(rebar, 0);
        var last = FirstPoint(rebar, n - 1);
        if (first is null || last is null) return false;

        return first.DistanceTo(last) > layoutWidthFeet + 2.0 / 304.8;
    }

    private static XYZ? FirstPoint(Autodesk.Revit.DB.Structure.Rebar rebar, int barIndex)
    {
        foreach (var curve in rebar.GetCenterlineCurves(false, false, false,
                     MultiplanarOption.IncludeOnlyPlanarCurves, barIndex))
            return curve.GetEndPoint(0);
        return null;
    }
}

