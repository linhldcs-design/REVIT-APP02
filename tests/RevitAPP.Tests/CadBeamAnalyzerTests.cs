using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadBeamAnalyzerTests
{
    [Fact]
    public void Analyze_FragmentedRailsWithShortGaps_ReturnsOneContinuousBeam()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 3000, -100),
                Segment(2, 3100, -100, 8000, -100),
                Segment(3, 0, 100, 4500, 100),
                Segment(4, 4600, 100, 8000, 100),
                Segment(5, 3000, -100, 3000, 100),
                Segment(6, 4500, -100, 4500, 100)
            },
            Annotation(100, 4000, 450, "DK3-200x300"));

        var result = CadBeamAnalyzer.Analyze(package,
            new[] { Segment(90, 0, 0, 8000, 0, "GRID") },
            new CadBeamAnalysisOptions(GapJoinToleranceMm: 300));

        var beam = Assert.Single(result.Beams);
        Assert.Equal(8000, beam.LengthMm, 3);
        Assert.Equal(200, beam.GeometryWidthMm, 3);
        Assert.Equal(300, beam.TextHeightMm);
        Assert.Equal("DK3", beam.Mark);
        Assert.True(beam.ReconstructedOnGridAxis);
        Assert.Equal(CadBeamCandidateStatus.Ready, beam.Status);
        Assert.DoesNotContain(5, beam.SourceSegmentIds);
        Assert.DoesNotContain(6, beam.SourceSegmentIds);
    }

    [Fact]
    public void Analyze_StaggeredRailFragmentsAroundGrid_ReturnsOneBeam()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 2800, -100),
                Segment(2, 4300, -100, 8000, -100),
                Segment(3, 0, 100, 4200, 100),
                Segment(4, 5600, 100, 8000, 100),
                Segment(10, 2800, -100, 2800, 0),
                Segment(11, 4200, 0, 4200, 100),
                Segment(12, 4300, -100, 4300, 0),
                Segment(13, 5600, 0, 5600, 100)
            },
            Annotation(100, 3500, 500, "D1(200x450)"));

        var result = CadBeamAnalyzer.Analyze(package,
            new[] { Segment(90, 0, 0, 8000, 0, "GRID") });

        var beam = Assert.Single(result.Beams);
        Assert.Equal(8000, beam.LengthMm, 3);
        Assert.Equal(450, beam.EffectiveHeightMm, 3);
        Assert.Equal(4, beam.SourceSegmentIds.Count);
    }

    [Fact]
    public void Analyze_TextWidthMismatch_KeepsGeometryWidthAndTextHeight()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -110, 6000, -110),
                Segment(2, 0, 110, 6000, 110)
            },
            Annotation(100, 3000, 300, "D2-200x300"));

        var beam = Assert.Single(CadBeamAnalyzer.Analyze(package, Array.Empty<CadStructureSegment>()).Beams);

        Assert.Equal(220, beam.EffectiveWidthMm, 3);
        Assert.Equal(300, beam.EffectiveHeightMm, 3);
        Assert.Equal(CadBeamCandidateStatus.TextWidthMismatch, beam.Status);
    }

    [Fact]
    public void Analyze_FormattedMText_ParsesMarkAndSection()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 6000, -100),
                Segment(2, 0, 100, 6000, 100)
            },
            Annotation(100, 3000, 300, "{\\C2;DK3-200x300\\P}"));

        var beam = Assert.Single(CadBeamAnalyzer.Analyze(
            package, Array.Empty<CadStructureSegment>()).Beams);

        Assert.Equal("DK3", beam.Mark);
        Assert.Equal(300, beam.TextHeightMm);
    }

    [Fact]
    public void Analyze_SectionTextChanges_SplitsOnlyAtSectionTransition()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 9000, -100),
                Segment(2, 0, 100, 9000, 100)
            },
            Annotation(100, 2000, 300, "D1-200x300"),
            Annotation(101, 7000, 300, "D2-200x450"));

        var beams = CadBeamAnalyzer.Analyze(
            package, Array.Empty<CadStructureSegment>()).Beams;

        Assert.Equal(2, beams.Count);
        Assert.Equal(new[] { 300.0, 450.0 },
            beams.OrderBy(beam => beam.StartMm.X).Select(beam => beam.EffectiveHeightMm).ToArray());
        Assert.Equal(9000, beams.Sum(beam => beam.LengthMm), 3);
    }

    [Fact]
    public void Analyze_GeometryWidthChangesOnSameAxis_KeepsBothAdjacentRuns()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 4500, -100),
                Segment(2, 0, 100, 4500, 100),
                Segment(3, 4500, -150, 9000, -150),
                Segment(4, 4500, 150, 9000, 150)
            },
            Annotation(100, 2000, 350, "D1-200x300"),
            Annotation(101, 7000, 400, "D2-300x450"));

        var beams = CadBeamAnalyzer.Analyze(
            package, new[] { Segment(90, 0, 0, 9000, 0, "GRID") }).Beams
            .OrderBy(beam => beam.StartMm.X).ToArray();

        Assert.Equal(2, beams.Length);
        Assert.Equal(new[] { 200.0, 300.0 }, beams.Select(beam => beam.GeometryWidthMm).ToArray());
        Assert.Equal(new[] { 300.0, 450.0 }, beams.Select(beam => beam.EffectiveHeightMm).ToArray());
        Assert.Equal(9000, beams.Sum(beam => beam.LengthMm), 3);
        Assert.All(beams, beam => Assert.Equal(CadBeamCandidateStatus.Ready, beam.Status));
    }

    [Fact]
    public void Analyze_ParallelBeamTextWithinSearchRadius_DoesNotSplitCurrentGeometry()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 6000, -100),
                Segment(2, 0, 100, 6000, 100)
            },
            Annotation(100, 2000, 250, "D1-200x300"),
            Annotation(101, 4000, 850, "D2-300x450"));

        var beam = Assert.Single(CadBeamAnalyzer.Analyze(
            package, Array.Empty<CadStructureSegment>()).Beams);

        Assert.Equal(200, beam.GeometryWidthMm, 3);
        Assert.Equal(300, beam.EffectiveHeightMm, 3);
        Assert.Equal("D1", beam.Mark);
    }

    [Fact]
    public void Analyze_ParallelBeamsWithSameWidth_OwnTheirNearestText()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 6000, -100),
                Segment(2, 0, 100, 6000, 100),
                Segment(3, 0, 500, 6000, 500),
                Segment(4, 0, 700, 6000, 700)
            },
            Annotation(100, 2500, 180, "D1-200x300"),
            Annotation(101, 3500, 780, "D2-200x500"));

        var beams = CadBeamAnalyzer.Analyze(
                package, Array.Empty<CadStructureSegment>()).Beams
            .Where(beam => beam.Status == CadBeamCandidateStatus.Ready)
            .OrderBy(beam => beam.StartMm.Y).ToArray();

        Assert.Equal(2, beams.Length);
        Assert.Equal(new[] { 300.0, 500.0 }, beams.Select(beam => beam.EffectiveHeightMm).ToArray());
        Assert.All(beams, beam => Assert.Equal(6000, beam.LengthMm, 3));
    }

    [Fact]
    public void Analyze_DistantGridDoesNotMoveTextSectionTransition()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 9000, -100),
                Segment(2, 0, 100, 9000, 100)
            },
            Annotation(100, 2000, 300, "D1-200x300"),
            Annotation(101, 7000, 300, "D2-200x450"));

        var beams = CadBeamAnalyzer.Analyze(
                package, new[] { Segment(90, 1000, -2000, 1000, 2000, "GRID") }).Beams
            .OrderBy(beam => beam.StartMm.X).ToArray();

        Assert.Equal(2, beams.Length);
        Assert.Equal(4500, beams[0].LengthMm, 3);
        Assert.Equal(4500, beams[1].LengthMm, 3);
    }

    [Fact]
    public void Analyze_SingleCenterLine_DoesNotGuessWidth()
    {
        var package = Package(
            new[] { Segment(1, 0, 0, 6000, 0) },
            Annotation(100, 3000, 200, "200x300"));

        var result = CadBeamAnalyzer.Analyze(package, Array.Empty<CadStructureSegment>());

        Assert.Empty(result.Beams);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2001)]
    public void Analyze_InvalidGapJoin_ReturnsValidationError(double gap)
    {
        var result = CadBeamAnalyzer.Analyze(
            Package(new[] { Segment(1, 0, 0, 1000, 0) }),
            Array.Empty<CadStructureSegment>(),
            new CadBeamAnalysisOptions(GapJoinToleranceMm: gap));

        Assert.Contains("Gap Join", result.Error);
    }

    [Fact]
    public void Analyze_GapJoinControlsRailCoverage_GridDoesNotBypassGate()
    {
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 900, -100),
                Segment(2, 2900, -100, 3800, -100),
                Segment(3, 0, 100, 900, 100),
                Segment(4, 2900, 100, 3800, 100)
            },
            Annotation(100, 1900, 300, "D1-200x300"));
        var grids = new[] { Segment(90, 0, 0, 3800, 0, "GRID") };

        var disconnected = CadBeamAnalyzer.Analyze(package, grids,
            new CadBeamAnalysisOptions(GapJoinToleranceMm: 0));
        var joined = CadBeamAnalyzer.Analyze(package, grids,
            new CadBeamAnalysisOptions(GapJoinToleranceMm: 2000));

        Assert.Empty(disconnected.Beams);
        Assert.Single(joined.Beams);
    }

    [Fact]
    public void Analyze_ShortBeamSharingRailWithLongBeam_ReturnsBothBeams()
    {
        // A long DK1 spans the bay while a short DK2 stub starts on the same lower boundary
        // line. Both boundaries land in one rail bucket, so rail coverage has to be measured
        // over the pair extent, not over everything the rail touches elsewhere.
        var package = Package(
            new[]
            {
                Segment(1, 0, -100, 20000, -100),
                Segment(2, 0, 100, 20000, 100),
                Segment(3, 2000, -300, 4450, -300),
                Segment(4, 2000, -100, 4450, -100)
            },
            Annotation(100, 10000, 400, "DK1-200x450"),
            Annotation(101, 3200, -500, "DK2-200x300"));

        var result = CadBeamAnalyzer.Analyze(package,
            new[] { Segment(90, 0, 0, 20000, 0, "GRID") },
            new CadBeamAnalysisOptions());

        Assert.Equal(2, result.Beams.Count);
        Assert.Contains(result.Beams, beam => beam.Mark == "DK1" && beam.TextHeightMm == 450);
        var stub = Assert.Single(result.Beams, beam => beam.Mark == "DK2");
        Assert.Equal(2450, stub.LengthMm, 3);
        Assert.Equal(200, stub.GeometryWidthMm, 3);
        Assert.Equal(300, stub.TextHeightMm);
    }

    [Fact]
    public void Analyze_BoundaryTrimmedIntoShortPieces_ReturnsOneFullLengthBeam()
    {
        // Boundaries get trimmed at every column face, leaving pieces well under the minimum
        // line length. They still form one real boundary once assembled.
        var segments = new List<CadStructureSegment>();
        var id = 1;
        for (var x = 0.0; x < 12000; x += 500)
            segments.Add(Segment(id++, x, -100, x + 300, -100));
        segments.Add(Segment(id, 0, 100, 12000, 100));

        var result = CadBeamAnalyzer.Analyze(
            Package(segments, Annotation(100, 6000, 300, "DK1-200x450")),
            new[] { Segment(90, 0, 0, 12000, 0, "GRID") },
            new CadBeamAnalysisOptions());

        var beam = Assert.Single(result.Beams);
        Assert.Equal(12000, beam.LengthMm, 3);
        Assert.Equal(200, beam.GeometryWidthMm, 3);
        Assert.Equal(CadBeamCandidateStatus.Ready, beam.Status);
        Assert.Equal(0, result.ShortLinesIgnored);
    }

    [Fact]
    public void Analyze_OneBoundaryTrimmedShort_KeepsBeamAtFullLength()
    {
        var result = CadBeamAnalyzer.Analyze(
            Package(
                new[]
                {
                    Segment(1, 0, -100, 12000, -100),
                    Segment(2, 4000, 100, 8000, 100)
                },
                Annotation(100, 6000, 300, "DK1-200x450")),
            new[] { Segment(90, 0, 0, 12000, 0, "GRID") },
            new CadBeamAnalysisOptions());

        var beam = Assert.Single(result.Beams);
        Assert.Equal(12000, beam.LengthMm, 3);
        Assert.Equal("DK1", beam.Mark);
    }

    [Fact]
    public void Analyze_BoundaryOffsetDrift_KeepsBeamAsOneRun()
    {
        // The right half of the lower boundary sits a few millimetres off, as trimming and
        // snapping leave it in a real drawing.
        var result = CadBeamAnalyzer.Analyze(
            Package(
                new[]
                {
                    Segment(1, 0, 100, 12000, 100),
                    Segment(2, 0, -100, 5000, -100),
                    Segment(3, 5200, -96, 12000, -96)
                },
                Annotation(100, 6000, 300, "DK1-200x450")),
            new[] { Segment(90, 0, 0, 12000, 0, "GRID") },
            new CadBeamAnalysisOptions());

        var beam = Assert.Single(result.Beams);
        Assert.Equal(12000, beam.LengthMm, 3);
    }

    [Fact]
    public void Analyze_ParallelLineWithoutAnnotation_DoesNotBecomeCandidate()
    {
        // A wall face running beside a beam satisfies the width and coverage gates on its own.
        // No label claims it, so it must not reach the review as an uncreatable row.
        var result = CadBeamAnalyzer.Analyze(
            Package(
                new[]
                {
                    Segment(1, 0, -100, 10000, -100),
                    Segment(2, 0, 100, 10000, 100),
                    Segment(3, 0, 700, 10000, 700)
                },
                Annotation(100, 5000, 400, "DK1-200x450")),
            new[] { Segment(90, 0, 0, 10000, 0, "GRID") },
            new CadBeamAnalysisOptions());

        var beam = Assert.Single(result.Beams);
        Assert.Equal(200, beam.GeometryWidthMm, 3);
        Assert.Equal("DK1", beam.Mark);
    }

    [Fact]
    public void Analyze_MaximumRunGap_SplitsStretchesBeyondTheLimit()
    {
        var segments = new[]
        {
            Segment(1, 0, -100, 5000, -100),
            Segment(2, 0, 100, 5000, 100),
            Segment(3, 9000, -100, 14000, -100),
            Segment(4, 9000, 100, 14000, 100)
        };
        var annotations = new[]
        {
            Annotation(100, 2500, 300, "DK1-200x450"),
            Annotation(101, 11500, 300, "DK1-200x450")
        };
        var grids = new[] { Segment(90, 0, 0, 14000, 0, "GRID") };

        var joined = CadBeamAnalyzer.Analyze(
            Package(segments, annotations), grids, new CadBeamAnalysisOptions());
        var split = CadBeamAnalyzer.Analyze(
            Package(segments, annotations), grids,
            new CadBeamAnalysisOptions(MaximumRunGapMm: 300));

        Assert.Equal(14000, Assert.Single(joined.Beams).LengthMm, 3);
        Assert.Equal(2, split.Beams.Count);
        Assert.All(split.Beams, beam => Assert.Equal(5000, beam.LengthMm, 3));
    }

    [Theory]
    [InlineData(@"\W0.32;DK1-200x450")]
    [InlineData(@"\pxqc;DK1-200x450")]
    [InlineData(@"{\W0.32;DK1-200x450}")]
    [InlineData(@"\A1;\W0.32;DK1-200x450")]
    [InlineData(@"\H0.7x;DK1-200x450")]
    [InlineData("xt0.32;DK1-200x450")]
    public void Analyze_MTextFormattingCodes_DoNotLeakIntoTheMark(string text)
    {
        var result = CadBeamAnalyzer.Analyze(
            Package(
                new[]
                {
                    Segment(1, 0, -100, 10000, -100),
                    Segment(2, 0, 100, 10000, 100)
                },
                new CadStructureAnnotation(
                    1, new CadStructurePoint2(5000, 400), text, 0, "MText", string.Empty, true)),
            new[] { Segment(90, 0, 0, 10000, 0, "GRID") },
            new CadBeamAnalysisOptions());

        var beam = Assert.Single(result.Beams);
        Assert.Equal("DK1", beam.Mark);
        Assert.Equal(450, beam.TextHeightMm);
    }

    [Fact]
    public void Analyze_BeamCrossedByBranches_KeepsTheBeamAndItsOwnLabel()
    {
        // Branches hanging off a main beam are within reach of the main beam's label. The label
        // is written along the beam it names, which is what tells them apart.
        var segments = new List<CadStructureSegment>
        {
            Segment(1, 0, -100, 12000, -100),
            Segment(2, 0, 100, 12000, 100)
        };
        var annotations = new List<CadStructureAnnotation>
        {
            Annotation(1, 6000, -400, "DK1-200x450")
        };
        var id = 10;
        var annotationId = 100;
        foreach (var x in new[] { 3000.0, 6000.0, 9000.0 })
        {
            segments.Add(Segment(id++, x - 100, 100, x - 100, 4000));
            segments.Add(Segment(id++, x + 100, 100, x + 100, 4000));
            annotations.Add(new CadStructureAnnotation(
                annotationId++, new CadStructurePoint2(x - 400, 2000),
                "DK2-200x300", 90, "TEXT", string.Empty, false));
        }

        var result = CadBeamAnalyzer.Analyze(
            Package(segments, annotations.ToArray()),
            new[] { Segment(900, 0, 0, 12000, 0, "GRID") },
            new CadBeamAnalysisOptions(
                MinimumLineLengthMm: 300, GapJoinToleranceMm: 500, TextSearchDistanceMm: 2000));

        Assert.Equal(4, result.Beams.Count);
        var main = Assert.Single(result.Beams, beam => beam.Mark == "DK1");
        Assert.Equal(12000, main.LengthMm, 3);
        Assert.Equal(450, main.TextHeightMm);
        Assert.Equal(3, result.Beams.Count(beam => beam.Mark == "DK2"));
    }

    [Theory]
    [InlineData("DK1", "-200x450")]
    [InlineData("DK1", "200x450")]
    public void Analyze_LabelSplitAcrossTwoTexts_RecoversTheMark(string name, string section)
    {
        var result = CadBeamAnalyzer.Analyze(
            Package(
                new[]
                {
                    Segment(1, 0, -100, 10000, -100),
                    Segment(2, 0, 100, 10000, 100)
                },
                Annotation(1, 4200, 400, name),
                Annotation(2, 5000, 400, section)),
            new[] { Segment(900, 0, 0, 10000, 0, "GRID") },
            new CadBeamAnalysisOptions(TextSearchDistanceMm: 2000));

        var beam = Assert.Single(result.Beams);
        Assert.Equal("DK1", beam.Mark);
        Assert.Equal(200, beam.TextWidthMm);
        Assert.Equal(450, beam.TextHeightMm);
        Assert.Equal(CadBeamCandidateStatus.Ready, beam.Status);
    }

    private static CadStructureTransferPackage Package(
        IReadOnlyList<CadStructureSegment> segments,
        params CadStructureAnnotation[] annotations) =>
        new CadStructureTransferPackage(
            CadStructureTransferPackage.CurrentSchemaVersion,
            "beam-test",
            DateTime.UtcNow,
            "beam.dwg",
            "2025",
            4,
            default,
            segments)
        {
            Annotations = annotations
        };

    private static CadStructureSegment Segment(
        int id, double x1, double y1, double x2, double y2,
        string layer = "BEAM") =>
        new(id, new CadStructurePoint2(x1, y1), new CadStructurePoint2(x2, y2), layer, string.Empty);

    private static CadStructureAnnotation Annotation(int id, double x, double y, string text) =>
        new(id, new CadStructurePoint2(x, y), text, 0, "TEXT", string.Empty, false);
}
