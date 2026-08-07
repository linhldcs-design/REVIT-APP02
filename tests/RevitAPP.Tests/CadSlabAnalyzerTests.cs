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

    [Fact]
    public void Analyze_BaysSeparatedByBeamStrips_MergeIntoOneSlab()
    {
        // Three bays at one elevation with a beam drawn by both faces between them. The pour runs
        // across the beams, so the strips are absorbed and the result is a single slab.
        var segments = new List<CadStructureSegment>();
        var id = 1;
        double[] stations = { 0, 4000, 4200, 8000, 8200, 12000 };
        foreach (var y in new[] { 0.0, 3000.0 })
            for (var index = 0; index < stations.Length - 1; index++)
                segments.Add(Segment(id++, stations[index], y, stations[index + 1], y));
        foreach (var x in stations)
            segments.Add(Segment(id++, x, 0, x, 3000));

        var annotations = new[]
        {
            Annotation(200, 2000, 1500, "-0.050 Hs=120"),
            Annotation(201, 6100, 1500, "-0.050 Hs=120"),
            Annotation(202, 10100, 1500, "-0.050 Hs=120")
        };

        var result = CadSlabAnalyzer.Analyze(
            Package(segments, annotations),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.Equal(120, slab.EffectiveThicknessMm, 3);
        Assert.Equal(-50, slab.EffectiveOffsetMm, 3);
        Assert.Equal(2, slab.AbsorbedStripCount);
        Assert.Equal(CadSlabRegionStatus.Ready, slab.Status);
    }

    [Fact]
    public void Analyze_LabelWrittenOnTwoLines_ReadsBothValues()
    {
        // The plan stacks the elevation above the thickness in one label.
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 4000, 3000),
                Annotation(100, 2000, 1500, "+0.000\\PHs=100")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.Equal(0, slab.EffectiveOffsetMm, 3);
        Assert.Equal(100, slab.EffectiveThicknessMm, 3);
    }

    [Fact]
    public void Analyze_HatchedAndPlainBaysAtOneLevel_ArePouredSeparately()
    {
        // A hatched area is poured on its own, so it stays a slab of its own even where the plan
        // gives it the same level as the bays around it.
        var result = CadSlabAnalyzer.Analyze(
            Package(Grid(3, 1, 4000, 6000),
                Annotation(90, 2000, 3000, "-0.050 Hs=120"),
                Annotation(91, 6000, 3000, "-0.050 Hs=120"),
                Annotation(92, 10000, 3000, "-0.050 Hs=120")),
            new[] { Hatch(1, 4000, 0, 8000, 6000, "ANSI31", 1.0) },
            new CadSlabAnalysisOptions());

        // The hatched bay is poured on its own and leaves the plain bays on either side of it,
        // which no longer touch, so they are poured separately too.
        Assert.Equal(3, result.Regions.Count);
        var hatched = Assert.Single(result.Regions, region => region.IsLowered);
        Assert.Equal(24.0, hatched.AreaM2, 2);
        Assert.Equal(2, result.Regions.Count(region => !region.IsLowered));
        Assert.All(result.Regions, region => Assert.Equal(24.0, region.AreaM2, 2));
        Assert.All(result.Regions, region => Assert.Equal(-50, region.EffectiveOffsetMm, 3));
    }

    [Fact]
    public void Analyze_HatchedBayAtItsOwnLevel_StaysASeparateSlab()
    {
        var result = CadSlabAnalyzer.Analyze(
            Package(Grid(2, 1, 4000, 6000),
                Annotation(90, 2000, 3000, "+0.000 Hs=120"),
                Annotation(91, 6000, 3000, "-0.050 Hs=120")),
            new[] { Hatch(1, 4000, 0, 8000, 6000, "ANSI31", 1.0) },
            new CadSlabAnalysisOptions());

        Assert.Equal(2, result.Regions.Count);
        Assert.Contains(result.Regions, region => Math.Abs(region.EffectiveOffsetMm) < 1);
        Assert.Contains(result.Regions, region => Math.Abs(region.EffectiveOffsetMm + 50) < 1);
    }

    [Fact]
    public void Analyze_ThreeHatchStyles_ProduceThreeSlabsAtTheirOwnDrops()
    {
        // A plan draws each drop with its own pattern. However many patterns it uses, each is a
        // slab of its own, and each takes the drop the user gave that style.
        var segments = Grid(3, 1, 4000, 3000);
        var annotations = new[]
        {
            Annotation(200, 2000, 1500, "Hs=100"),
            Annotation(201, 6000, 1500, "Hs=100"),
            Annotation(202, 10000, 1500, "Hs=100")
        };
        var hatches = new[]
        {
            Hatch(1, 0, 0, 4000, 3000, "ANSI31", 1.0),
            Hatch(2, 4000, 0, 8000, 3000, "ANSI31", 2.0),
            Hatch(3, 8000, 0, 12000, 3000, "ANSI37", 1.0)
        };

        var result = CadSlabAnalyzer.Analyze(
            Package(segments, annotations), hatches,
            new CadSlabAnalysisOptions
            {
                HatchOffsetsMm = new Dictionary<string, double>
                {
                    ["ANSI31|1|0"] = -50,
                    ["ANSI31|2|0"] = -100,
                    ["ANSI37|1|0"] = -300
                }
            });

        Assert.Equal(3, result.HatchStyles.Count);
        Assert.Equal(3, result.Regions.Count);
        Assert.Contains(result.Regions, region => Math.Abs(region.EffectiveOffsetMm + 50) < 1);
        Assert.Contains(result.Regions, region => Math.Abs(region.EffectiveOffsetMm + 100) < 1);
        Assert.Contains(result.Regions, region => Math.Abs(region.EffectiveOffsetMm + 300) < 1);
        Assert.All(result.Regions, region => Assert.True(region.IsLowered));
    }

    [Fact]
    public void Analyze_SameHatchStyleInTwoBays_StaysOneSlab()
    {
        var segments = Grid(2, 1, 4000, 3000);
        var result = CadSlabAnalyzer.Analyze(
            Package(segments,
                Annotation(200, 2000, 1500, "Hs=100"),
                Annotation(201, 6000, 1500, "Hs=100")),
            new[]
            {
                Hatch(1, 0, 0, 4000, 3000, "ANSI31", 1.0),
                Hatch(2, 4000, 0, 8000, 3000, "ANSI31", 1.0)
            },
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.True(slab.IsLowered);
        Assert.Equal(2, slab.CellIds.Count);
    }

    [Fact]
    public void Analyze_LabelInOneBay_ReachesTheWholePour()
    {
        // A plan writes the section once and leaves the rest of the floor unlabelled. Every bay
        // still belongs to that pour, and the slab covers the whole plan rather than one bay.
        var segments = Grid(4, 2, 4000, 3000);
        var result = CadSlabAnalyzer.Analyze(
            Package(segments, Annotation(900, 2000, 1500, "+0.000 Hs=100")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.Equal(96.0, slab.AreaM2, 2);
        Assert.Equal(100, slab.DetectedThicknessMm);
        Assert.Equal(CadSlabRegionStatus.Ready, slab.Status);
    }

    [Fact]
    public void Analyze_BoundaryTrimmedAtColumns_KeepsThePiecesThatCloseIt()
    {
        // Trimming at a column face leaves stubs shorter than the minimum line length. They close
        // the bay, so only a piece touching nothing else is noise.
        var segments = new List<CadStructureSegment>();
        var id = 1;
        foreach (var y in new[] { 0.0, 6000.0 })
        {
            var x = 0.0;
            while (x < 8000)
            {
                segments.Add(Segment(id++, x, y, x + 150, y));
                segments.Add(Segment(id++, x + 150, y, Math.Min(x + 4000, 8000), y));
                x += 4000;
            }
        }
        segments.Add(Segment(id++, 0, 0, 0, 6000));
        segments.Add(Segment(id++, 8000, 0, 8000, 6000));
        segments.Add(Segment(id, 3000, 9000, 3120, 9000));

        var result = CadSlabAnalyzer.Analyze(
            Package(segments, Annotation(900, 4000, 3000, "+0.000 Hs=100")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions(MinimumLineLengthMm: 200));

        var slab = Assert.Single(result.Regions);
        Assert.Equal(48.0, slab.AreaM2, 2);
        Assert.Equal(1, result.ShortLinesIgnored);
        Assert.Equal(0, result.UnclosedVertexCount);
    }

    [Fact]
    public void Analyze_ElevationAndThicknessAsSeparateTexts_AreBothRead()
    {
        // A plan writes the level above the thickness with the level symbol between them, so the
        // two values arrive as separate text entities rather than one label.
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 8000, 6000),
                Annotation(90, 4000, 3600, "-0.050"),
                Annotation(91, 4000, 2400, "Hs=120")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.Equal(120, slab.DetectedThicknessMm);
        Assert.Equal(-50, slab.DetectedElevationMm);
        Assert.Equal(CadSlabRegionStatus.Ready, slab.Status);
    }

    [Theory]
    [InlineData(@"-0.050\PHs=120")]
    [InlineData(@"\W0.8;-0.050\PHs=120")]
    [InlineData(@"{\W0.8;-0.050\PHs=120}")]
    public void Analyze_MTextWrittenOnTwoLines_KeepsBothValues(string text)
    {
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 8000, 6000),
                new CadStructureAnnotation(
                    90, new CadStructurePoint2(4000, 3000), text, 0, "MText", string.Empty, true)),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.Equal(120, slab.DetectedThicknessMm);
        Assert.Equal(-50, slab.DetectedElevationMm);
    }

    [Theory]
    [InlineData("1818 -0.100", -100)]
    [InlineData("2050 200 -0.050", -50)]
    [InlineData("-0.100 1300", -100)]
    public void Analyze_DimensionsBesideTheLabel_DoNotBleedIntoTheLevel(string text, double expected)
    {
        // Dimensions sit right beside the level on a plan. Reading the whole number let 1818 join
        // -0.100 and put the slab eighty metres up.
        var result = CadSlabAnalyzer.Analyze(
            Package(Rectangle(1, 0, 0, 8000, 6000), Annotation(90, 4000, 3000, $"{text} Hs=120")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.Equal(expected, slab.EffectiveOffsetMm, 3);
        Assert.Equal(120, slab.EffectiveThicknessMm, 3);
    }

    [Fact]
    public void Analyze_MergedRegion_ReportsAPositiveAreaAndAnOuterLoopThatWindsOneWay()
    {
        // Stitching picks up an edge in whichever direction it was drawn. A reversed outer loop
        // shows as a negative area and Revit refuses the profile outright.
        var result = CadSlabAnalyzer.Analyze(
            Package(Grid(3, 2, 4000, 3000), Annotation(90, 2000, 1500, "+0.000 Hs=100")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions());

        var slab = Assert.Single(result.Regions);
        Assert.True(slab.AreaM2 > 0, "area must be positive");
        Assert.True(slab.OuterLoop.SignedAreaMm2 > 0, "outer loop must wind counter-clockwise");
        Assert.All(slab.Holes, hole =>
            Assert.True(hole.SignedAreaMm2 < 0, "a hole must wind the other way"));
    }

    [Fact]
    public void Analyze_StairCoreSpanningBays_IsOneHoleInsideOneSlab()
    {
        // A core drawn across four bays is a single opening, and the floor still reaches past it
        // to the far side: the core makes a hole, it does not cut the slab in two.
        var segments = Grid(4, 3, 4000, 3000);
        // The user draws a window round the core: the outline states the hole, whatever the slab
        // lines inside it do.
        var outline = Rectangle(900, 4000, 3000, 12000, 9000);

        var result = CadSlabAnalyzer.Analyze(
            Package(segments, Annotation(80, 2000, 1500, "+0.000 Hs=100")),
            Array.Empty<CadHatchRegion>(),
            new CadSlabAnalysisOptions { OpeningOutlinesMm = outline });

        var slab = Assert.Single(result.Regions);
        Assert.Equal(96.0, slab.AreaM2, 2);
        Assert.Single(slab.Holes);
        Assert.Equal(8, slab.CellIds.Count);
    }

    [Fact]
    public void Analyze_MatchingHatchesApart_ArePouredAsSeparateSlabs()
    {
        // Three areas hatched alike and labelled alike, but lying apart on the plan. Concrete
        // cannot bridge the gap between them, so each is a slab of its own.
        var result = CadSlabAnalyzer.Analyze(
            Package(Grid(5, 1, 4000, 6000),
                Annotation(90, 2000, 3000, "-0.100 Hs=120"),
                Annotation(91, 6000, 3000, "+0.000 Hs=120"),
                Annotation(92, 10000, 3000, "-0.100 Hs=120"),
                Annotation(93, 14000, 3000, "+0.000 Hs=120"),
                Annotation(94, 18000, 3000, "-0.100 Hs=120")),
            new[]
            {
                Hatch(1, 0, 0, 4000, 6000, "ANSI31", 1.0),
                Hatch(2, 8000, 0, 12000, 6000, "ANSI31", 1.0),
                Hatch(3, 16000, 0, 20000, 6000, "ANSI31", 1.0)
            },
            new CadSlabAnalysisOptions());

        var hatched = result.Regions.Where(region => region.IsLowered).ToArray();
        Assert.Equal(3, hatched.Length);
        Assert.All(hatched, region =>
        {
            Assert.Equal(24.0, region.AreaM2, 2);
            Assert.Equal(-100, region.EffectiveOffsetMm, 3);
            Assert.Equal(120, region.EffectiveThicknessMm, 3);
        });
    }

    private static CadHatchRegion Hatch(
        int id, double x1, double y1, double x2, double y2, string pattern, double scale) =>
        new(id, new[]
        {
            new CadStructurePoint2(x1, y1),
            new CadStructurePoint2(x2, y1),
            new CadStructurePoint2(x2, y2),
            new CadStructurePoint2(x1, y2)
        })
        {
            PatternName = pattern,
            PatternScale = scale
        };

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
