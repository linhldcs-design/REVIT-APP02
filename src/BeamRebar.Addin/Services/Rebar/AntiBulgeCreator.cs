using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamRebar.Core.Models;

namespace BeamRebar.Addin.Services.Rebar;

/// <summary>
///     Tạo thép dọc cấu tạo chống phình ở hai mặt bên dầm khi h > ngưỡng (TCVN). Các thanh chạy dọc
///     trục, phân bố đều theo chiều cao trên mỗi mặt bên, cách mép trừ lớp bảo vệ.
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

    public int Create(Element host, SpanFrame frame, AntiBulgeConfig config, List<string> warnings)
    {
        if (!config.Enabled) return 0;

        var heightMm = frame.HeightFeet * 304.8;
        if (heightMm <= config.HeightThresholdMm) return 0;

        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bỏ qua thép chống phình D{config.Diameter.Millimeters}: thiếu RebarBarType.");
            return 0;
        }

        var coverSide = _cover.SideMm / 304.8;
        var coverTop = _cover.TopMm / 304.8;
        var coverBottom = _cover.BottomMm / 304.8;
        var barFeet = config.Diameter.Feet;

        // Vùng đặt theo chiều cao (giữa thép trên & dưới), số thanh mỗi mặt theo spacing.
        var usableHeight = frame.HeightFeet - coverTop - coverBottom;
        var spacingFeet = config.SpacingMm / 304.8;
        var perFace = Math.Max(1, (int)Math.Floor(usableHeight / spacingFeet) - 1);

        var halfW = frame.WidthFeet / 2 - coverSide - barFeet / 2;
        var created = 0;

        for (var face = -1; face <= 1; face += 2)
        {
            for (var i = 1; i <= perFace; i++)
            {
                var vertical = -(coverTop + usableHeight * i / (perFace + 1));
                var p0 = frame.AxisTop(0) + frame.Across * (halfW * face) + frame.Up * vertical;
                var p1 = frame.AxisTop(1) + frame.Across * (halfW * face) + frame.Up * vertical;

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
                    warnings.Add($"Lỗi tạo thép chống phình D{config.Diameter.Millimeters}: {ex.Message}");
                }
            }
        }

        return created;
    }
}
