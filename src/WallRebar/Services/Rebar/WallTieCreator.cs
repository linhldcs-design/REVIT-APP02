using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallRebar.Models;

namespace WallRebar.Services.Rebar;

/// <summary>
///     Creates tie bars connecting the two wall mesh faces from the dedicated Tie configuration.
/// </summary>
public sealed class WallTieCreator
{
    private readonly Document _document;
    private readonly RebarFamilyValidator _families;

    public WallTieCreator(Document document, RebarFamilyValidator families)
    {
        _document = document;
        _families = families;
    }

    public int Create(Element host, WallFrame frame, WallRebarModel model, List<string> warnings)
    {
        var config = model.Tie;
        var barType = _families.GetBarType(config.Diameter);
        if (barType == null)
        {
            warnings.Add($"Bo qua thep giang D{config.Diameter.Millimeters}: thieu RebarBarType.");
            return 0;
        }

        var hookType = _families.Get180HookType();
        if (hookType == null)
        {
            warnings.Add("Bo qua thep giang: thieu RebarHookType 180 do.");
            return 0;
        }

        var maximumSpacingFeet = config.SpacingMm / 304.8;
        if (maximumSpacingFeet <= 1e-6)
        {
            warnings.Add("Bo qua thep giang: buoc rai khong hop le.");
            return 0;
        }

        var cover = model.Cover;
        var startEndFeet = cover.StartEndMm / 304.8;
        var topBottomFeet = cover.TopBottomMm / 304.8;
        var leftRightFeet = cover.LeftRightMm / 304.8;
        var verticalBarRadius = model.Vertical.Diameter.Feet / 2;

        // Tim hai dau tie trung mat phang tim thep doc de hook 180 do om dung thanh doc hai mat.
        var offA = leftRightFeet + verticalBarRadius;
        var offB = frame.ThicknessFeet - leftRightFeet - verticalBarRadius;
        if (offB - offA <= 1e-6)
        {
            warnings.Add("Bo qua thep giang: tuong qua mong.");
            return 0;
        }

        var alongStart = startEndFeet;
        var alongLayoutFeet = frame.LengthFeet - 2 * startEndFeet;
        var zStart = topBottomFeet;
        var zLayoutFeet = frame.HeightFeet - 2 * topBottomFeet;
        if (alongLayoutFeet <= 1e-6 || zLayoutFeet <= 1e-6)
        {
            warnings.Add("Bo qua thep giang: tuong qua nho so voi lop bao ve.");
            return 0;
        }

        var alongGrid = MaximumSpacingGrid.Create(alongLayoutFeet, model.Vertical.SpacingMm / 304.8);
        var heightGrid = MaximumSpacingGrid.Create(zLayoutFeet, model.Horizontal.SpacingMm / 304.8);
        if (alongGrid == null || heightGrid == null)
        {
            warnings.Add("Bo qua thep giang: luoi thep doc/ngang khong hop le.");
            return 0;
        }

        // Theo hinh tham chieu: mot hang cao do co tie, hang ke tiep khong co tie.
        // Gia tri Tie.SpacingMm chi truyen truc tiep vao Layout Rule = Maximum Spacing doc chieu dai vach.
        var heightStride = 2 * heightGrid.SpacingFeet <= maximumSpacingFeet + 1e-6 ? 2 : 1;
        var tieLiftFeet = (model.Vertical.Diameter.Feet + config.Diameter.Feet) / 2;

        var createdBars = 0;
        var failedRows = 0;
        for (var heightIndex = 0; heightIndex <= heightGrid.IntervalCount; heightIndex += heightStride)
        {
            var z = zStart + heightIndex * heightGrid.SpacingFeet + tieLiftFeet;
            if (z > zStart + zLayoutFeet + 1e-6) continue;

            var p0 = frame.PointAt(alongStart, z, offA);
            var p1 = frame.PointAt(alongStart, z, offB);

            var rebar = CreateTieSet(host, barType, hookType, frame.DirAlong, p0, p1,
                maximumSpacingFeet, alongLayoutFeet, alongGrid);
            if (rebar == null)
            {
                failedRows++;
                continue;
            }

            createdBars += rebar.NumberOfBarPositions;
        }

        if (failedRows > 0)
            warnings.Add($"Khong tao duoc {failedRows} hang tie Maximum Spacing D{config.Diameter.Millimeters}.");

        return createdBars;
    }

