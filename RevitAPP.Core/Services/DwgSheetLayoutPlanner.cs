using RevitAPP.Core.Models.DwgExport;

namespace RevitAPP.Core.Services;

public static class DwgSheetLayoutPlanner
{
    public static int ReferenceScale(IEnumerable<DwgViewportPlan> viewports)
    {
        var scales = viewports.Select(viewport => viewport.ScaleDenominator).ToArray();
        if (scales.Length == 0)
            throw new ArgumentException("Sheet phải có ít nhất một viewport.", nameof(viewports));
        if (scales.Any(scale => scale <= 0))
            throw new ArgumentOutOfRangeException(nameof(viewports), "Tỷ lệ view phải lớn hơn 0.");

        return scales.Max();
    }


    public static double GeometryFactor(int referenceScale, int viewScale)
    {
        ValidateScales(referenceScale, viewScale);
        return (double)referenceScale / viewScale;
    }

    public static double DimensionLinearFactor(int referenceScale, int viewScale)
    {
        ValidateScales(referenceScale, viewScale);
        return (double)viewScale / referenceScale;
    }

    public static IReadOnlyList<DwgSheetPlacement> ArrangeLeftToRight(
        IEnumerable<DwgSheetExtents> extents,
        double gapDrawingUnits)
    {
        if (!IsFinite(gapDrawingUnits) || gapDrawingUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(gapDrawingUnits));

        var ordered = extents.OrderBy(item => item.Ordinal).ToArray();
        if (ordered.Select(item => item.Ordinal).Distinct().Count() != ordered.Length)
            throw new ArgumentException("Ordinal của sheet phải duy nhất.", nameof(extents));

        var placements = new List<DwgSheetPlacement>(ordered.Length);
        var nextX = 0d;
        foreach (var item in ordered)
        {
            ValidateExtents(item);
            placements.Add(new DwgSheetPlacement(item.Ordinal, nextX - item.MinX, -item.MinY));
            nextX += item.Width + gapDrawingUnits;
        }

        return placements;
    }

    public static double MillimetresToDrawingUnits(
        double millimetres,
        DwgDrawingUnit unit)
    {
        if (!IsFinite(millimetres) || millimetres < 0)
            throw new ArgumentOutOfRangeException(nameof(millimetres));

        return unit switch
        {
            DwgDrawingUnit.Millimetres => millimetres,
            DwgDrawingUnit.Centimetres => millimetres / 10d,
            DwgDrawingUnit.Metres => millimetres / 1000d,
            DwgDrawingUnit.Inches => millimetres / 25.4d,
            DwgDrawingUnit.Feet => millimetres / 304.8d,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }

    public static double FeetToDrawingUnits(double feet, DwgDrawingUnit unit)
    {
        if (!IsFinite(feet)) throw new ArgumentOutOfRangeException(nameof(feet));

        const double millimetresPerFoot = 304.8d;
        var millimetres = feet * millimetresPerFoot;
        return unit switch
        {
            DwgDrawingUnit.Millimetres => millimetres,
            DwgDrawingUnit.Centimetres => millimetres / 10d,
            DwgDrawingUnit.Metres => millimetres / 1000d,
            DwgDrawingUnit.Inches => feet * 12d,
            DwgDrawingUnit.Feet => feet,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }

    public static double InchesToDrawingUnits(double inches, DwgDrawingUnit unit)
    {
        if (!IsFinite(inches)) throw new ArgumentOutOfRangeException(nameof(inches));

        return unit switch
        {
            DwgDrawingUnit.Millimetres => inches * 25.4d,
            DwgDrawingUnit.Centimetres => inches * 2.54d,
            DwgDrawingUnit.Metres => inches * 0.0254d,
            DwgDrawingUnit.Inches => inches,
            DwgDrawingUnit.Feet => inches / 12d,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }

    private static void ValidateScales(int referenceScale, int viewScale)
    {
        if (referenceScale <= 0) throw new ArgumentOutOfRangeException(nameof(referenceScale));
        if (viewScale <= 0) throw new ArgumentOutOfRangeException(nameof(viewScale));
        if (viewScale > referenceScale)
            throw new ArgumentException("Reference scale phải là mẫu số lớn nhất của sheet.");
    }

    private static void ValidateExtents(DwgSheetExtents item)
    {
        if (!IsFinite(item.MinX) || !IsFinite(item.MinY)
            || !IsFinite(item.MaxX) || !IsFinite(item.MaxY)
            || item.MaxX <= item.MinX || item.MaxY <= item.MinY)
            throw new ArgumentException($"Extents sheet ordinal {item.Ordinal} không hợp lệ.");
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
