using RevitAPP.Core.Models.DwgExport;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.DwgExport;

public sealed class DwgSheetLayoutPlannerTests
{
    [Fact]
    public void MixedScale_OneToSeventyFiveAndOneToTwentyFive_UsesRequiredFactors()
    {
        Assert.Equal(3d, DwgSheetLayoutPlanner.GeometryFactor(75, 25), 6);
        Assert.Equal(1d / 3d, DwgSheetLayoutPlanner.DimensionLinearFactor(75, 25), 6);
        Assert.Equal(1d, DwgSheetLayoutPlanner.GeometryFactor(75, 75), 6);
        Assert.Equal(1d, DwgSheetLayoutPlanner.DimensionLinearFactor(75, 75), 6);
    }

    [Fact]
    public void ReferenceScale_ReturnsLargestDenominator()
    {
        var viewports = new[]
        {
            Viewport(1, 100),
            Viewport(2, 20),
            Viewport(3, 50)
        };

        Assert.Equal(100, DwgSheetLayoutPlanner.ReferenceScale(viewports));
    }

    [Fact]
    public void ArrangeLeftToRight_NormalizesMinimaAndAddsGap()
    {
        var placements = DwgSheetLayoutPlanner.ArrangeLeftToRight(
            new[]
            {
                new DwgSheetExtents(1, -10, 20, 90, 70),
                new DwgSheetExtents(0, 5, -5, 205, 95)
            },
            25);

        Assert.Equal(new DwgSheetPlacement(0, -5, 5), placements[0]);
        Assert.Equal(new DwgSheetPlacement(1, 235, -20), placements[1]);
    }

    [Theory]
    [InlineData(DwgDrawingUnit.Millimetres, 100)]
    [InlineData(DwgDrawingUnit.Centimetres, 10)]
    [InlineData(DwgDrawingUnit.Metres, 0.1)]
    [InlineData(DwgDrawingUnit.Inches, 3.937007874)]
    public void MillimetresToDrawingUnits_ConvertsGap(DwgDrawingUnit unit, double expected)
    {
        Assert.Equal(expected, DwgSheetLayoutPlanner.MillimetresToDrawingUnits(100, unit), 8);
    }

    [Fact]
    public void ArrangeLeftToRight_DuplicateOrdinal_Throws()
    {
        Assert.Throws<ArgumentException>(() => DwgSheetLayoutPlanner.ArrangeLeftToRight(
            new[]
            {
                new DwgSheetExtents(0, 0, 0, 100, 100),
                new DwgSheetExtents(0, 0, 0, 100, 100)
            },
            10));
    }

    private static DwgViewportPlan Viewport(long id, int scale) =>
        new(id, id + 10, $"View {id}", scale, 0, 0, 0);
}
