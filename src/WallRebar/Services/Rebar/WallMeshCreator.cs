using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallRebar.Models;

namespace WallRebar.Services.Rebar;

/// <summary>
///     Tạo một lưới thép phẳng tại MỘT mặt tường (mặt A hoặc mặt B): thanh dọc (theo chiều cao) + thanh ngang
///     (theo chiều dài), rải đều theo bước. Lưới đặt cách mặt tường một <c>thicknessOffset</c> (feet).
///     Móc đầu thanh dọc bẻ vào trong bê tông (qua bề dày) theo <see cref="HookType"/>.
/// </summary>
public sealed class WallMeshCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;

    public WallMeshCreator(Document document, RebarFamilyValidator families)
    {
        _document = document;
        _families = families;
    }

    /// <param name="thicknessOffsetFeet">Khoảng cách từ mặt A (gốc) tới lưới, qua bề dày.</param>
    /// <param name="hookBendSign">+1: móc bẻ theo +DirThickness; -1: theo -DirThickness (vào trong bê tông).</param>
    public WallMeshCreationResult Create(Element host, WallFrame frame, WallRebarModel model,
        double thicknessOffsetFeet, int hookBendSign, List<string> warnings)
    {
        var vertical = 0;
        var horizontal = 0;
        if (model.Vertical.Enabled)
            vertical += CreateVerticalBars(host, frame, model, thicknessOffsetFeet, hookBendSign, warnings);
        if (model.Horizontal.Enabled)
            horizontal += CreateHorizontalBars(host, frame, model, thicknessOffsetFeet, hookBendSign, warnings);
        return new WallMeshCreationResult(vertical, horizontal);
    }

    /// <summary>Thanh dọc: chạy theo DirUp; rải dọc DirAlong @ Vertical.SpacingMm. Móc trên/dưới bẻ qua bề dày.</summary>
    private int CreateVerticalBars(Element host, WallFrame frame, WallRebarModel model,
        double thicknessOffsetFeet, int hookBendSign, List<string> warnings)
    {
        var config = model.Vertical;
        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua thép dọc D{config.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        var cover = model.Cover;
        var startEndFeet = cover.StartEndMm / 304.8;
        var topBottomFeet = cover.TopBottomMm / 304.8;
        var topOffFeet = model.TopOffsetMm / 304.8;
        var botOffFeet = model.BottomOffsetMm / 304.8;

        var zBottom = topBottomFeet + botOffFeet;
        var zTop = frame.HeightFeet - topBottomFeet - topOffFeet;
        var alongStart = startEndFeet;
        var layoutFeet = frame.LengthFeet - 2 * startEndFeet;
        if (zTop - zBottom <= 1e-6 || layoutFeet <= 1e-6)
        {
            warnings.Add("Bỏ qua thép dọc: tường quá nhỏ so với lớp bảo vệ/offset.");
            return 0;
        }

        var p0 = frame.PointAt(alongStart, zBottom, thicknessOffsetFeet);
        var p1 = frame.PointAt(alongStart, zTop, thicknessOffsetFeet);

        // Móc bẻ qua bề dày (vào trong bê tông). bendDir = ±DirThickness.
        var bendDir = frame.DirThickness * hookBendSign;
        var curves = BuildBarWithEndHooks(p0, p1, model.BottomHookType, model.BottomHookLengthMm / 304.8,
            model.TopHookType, model.TopHookLengthMm / 304.8, bendDir);

        var normal = frame.DirAlong; // phương rải các thanh dọc
        if (!TryCreate(host, normal, curves, config, layoutFeet, "thép dọc", warnings))
            return 0;
        return 1;
    }

    /// <summary>Thanh ngang: chạy theo DirAlong; rải dọc DirUp @ Horizontal.SpacingMm. Không móc (đai mặt).
    ///     Xếp chồng vào trong bê tông một đường kính thanh dọc để không trùng tâm với thép dọc tại điểm giao.</summary>
    private int CreateHorizontalBars(Element host, WallFrame frame, WallRebarModel model,
        double thicknessOffsetFeet, int hookBendSign, List<string> warnings)
    {
        var config = model.Horizontal;
        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua thép ngang D{config.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        var cover = model.Cover;
        var startEndFeet = cover.StartEndMm / 304.8;
        var topBottomFeet = cover.TopBottomMm / 304.8;
        var horizStartFeet = model.HorizontalOffsetStartMm / 304.8;
        var horizEndFeet = model.HorizontalOffsetEndMm / 304.8;

        var alongStart = startEndFeet + horizStartFeet;
        var alongEnd = frame.LengthFeet - startEndFeet - horizEndFeet;
        var zStart = topBottomFeet;
        var layoutFeet = frame.HeightFeet - 2 * topBottomFeet;
        if (alongEnd - alongStart <= 1e-6 || layoutFeet <= 1e-6)
        {
            warnings.Add("Bỏ qua thép ngang: tường quá nhỏ so với lớp bảo vệ/offset.");
            return 0;
        }

        // Lùi thêm 1 đường kính thanh dọc vào trong bê tông (cùng phía móc bẻ) → 2 lớp lưới không đè tâm.
        var nestOffset = thicknessOffsetFeet + hookBendSign * model.Vertical.Diameter.Feet;
        var p0 = frame.PointAt(alongStart, zStart, nestOffset);
        var p1 = frame.PointAt(alongEnd, zStart, nestOffset);
        var curves = new List<Curve> { Line.CreateBound(p0, p1) };

        var normal = frame.DirUp; // phương rải các thanh ngang
        if (!TryCreate(host, normal, curves, config, layoutFeet, "thép ngang", warnings))
            return 0;
        return 1;
    }

    /// <summary>Thanh dọc với móc khác nhau ở 2 đầu (dưới/trên), đồng phẳng (trục + phương bẻ). Chiều dài
    ///     móc + đoạn quặp (Closed) clamp để KHÔNG vượt quá thân thanh → tránh CreateFromCurves "Can't solve
    ///     Rebar Shape" với input quá lớn (vd hook length &gt; chiều cao tường).</summary>
    private static IList<Curve> BuildBarWithEndHooks(XYZ pBottom, XYZ pTop,
        HookType bottomHook, double bottomLenFeet, HookType topHook, double topLenFeet, XYZ bendDir)
    {
        var curves = new List<Curve>();
        var barLen = pBottom.DistanceTo(pTop);
        var axis = (pTop - pBottom).Normalize();

        // Đoạn quặp song song trục (Closed) = nửa hook length, nhưng không quá 40% chiều dài thân.
        var maxLip = barLen * 0.4;

        // Móc chân (đầu pBottom).
        if (bottomHook != HookType.Straight && bottomLenFeet > 1e-6)
        {
            var bend = pBottom + bendDir * bottomLenFeet;
            if (bottomHook == HookType.Closed)
            {
                var lip = Math.Min(bottomLenFeet * 0.5, maxLip);
                curves.Add(Line.CreateBound(bend + axis * lip, bend));
            }
            curves.Add(Line.CreateBound(bend, pBottom));
        }

        curves.Add(Line.CreateBound(pBottom, pTop));

        // Móc đỉnh (đầu pTop).
        if (topHook != HookType.Straight && topLenFeet > 1e-6)
        {
            var bend = pTop + bendDir * topLenFeet;
            curves.Add(Line.CreateBound(pTop, bend));
            if (topHook == HookType.Closed)
            {
                var lip = Math.Min(topLenFeet * 0.5, maxLip);
                curves.Add(Line.CreateBound(bend, bend - axis * lip));
            }
        }

        return curves;
    }

    private bool TryCreate(Element host, XYZ normal, IList<Curve> curves, WallLayerConfig config,
        double layoutFeet, string label, List<string> warnings)
    {
        var barType = _families.GetBarType(config.Diameter)!;
        Autodesk.Revit.DB.Structure.Rebar? rebar;
        try
        {
            rebar = RebarCompat.CreateFromCurves(
                _document, barType, host, normal, curves,
                RebarHookOrientationCompat.Right, RebarHookOrientationCompat.Right);
        }
        catch (Exception ex)
        {
            warnings.Add($"Lỗi tạo {label} D{config.Diameter.Millimeters}: {ex.Message}");
            return false;
        }

        ApplyLayout(rebar, config, layoutFeet, label, warnings);
        return true;
    }

    /// <summary>Rải set theo bước (MaximumSpacing phủ hết dải). Đảo chiều nếu rải sai (ra ngoài host).</summary>
    private static void ApplyLayout(Autodesk.Revit.DB.Structure.Rebar rebar, WallLayerConfig config,
        double layoutFeet, string label, List<string> warnings)
    {
        if (!rebar.IsRebarShapeDriven()) return;
        var accessor = rebar.GetShapeDrivenAccessor();

        var spacingFeet = config.SpacingMm / 304.8;
        if (spacingFeet <= 1e-6)
        {
            warnings.Add($"{label} D{config.Diameter.Millimeters}: bước rải không hợp lệ — chỉ tạo 1 thanh.");
            return;
        }

        try
        {
            void SetLayout(bool normalSide)
                => accessor.SetLayoutAsMaximumSpacing(spacingFeet, layoutFeet, normalSide, true, true);

            SetLayout(true);
            rebar.Document.Regenerate();
            if (BarsOverflow(rebar, layoutFeet))
            {
                SetLayout(false);
                rebar.Document.Regenerate();
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Lỗi rải {label} D{config.Diameter.Millimeters}: {ex.Message}");
        }
    }

    private static bool BarsOverflow(Autodesk.Revit.DB.Structure.Rebar rebar, double layoutFeet)
    {
        var n = rebar.NumberOfBarPositions;
        if (n <= 1) return false;

        var first = FirstPoint(rebar, 0);
        var last = FirstPoint(rebar, n - 1);
        if (first is null || last is null) return false;
        return first.DistanceTo(last) > layoutFeet + 2.0 / 304.8;
    }

    private static XYZ? FirstPoint(Autodesk.Revit.DB.Structure.Rebar rebar, int barIndex)
    {
        foreach (var curve in rebar.GetCenterlineCurves(false, false, false,
                     MultiplanarOption.IncludeOnlyPlanarCurves, barIndex))
            return curve.GetEndPoint(0);
        return null;
    }
}
