using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services.Rebar;

/// <summary>
///     Creates horizontal reinforcement for the footing body. Each layer is one continuous stirrup profile
///     around the footing perimeter, distributed through the footing height.
/// </summary>
public sealed class FootingStirrupCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;

    public FootingStirrupCreator(Document document, RebarFamilyValidator families)
    {
        _document = document;
        _families = families;
    }

    public int Create(Element host, FootingFrame frame, PedestalBox? pedestal,
        HorizontalStirrupConfig config, CoverSettings cover, List<string> warnings)
    {
        var diameter = config.DiameterX;
        var barType = _families.GetBarType(diameter);
        if (barType == null)
        {
            warnings.Add($"Bo qua dai ngang D{diameter.Millimeters}: thieu RebarBarType.");
            return 0;
        }

        var sideCover = cover.SideMm / 304.8;
        const double centerU = 0.5;
        const double centerV = 0.5;
        var widthX = frame.WidthXFeet;
        var widthY = frame.WidthYFeet;
        var halfX = widthX / 2 - sideCover - diameter.Feet / 2;
        var halfY = widthY / 2 - sideCover - diameter.Feet / 2;
        if (halfX <= 1e-6 || halfY <= 1e-6)
        {
            warnings.Add("Bo qua dai ngang: co mong qua nho so voi lop bao ve.");
            return 0;
        }

        var layers = Math.Max(1, config.Layers);
        var created = 0;
        for (var i = 0; i < layers; i++)
        {
            var z = LayerZ(frame, cover, diameter, layers, i);
            var profile = StirrupProfile(frame, centerU, centerV, halfX, halfY, z);
            if (TryCreateProfile(host, barType, profile, config, diameter, warnings))
                created++;
        }

        return created;
    }

    private static double LayerZ(FootingFrame frame, CoverSettings cover,
        RebarDiameter diameter, int layers, int index)
    {
        var bottomBarZ = frame.BottomZFeet + cover.BottomMm / 304.8 + diameter.Feet / 2;
        var topBarZ = frame.BaseTopZFeet - cover.TopMm / 304.8 - diameter.Feet / 2;
        if (topBarZ <= bottomBarZ)
            return (frame.BottomZFeet + frame.BaseTopZFeet) / 2;

        return bottomBarZ + (topBarZ - bottomBarZ) * (index + 1) / (layers + 1);
    }

    private bool TryCreateProfile(Element host, RebarBarType barType,
        IList<Curve> profile, HorizontalStirrupConfig config, RebarDiameter diameter, List<string> warnings)
    {
        var hook = config.HookEnabled ? _families.GetHookType(HookAngle.Deg90) : null;
        var rebar = TryCreate(host, barType, hook, profile, diameter, warnings);
        if (rebar != null)
            return true;

        return hook != null && TryCreate(host, barType, null, profile, diameter, warnings) != null;
    }

    private Autodesk.Revit.DB.Structure.Rebar? TryCreate(Element host, RebarBarType barType,
        RebarHookType? hook, IList<Curve> profile, RebarDiameter diameter, List<string> warnings)
    {
        try
        {
            return RebarCompat.CreateFromCurves(
                _document, RebarStyle.StirrupTie, barType, hook, hook, host,
                XYZ.BasisZ, profile,
                right: false, useExistingShapeIfPossible: false);
        }
        catch (Exception ex)
        {
            if (hook == null)
                warnings.Add($"Loi tao dai ngang D{diameter.Millimeters}: {ex.Message}");
            else
                warnings.Add($"Bo moc 90 dai ngang D{diameter.Millimeters}: {ex.Message}");
            return null;
        }
    }

    private static IList<Curve> StirrupProfile(
        FootingFrame frame, double centerU, double centerV, double halfX, double halfY, double zFeet)
    {
        var center = frame.PointAt(centerU, centerV, zFeet);
        XYZ Corner(double sx, double sy) => center + frame.DirX * (sx * halfX) + frame.DirY * (sy * halfY);

        var c1 = Corner(-1, -1);
        var c2 = Corner(1, -1);
        var c3 = Corner(1, 1);
        var c4 = Corner(-1, 1);

        return
        [
            Line.CreateBound(c1, c2),
            Line.CreateBound(c2, c3),
            Line.CreateBound(c3, c4),
            Line.CreateBound(c4, c1)
        ];
    }
}