    private Autodesk.Revit.DB.Structure.Rebar? CreateTieSet(Element host, RebarBarType barType,
        RebarHookType hookType, XYZ normal, XYZ p0, XYZ p1, double maximumSpacingFeet,
        double layoutFeet, MaximumSpacingGrid verticalGrid)
    {
        foreach (var candidateNormal in new[] { normal, -normal })
        {
            Autodesk.Revit.DB.Structure.Rebar? rebar = null;
            try
            {
                IList<Curve> curves = [Line.CreateBound(p0, p1)];
                rebar = RebarCompat.CreateFromCurves(
                    _document, barType, host, candidateNormal, curves,
                    RebarHookOrientationCompat.Left, RebarHookOrientationCompat.Left,
                    useExistingShapeIfPossible: true, startHookType: hookType, endHookType: hookType);

                if (!rebar.IsRebarShapeDriven())
                {
                    _document.Delete(rebar.Id);
                    continue;
                }

                var accessor = rebar.GetShapeDrivenAccessor();
                accessor.SetLayoutAsMaximumSpacing(maximumSpacingFeet, layoutFeet, true, true, true);
                _document.Regenerate();
                if (BarsOverflow(rebar, layoutFeet))
                {
                    accessor.SetLayoutAsMaximumSpacing(maximumSpacingFeet, layoutFeet, false, true, true);
                    _document.Regenerate();
                }

                AlignBarsToVerticalGrid(rebar, accessor, normal, verticalGrid);
                _document.Regenerate();
                return rebar;
            }
            catch
            {
                if (rebar != null && rebar.IsValidObject) _document.Delete(rebar.Id);
            }
        }

        return null;
    }

    private static void AlignBarsToVerticalGrid(Autodesk.Revit.DB.Structure.Rebar rebar,
        RebarShapeDrivenAccessor accessor, XYZ distributionDirection, MaximumSpacingGrid verticalGrid)
    {
        var lastTargetIndex = -1;
        for (var barIndex = 0; barIndex < rebar.NumberOfBarPositions; barIndex++)
        {
            var transform = accessor.GetBarPositionTransform(barIndex);
            var currentOffset = transform.Origin.DotProduct(distributionDirection);
            var targetIndex = (int)Math.Round(currentOffset / verticalGrid.SpacingFeet);
            targetIndex = Math.Clamp(targetIndex, lastTargetIndex + 1, verticalGrid.IntervalCount);
            lastTargetIndex = targetIndex;

            var targetOffset = targetIndex * verticalGrid.SpacingFeet;
            var delta = targetOffset - currentOffset;
            if (Math.Abs(delta) <= 1e-6) continue;

            rebar.MoveBarInSet(barIndex,
                Transform.CreateTranslation(distributionDirection * delta));
        }
    }

    private static bool BarsOverflow(Autodesk.Revit.DB.Structure.Rebar rebar, double layoutFeet)
    {
        var count = rebar.NumberOfBarPositions;
        if (count <= 1) return false;

        var first = FirstPoint(rebar, 0);
        var last = FirstPoint(rebar, count - 1);
        return first != null && last != null && first.DistanceTo(last) > layoutFeet + 2.0 / 304.8;
    }

    private static XYZ? FirstPoint(Autodesk.Revit.DB.Structure.Rebar rebar, int barIndex)
    {
        foreach (var curve in rebar.GetCenterlineCurves(false, false, false,
                     MultiplanarOption.IncludeOnlyPlanarCurves, barIndex))
            return curve.GetEndPoint(0);
        return null;
    }

    private sealed record MaximumSpacingGrid(int IntervalCount, double SpacingFeet)
    {
        public static MaximumSpacingGrid? Create(double layoutFeet, double maximumSpacingFeet)
        {
            if (layoutFeet <= 1e-6 || maximumSpacingFeet <= 1e-6) return null;
            var intervalCount = Math.Max(1, (int)Math.Ceiling(layoutFeet / maximumSpacingFeet));
            return new MaximumSpacingGrid(intervalCount, layoutFeet / intervalCount);
        }
    }
}
