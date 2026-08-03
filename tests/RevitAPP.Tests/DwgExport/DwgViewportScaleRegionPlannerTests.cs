using RevitAPP.Core.Models.DwgExport;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.DwgExport;

public sealed class DwgViewportScaleRegionPlannerTests
{
    [Fact]
    public void MapToDrawingUnits_UsesSignedRevitPaperCoordinates()
    {
        var sheet = new DwgSheetPlan(
            3, 1, "S-01", "Sheet", "sheet.dwg",
            new[]
            {
                new DwgViewportPlan(10, 20, "View", 75, 0, 0, 0, -0.5, 0.25, 1.25, 1.5)
            });

        var regions = DwgViewportScaleRegionPlanner.MapToDrawingUnits(
            sheet,
            DwgDrawingUnit.Millimetres);

        Assert.Equal(-152.4, regions[0].MinX, 6);
        Assert.Equal(76.2, regions[0].MinY, 6);
        Assert.Equal(381, regions[0].MaxX, 6);
        Assert.Equal(457.2, regions[0].MaxY, 6);
    }

    [Fact]
    public void MapToDrawingUnits_ConvertsToRequestedDwgUnit()
    {
        var sheet = new DwgSheetPlan(
            2, 1, "S-01", "Sheet", "sheet.dwg",
            new[] { new DwgViewportPlan(10, 20, "View", 75, 0, 0, 0, -1, -2, 1, 2) });

        var regions = DwgViewportScaleRegionPlanner.MapToDrawingUnits(
            sheet,
            DwgDrawingUnit.Feet);

        Assert.Equal(-1, regions[0].MinX);
        Assert.Equal(-2, regions[0].MinY);
        Assert.Equal(1, regions[0].MaxX);
        Assert.Equal(2, regions[0].MaxY);
    }

    [Fact]
    public void MapToDrawingUnits_OneToSeventyFiveAndOneToTwentyFive_MapsFactors()
    {
        var sheet = new DwgSheetPlan(
            0, 1, "S-01", "Sheet", "sheet.dwg",
            new[]
            {
                new DwgViewportPlan(10, 20, "Reference", 75, 2, 5, 0, 1, 4, 3, 6),
                new DwgViewportPlan(11, 21, "Detail", 25, 7, 5, 0, 6, 4, 8, 6)
            });

        var regions = DwgViewportScaleRegionPlanner.MapToDrawingUnits(
            sheet,
            DwgDrawingUnit.Feet);

        Assert.Equal(2, regions.Count);
        Assert.Equal(1d, regions[0].GeometryFactor, 6);
        Assert.Equal(1d, regions[0].DimensionLinearFactor, 6);
        Assert.Equal(3d, regions[1].GeometryFactor, 6);
        Assert.Equal(1d / 3d, regions[1].DimensionLinearFactor, 6);
        Assert.Equal(6, regions[1].MinX, 6);
        Assert.Equal(4, regions[1].MinY, 6);
        Assert.Equal(8, regions[1].MaxX, 6);
        Assert.Equal(6, regions[1].MaxY, 6);
    }
}
