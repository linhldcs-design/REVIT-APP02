using RevitAPP.Core.Models.DwgExport;

namespace RevitAPP.Core.Services;

public static class DwgViewportScaleRegionPlanner
{
    public static IReadOnlyList<DwgViewportScaleRegion> MapToDrawingUnits(
        DwgSheetPlan sheet,
        DwgDrawingUnit drawingUnit)
    {
        if (sheet.Viewports.Count == 0) return Array.Empty<DwgViewportScaleRegion>();

        var reference = DwgSheetLayoutPlanner.ReferenceScale(sheet.Viewports);
        return sheet.Viewports.Select(viewport => new DwgViewportScaleRegion(
            viewport.ViewportId,
            viewport.ScaleDenominator,
            DwgSheetLayoutPlanner.FeetToDrawingUnits(viewport.SheetMinXFeet, drawingUnit),
            DwgSheetLayoutPlanner.FeetToDrawingUnits(viewport.SheetMinYFeet, drawingUnit),
            DwgSheetLayoutPlanner.FeetToDrawingUnits(viewport.SheetMaxXFeet, drawingUnit),
            DwgSheetLayoutPlanner.FeetToDrawingUnits(viewport.SheetMaxYFeet, drawingUnit),
            DwgSheetLayoutPlanner.GeometryFactor(reference, viewport.ScaleDenominator),
            DwgSheetLayoutPlanner.DimensionLinearFactor(reference, viewport.ScaleDenominator))).ToArray();
    }
}
