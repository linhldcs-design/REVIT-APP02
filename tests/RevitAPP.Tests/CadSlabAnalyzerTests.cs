using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadSlabAnalyzerTests
{
    [Fact]
    public void Analyze_CellsAtOneElevation_MergeIntoASingleSlab()
    {
        // Four bays of a floor poured in one piece. The beams between them are drawn as lines, and
        // the pour runs across them, so the result is one floor and not four.
        var segments = Grid(4, 2, 4000, 3000);
        var annotations = new List<CadStructureAnnotation>();
        var id = 100;
        for (var column = 0; column < 4; column++)
        for (var row = 0; row < 2; row++)
            annotations.Add(Annotation(id++, column * 4000 + 2000, row * 3000 + 1500, "+0.000 Hs=100"));

        var result = CadSlabAnalyzer.Analyze(
            Package(segments, annotations.ToArray()),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.Equal(100, slab.EffectiveThicknessMm, 3);
        Assert.Equal(0, slab.EffectiveOffsetMm, 3);
        Assert.Equal(8, slab.CellIds.Count);
    }

    [Fact]
    public void Analyze_TwoElevations_ProduceTwoSlabs()
    {
        var segments = Grid(2, 1, 4000, 3000);
        var result = CadSlabAnalyzer.Analyze(
            Package(segments,
                Annotation(100, 2000, 1500, "+0.000 Hs=100"),
                Annotation(101, 6000, 1500, "-0.050 Hs=100")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        Assert.Equal(2, result.Regions.Count);
        Assert.Contains(result.Regions, region => Math.Abs(region.EffectiveOffsetMm) < 1);
        Assert.Contains(result.Regions, region => Math.Abs(region.EffectiveOffsetMm + 50) < 1);
    }

    [Fact]
    public void Analyze_DifferentThickness_ProducesTwoSlabs()
    {
        var segments = Grid(2, 1, 4000, 3000);
        var result = CadSlabAnalyzer.Analyze(
            Package(segments,
                Annotation(100, 2000, 1500, "+0.000 Hs=100"),
                Annotation(101, 6000, 1500, "+0.000 Hs=150")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        Assert.Equal(2, result.Regions.Count);
        Assert.Contains(result.Regions, region => Math.Abs(region.EffectiveThicknessMm - 100) < 1);
        Assert.Contains(result.Regions, region => Math.Abs(region.EffectiveThicknessMm - 150) < 1);
    }

    [Theory]
    [InlineData("Hs=100", 100)]
    [InlineData("Hs = 120", 120)]
    [InlineData("HS200", 200)]
    [InlineData("h=100", 100)]
    [InlineData("S120", 120)]
    [InlineData("100", 100)]
    [InlineData("120", 120)]
    [InlineData("200", 200)]
    [InlineData("100mm", 100)]
    public void Analyze_ThicknessLabels_AreRead(string text, double expected)
    {
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 4000, 3000), Annotation(100, 2000, 1500, text)),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        Assert.Equal(expected, Assert.Single(result.Regions).EffectiveThicknessMm, 3);
    }

    [Theory]
    [InlineData("3950")]
    [InlineData("1550")]
    [InlineData("28800")]
    public void Analyze_NumbersOutsideTheThicknessRange_AreNotReadAsThickness(string text)
    {
        // A plan carries grid spacings and dimensions inside the bays. Treating any number as a
        // thickness would turn a 3950 mm grid spacing into a 3950 mm slab.
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 4000, 3000), Annotation(100, 2000, 1500, text)),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.Null(slab.DetectedThicknessMm);
        Assert.Equal(100, slab.EffectiveThicknessMm, 3);
    }

    [Theory]
    [InlineData("+0.000", 0)]
    [InlineData("0.000", 0)]
    [InlineData("-0.050", -50)]
    [InlineData("-0.100", -100)]
    [InlineData("-1.500", -1500)]
    public void Analyze_ElevationLabels_AreReadInMetres(string text, double expectedMm)
    {
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 4000, 3000), Annotation(100, 2000, 1500, $"{text} Hs=100")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        Assert.Equal(expectedMm, Assert.Single(result.Regions).EffectiveOffsetMm, 3);
    }

    [Fact]
    public void Analyze_ElevationAndThicknessTogether_DoNotConfuseEachOther()
    {
        // -0.100 is an elevation and 100 is a thickness. Both appear in the same label.
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 4000, 3000), Annotation(100, 2000, 1500, "-0.100 Hs=100")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.Equal(-100, slab.EffectiveOffsetMm, 3);
        Assert.Equal(100, slab.EffectiveThicknessMm, 3);
    }

    [Fact]
    public void Analyze_HatchedBay_IsMarkedLowered()
    {
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 4000, 3000), Annotation(100, 2000, 1500, "Hs=100")),
            new[]
            {
                new CadHatchRegion(1, new[]
                {
                    new CadStructurePoint2(0, 0),
                    new CadStructurePoint2(4000, 0),
                    new CadStructurePoint2(4000, 3000),
                    new CadStructurePoint2(0, 3000)
                })
            },
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.True(slab.IsLowered);
        Assert.Equal(-50, slab.EffectiveOffsetMm, 3);
    }

    [Fact]
    public void Analyze_MissingLabels_FallBackToTheDefaults()
    {
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 4000, 3000)),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions(DefaultThicknessMm: 120, DefaultOffsetMm: -20));

        var slab = Assert.Single(result.Regions);
        Assert.Equal(120, slab.EffectiveThicknessMm, 3);
        Assert.Equal(-20, slab.EffectiveOffsetMm, 3);
        Assert.Equal(CadSlabRegionStatus.MissingThickness, slab.Status);
    }

    [Fact]
    public void Analyze_OverrideOptions_IgnoreTheDrawingLabels()
    {
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 4000, 3000), Annotation(100, 2000, 1500, "-0.050 Hs=200")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions(
                DefaultThicknessMm: 100, DefaultOffsetMm: 0,
                OverrideThickness: true, OverrideElevation: true));

        var slab = Assert.Single(result.Regions);
        Assert.Equal(100, slab.EffectiveThicknessMm, 3);
        Assert.Equal(0, slab.EffectiveOffsetMm, 3);
    }

    [Fact]
    public void Analyze_TinyRegion_IsDroppedByTheAreaGate()
    {
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 500, 500), Annotation(100, 250, 250, "Hs=100")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions(MinimumRegionAreaM2: 1.0));

        Assert.Empty(result.Regions);
    }

    private static List<CadStructureSegment> Grid(int columns, int rows, double width, double height)
    {
        var segments = new List<CadStructureSegment>();
        var id = 1;
        for (var row = 0; row <= rows; row++)
            segments.Add(Segment(id++, 0, row * height, columns * width, row * height));
        for (var column = 0; column <= columns; column++)
            segments.Add(Segment(id++, column * width, 0, column * width, rows * height));
        return segments;
    }

    private static List<CadStructureSegment> Rectangle(
        int firstId, double x1, double y1, double x2, double y2) =>
        new()
        {
            Segment(firstId, x1, y1, x2, y1),
            Segment(firstId + 1, x2, y1, x2, y2),
            Segment(firstId + 2, x2, y2, x1, y2),
            Segment(firstId + 3, x1, y2, x1, y1)
        };

    private static CadStructureTransferPackage Package(
        IReadOnlyList<CadStructureSegment> segments,
        params CadStructureAnnotation[] annotations) =>
        new(CadStructureTransferPackage.CurrentSchemaVersion, "slab-test", DateTime.UtcNow,
            "slab.dwg", "2025", 4, default, segments)
        {
            Annotations = annotations
        };

    private static CadStructureSegment Segment(
        int id, double x1, double y1, double x2, double y2) =>
        new(id, new CadStructurePoint2(x1, y1), new CadStructurePoint2(x2, y2), "SLAB", string.Empty);

    private static CadStructureAnnotation Annotation(int id, double x, double y, string text) =>
        new(id, new CadStructurePoint2(x, y), text, 0, "TEXT", string.Empty, false);
}
