using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamRebar.Core.Models;

namespace BeamRebar.Addin.Services.Rebar;

/// <summary>
///     Tạo thép dọc thẳng (thép chủ + gia cường) cho một nhịp: các thanh chạy dọc trục dầm, phân bố
///     đều theo phương ngang tiết diện, đặt ở mặt trên hoặc dưới sau khi trừ lớp bảo vệ. v1 neo thẳng
///     (kéo dài <c>anchorFeet</c> vào gối mỗi đầu), không uốn móc.
/// </summary>
public sealed class LongitudinalBarCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;
    private readonly CoverSettings _cover;

    public LongitudinalBarCreator(Document document, RebarFamilyValidator families, CoverSettings cover)
    {
        _document = document;
        _families = families;
        _cover = cover;
    }

    /// <summary>Tạo một lớp thép dọc. <paramref name="atTop"/> true = mặt trên, false = mặt dưới.</summary>
    public int Create(Element host, SpanFrame frame, RebarDiameter diameter, int count,
        bool atTop, double extraVerticalOffsetFeet, List<string> warnings)
    {
        if (count <= 0) return 0;

        var barType = _families.GetBarType(diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua thép dọc D{diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        var barFeet = diameter.Feet;
        var coverSide = _cover.SideMm / 304.8;
        var coverTop = _cover.TopMm / 304.8;
        var coverBottom = _cover.BottomMm / 304.8;

        // Vị trí theo chiều cao: tâm thanh cách mặt cover + nửa đường kính (+ offset lớp 2).
        var vertical = atTop
            ? -(coverTop + barFeet / 2 + extraVerticalOffsetFeet)
            : -(frame.HeightFeet - coverBottom - barFeet / 2 - extraVerticalOffsetFeet);

        // Phân bố ngang trong vùng (b - 2*cover - bar).
        var usableHalf = Math.Max(0, frame.WidthFeet / 2 - coverSide - barFeet / 2);
        var created = 0;

        for (var i = 0; i < count; i++)
        {
            var lateral = count == 1
                ? 0
                : -usableHalf + 2 * usableHalf * i / (count - 1);

            // v1: thép nằm GỌN trong dầm (lùi vào mỗi đầu một đoạn cover) để chắc chắn trong host,
            // tránh "rebar outside host" gây crash. Neo qua gối (anchorFeet) defer v2 khi đã ràng buộc host.
            var endInset = coverSide;
            var p0 = frame.AxisTop(0) + frame.Across * lateral + frame.Up * vertical + frame.Along * endInset;
            var p1 = frame.AxisTop(1) + frame.Across * lateral + frame.Up * vertical - frame.Along * endInset;

            try
            {
                IList<Curve> curves = [Line.CreateBound(p0, p1)];
                Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                    _document, RebarStyle.Standard, barType, null, null, host,
                    frame.Along, curves, RebarHookOrientation.Right, RebarHookOrientation.Right,
                    useExistingShapeIfPossible: true, createNewShape: true);
                created++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Lỗi tạo 1 thanh thép dọc D{diameter.Millimeters}: {ex.Message}");
            }
        }

        return created;
    }
}
