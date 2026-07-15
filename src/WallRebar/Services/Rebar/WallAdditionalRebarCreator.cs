using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallRebar.Models;

namespace WallRebar.Services.Rebar;

/// <summary>
///     Creates the optional lower-zone vertical bars shown in blue in the longitudinal preview.
///     Bars use the main vertical diameter, sit midway between main vertical bars, and extend through
///     the lower two-thirds of the main vertical working height on both wall faces.
/// </summary>
public sealed class WallAdditionalRebarCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;

    public WallAdditionalRebarCreator(Document document, RebarFamilyValidator families)
    {
        _document = document;
        _families = families;
    }

    public int Create(Element host, WallFrame frame, WallRebarModel model,
        double offA, double offB, bool faceAIsExterior, List<string> warnings)
    {
        var config = model.Vertical;
        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bo qua thep tang cuong D{config.Diameter.Millimeters}: thieu RebarBarType.");
            return 0;
        }

        var maximumSpacingFeet = config.SpacingMm / 304.8;
        var startEndFeet = model.Cover.StartEndMm / 304.8;
        var topBottomFeet = model.Cover.TopBottomMm / 304.8;
        var zBottom = topBottomFeet + model.BottomOffsetMm / 304.8;
        var zMainTop = frame.HeightFeet - topBottomFeet - model.TopOffsetMm / 304.8;
        var layoutFeet = frame.LengthFeet - 2 * startEndFeet;
        if (maximumSpacingFeet <= 1e-6 || layoutFeet <= 1e-6 || zMainTop - zBottom <= 1e-6)
        {
            warnings.Add("Bo qua thep tang cuong: hinh hoc/buoc rai khong hop le.");
            return 0;
        }

        var intervalCount = Math.Max(1, (int)Math.Ceiling(layoutFeet / maximumSpacingFeet));
        var actualSpacingFeet = layoutFeet / intervalCount;
        var barCount = intervalCount;
        var alongStart = startEndFeet + actualSpacingFeet / 2;
        var additionalTop = zBottom + (zMainTop - zBottom) * 2.0 / 3.0;

        var createdSets = 0;
        foreach (var (thicknessOffset, hookBendSign, isExterior) in new[]
                 {
                     (offA, 1.0, faceAIsExterior),
                     (offB, -1.0, !faceAIsExterior)
                 })
        {
            var drawFace = isExterior
                ? model.DrawAdditionalRebarExterior
                : model.DrawAdditionalRebarInterior;
            if (!drawFace) continue;

            var p0 = frame.PointAt(alongStart, zBottom, thicknessOffset);
            var p1 = frame.PointAt(alongStart, additionalTop, thicknessOffset);
            var curves = BuildBarWithBottomHook(p0, p1, model.BottomHookType,
                model.BottomHookLengthMm / 304.8, frame.DirThickness * hookBendSign);
            if (TryCreateSet(host, barType, frame.DirAlong, curves, p0,
                    barCount, actualSpacingFeet, layoutFeet - actualSpacingFeet, warnings))
                createdSets++;
        }

        return createdSets;
    }

    private bool TryCreateSet(Element host, RebarBarType barType, XYZ normal, IList<Curve> curves, XYZ origin,
        int count, double spacingFeet, double distributionLengthFeet, List<string> warnings)
    {
        Autodesk.Revit.DB.Structure.Rebar? rebar = null;
        try
        {
            rebar = RebarCompat.CreateFromCurves(
                _document, barType, host, normal, curves,
                RebarHookOrientationCompat.Right, RebarHookOrientationCompat.Right,
                useExistingShapeIfPossible: true);

            if (!rebar.IsRebarShapeDriven())
                throw new InvalidOperationException("Rebar tang cuong khong phai shape-driven.");

            var accessor = rebar.GetShapeDrivenAccessor();
            accessor.SetLayoutAsNumberWithSpacing(count, spacingFeet, true, true, true);
            _document.Regenerate();

            if (BarsOutsideRange(rebar, origin, normal, distributionLengthFeet))
            {
                accessor.SetLayoutAsNumberWithSpacing(count, spacingFeet, false, true, true);
                _document.Regenerate();
            }

            return true;
        }
        catch (Exception ex)
        {
            if (rebar != null && rebar.IsValidObject) _document.Delete(rebar.Id);
            warnings.Add($"Loi tao thep tang cuong D{barType.BarNominalDiameter * 304.8:0}: {ex.Message}");
            return false;
        }
    }

    private static IList<Curve> BuildBarWithBottomHook(XYZ pBottom, XYZ pTop,
        HookType bottomHook, double hookLengthFeet, XYZ bendDirection)
    {
        var curves = new List<Curve>();
        var barLength = pBottom.DistanceTo(pTop);
        var axis = (pTop - pBottom).Normalize();

        if (bottomHook != HookType.Straight && hookLengthFeet > 1e-6)
        {
            var bend = pBottom + bendDirection * hookLengthFeet;
            if (bottomHook == HookType.Closed)
            {
                var lip = Math.Min(hookLengthFeet * 0.5, barLength * 0.4);
                curves.Add(Line.CreateBound(bend + axis * lip, bend));
            }

            curves.Add(Line.CreateBound(bend, pBottom));
        }

        curves.Add(Line.CreateBound(pBottom, pTop));
        return curves;
    }

    private static bool BarsOutsideRange(Autodesk.Revit.DB.Structure.Rebar rebar, XYZ origin,
        XYZ distributionDirection, double distributionLengthFeet)
    {
        for (var index = 0; index < rebar.NumberOfBarPositions; index++)
        {
            var point = FirstPoint(rebar, index);
            if (point == null) continue;
            var offset = (point - origin).DotProduct(distributionDirection);
            if (offset < -2.0 / 304.8 || offset > distributionLengthFeet + 2.0 / 304.8)
                return true;
        }

        return false;
    }

    private static XYZ? FirstPoint(Autodesk.Revit.DB.Structure.Rebar rebar, int barIndex)
    {
        foreach (var curve in rebar.GetCenterlineCurves(false, false, false,
                     MultiplanarOption.IncludeOnlyPlanarCurves, barIndex))
            return curve.GetEndPoint(0);
        return null;
    }
}
