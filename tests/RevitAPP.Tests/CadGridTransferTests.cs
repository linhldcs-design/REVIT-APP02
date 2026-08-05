using RevitAPP.Core.Models.CadGrid;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class CadGridTransferTests
{
    [Fact]
    public void RelativeLayout_RectangularNetwork_StartsAtLowerLeftCorner()
    {
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 0, 6000),
            new CadGridTransferLine(2, 3000, 0, 3000, 6000),
            new CadGridTransferLine(3, 7500, 0, 7500, 6000),
            new CadGridTransferLine(10, 0, 0, 7500, 0),
            new CadGridTransferLine(11, 0, 4000, 7500, 4000),
            new CadGridTransferLine(12, 0, 6000, 7500, 6000));

        var result = CadGridRelativeLayoutAnalyzer.Analyze(package);

        Assert.True(result.IsValid, result.Error);
        var layout = Assert.IsType<CadGridRelativeLayout>(result.Layout);
        var vertical = FamilyContaining(layout, 1);
        var horizontal = FamilyContaining(layout, 10);
        Assert.Equal(new[] { 0d, 3000d, 7500d }, vertical.OffsetsMm);
        Assert.Equal(new[] { 0d, 4000d, 6000d }, horizontal.OffsetsMm);
    }

    [Theory]
    [InlineData(1, 25.4)]
    [InlineData(2, 304.8)]
    [InlineData(4, 1.0)]
    [InlineData(5, 10.0)]
    [InlineData(6, 1000.0)]
    public void MillimetresPerDrawingUnit_KnownInsUnits_ReturnsScale(
        int insUnits,
        double expected)
    {
        Assert.Equal(expected, CadGridUnitConverter.MillimetresPerDrawingUnit(insUnits), 6);
    }

    [Fact]
    public void Store_RoundTrip_PreservesPackage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var package = Package(
                new CadGridTransferLine(1, 0, 0, 0, 1000),
                new CadGridTransferLine(2, 0, 0, 1000, 0));

            CadGridTransferStore.WriteAtomic(package, path);
            var restored = CadGridTransferStore.ReadLatest(path, TimeSpan.FromHours(1));

            Assert.Equal(package.SelectionId, restored.SelectionId);
            Assert.Equal(package.Lines, restored.Lines);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Store_StalePackage_IsRejected()
    {
        var package = Package(
            DateTime.UtcNow.AddHours(-1),
            new CadGridTransferLine(1, 0, 0, 0, 1000),
            new CadGridTransferLine(2, 0, 0, 1000, 0));

        var exception = Assert.Throws<InvalidDataException>(
            () => CadGridTransferStore.Validate(package, TimeSpan.FromMinutes(30)));

        Assert.Contains("30", exception.Message);
    }

    [Fact]
    public void RelativeLayout_ReversedEndpoints_ProducesSameOffsets()
    {
        var forward = Package(
            new CadGridTransferLine(1, 0, 0, 0, 6000),
            new CadGridTransferLine(2, 3000, 0, 3000, 6000),
            new CadGridTransferLine(10, 0, 0, 3000, 0),
            new CadGridTransferLine(11, 0, 4000, 3000, 4000));

        // Same lines, every endpoint pair swapped: a DWG line drawn right-to-left is
        // the same grid line and must not change the spacing chain.
        var reversed = Package(
            new CadGridTransferLine(1, 0, 6000, 0, 0),
            new CadGridTransferLine(2, 3000, 6000, 3000, 0),
            new CadGridTransferLine(10, 3000, 0, 0, 0),
            new CadGridTransferLine(11, 3000, 4000, 0, 4000));

        var forwardResult = CadGridRelativeLayoutAnalyzer.Analyze(forward);
        var reversedResult = CadGridRelativeLayoutAnalyzer.Analyze(reversed);

        Assert.True(forwardResult.IsValid, forwardResult.Error);
        Assert.True(reversedResult.IsValid, reversedResult.Error);
        Assert.Equal(
            FamilyContaining(forwardResult.Layout!, 1).OffsetsMm,
            FamilyContaining(reversedResult.Layout!, 1).OffsetsMm);
        Assert.Equal(
            FamilyContaining(forwardResult.Layout!, 10).OffsetsMm,
            FamilyContaining(reversedResult.Layout!, 10).OffsetsMm);
    }

    [Fact]
    public void RelativeLayout_UnevenSpacing_PreservesChainOrder()
    {
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 0, 9000),
            new CadGridTransferLine(2, 2200, 0, 2200, 9000),
            new CadGridTransferLine(3, 8700, 0, 8700, 9000),
            new CadGridTransferLine(10, 0, 0, 8700, 0),
            new CadGridTransferLine(11, 0, 1500, 8700, 1500));

        var result = CadGridRelativeLayoutAnalyzer.Analyze(package);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(
            new[] { 0d, 2200d, 8700d },
            FamilyContaining(result.Layout!, 1).OffsetsMm);
    }

    [Fact]
    public void RelativeLayout_InchesDrawing_ConvertsSpacingToMillimetres()
    {
        // InsUnits 1 = inches, so a 100-unit spacing is 2540 mm.
        var package = Package(
            DateTime.UtcNow,
            insUnits: 1,
            new CadGridTransferLine(1, 0, 0, 0, 200),
            new CadGridTransferLine(2, 100, 0, 100, 200),
            new CadGridTransferLine(10, 0, 0, 100, 0),
            new CadGridTransferLine(11, 0, 200, 100, 200));

        var result = CadGridRelativeLayoutAnalyzer.Analyze(package);

        Assert.True(result.IsValid, result.Error);
        var vertical = FamilyContaining(result.Layout!, 1);
        Assert.Equal(2540d, vertical.OffsetsMm[1], 3);
    }

    [Fact]
    public void Store_UnsupportedSchema_IsRejected()
    {
        var package = new CadGridTransferPackage(
            CadGridTransferPackage.CurrentSchemaVersion + 1,
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            "sample.dwg",
            "2025",
            4,
            new[]
            {
                new CadGridTransferLine(1, 0, 0, 0, 1000),
                new CadGridTransferLine(2, 0, 0, 1000, 0)
            });

        Assert.Throws<InvalidDataException>(
            () => CadGridTransferStore.Validate(package, TimeSpan.MaxValue));
    }

    [Fact]
    public void Store_NonFiniteCoordinate_IsRejected()
    {
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 0, double.NaN),
            new CadGridTransferLine(2, 0, 0, 1000, 0));

        Assert.Throws<InvalidDataException>(
            () => CadGridTransferStore.Validate(package, TimeSpan.MaxValue));
    }

    [Fact]
    public void Store_TooFewLines_IsRejected()
    {
        var package = Package(new CadGridTransferLine(1, 0, 0, 0, 1000));

        Assert.Throws<InvalidDataException>(
            () => CadGridTransferStore.Validate(package, TimeSpan.MaxValue));
    }

    [Fact]
    public void Store_CorruptJson_ThrowsInvalidData()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{ not valid json");
            Assert.Throws<InvalidDataException>(
                () => CadGridTransferStore.ReadLatest(path, TimeSpan.MaxValue));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Store_MissingFile_ThrowsFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

        Assert.Throws<FileNotFoundException>(
            () => CadGridTransferStore.ReadLatest(path, TimeSpan.MaxValue));
    }

    [Theory]
    [InlineData(0, 1, 1, 0, 90)]
    [InlineData(0, 1, 0, -1, 0)]
    [InlineData(1, 0, -1, 0, 0)]
    [InlineData(1, 0, 1, 1, 45)]
    public void AngleBetween_IsUndirected(
        double leftX,
        double leftY,
        double rightX,
        double rightY,
        double expected)
    {
        Assert.Equal(
            expected,
            CadGridFamilyAssigner.AngleBetweenDegrees(
                new CadGridPoint2(leftX, leftY),
                new CadGridPoint2(rightX, rightY)),
            6);
    }

    [Fact]
    public void RelativeLayout_FamilyOffsets_MeasureSpacingWithinThatFamily()
    {
        // Asymmetric on purpose: equal spacing chains would hide a swapped pairing.
        // Vertical lines (ids 1..3) sit at x = 0, 6000, 18000, so the family whose
        // members are those vertical lines must carry {0, 6000, 18000}.
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 0, 8000),
            new CadGridTransferLine(2, 6000, 0, 6000, 8000),
            new CadGridTransferLine(3, 18000, 0, 18000, 8000),
            new CadGridTransferLine(10, 0, 0, 18000, 0),
            new CadGridTransferLine(11, 0, 4000, 18000, 4000),
            new CadGridTransferLine(12, 0, 8000, 18000, 8000));

        var result = CadGridRelativeLayoutAnalyzer.Analyze(package);

        Assert.True(result.IsValid, result.Error);
        var verticalFamily = FamilyContaining(result.Layout!, 1);
        var horizontalFamily = FamilyContaining(result.Layout!, 10);

        Assert.Equal(new[] { 0d, 6000d, 18000d }, verticalFamily.OffsetsMm);
        Assert.Equal(new[] { 0d, 4000d, 8000d }, horizontalFamily.OffsetsMm);

        // The family's Direction must be parallel to its own members: the vertical
        // family runs along Y. This is what ties an anchor grid to the correct chain.
        Assert.Equal(
            0d,
            CadGridFamilyAssigner.AngleBetweenDegrees(
                verticalFamily.Direction,
                new CadGridPoint2(0, 1)),
            6);
        Assert.Equal(
            0d,
            CadGridFamilyAssigner.AngleBetweenDegrees(
                horizontalFamily.Direction,
                new CadGridPoint2(1, 0)),
            6);
    }

    [Fact]
    public void Assign_AnchorsInSameOrderAsCad_IsNotSwapped()
    {
        var assignment = CadGridFamilyAssigner.Assign(
            new CadGridPoint2(0, 1),
            new CadGridPoint2(1, 0),
            new CadGridPoint2(0, 1),
            new CadGridPoint2(1, 0));

        Assert.False(assignment.IsSwapped);
        Assert.False(assignment.IsAmbiguous);
    }

    [Fact]
    public void Assign_AnchorsPickedInOppositeOrder_IsSwapped()
    {
        var assignment = CadGridFamilyAssigner.Assign(
            new CadGridPoint2(0, 1),
            new CadGridPoint2(1, 0),
            new CadGridPoint2(1, 0),
            new CadGridPoint2(0, 1));

        Assert.True(assignment.IsSwapped);
        Assert.False(assignment.IsAmbiguous);
    }

    [Fact]
    public void Assign_SkewedGrid_MatchesNearestDirection()
    {
        // CAD families 30° apart from orthogonal; anchors follow the same skew.
        var assignment = CadGridFamilyAssigner.Assign(
            new CadGridPoint2(Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6)),
            new CadGridPoint2(-Math.Sin(Math.PI / 6), Math.Cos(Math.PI / 6)),
            new CadGridPoint2(Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6)),
            new CadGridPoint2(-Math.Sin(Math.PI / 6), Math.Cos(Math.PI / 6)));

        Assert.False(assignment.IsSwapped);
        Assert.False(assignment.IsAmbiguous);
    }

    [Fact]
    public void Assign_NearlyParallelFamilies_IsAmbiguous()
    {
        // Both anchors sit almost midway between the two CAD directions, so neither
        // assignment is defensible and the caller must ask instead of guessing.
        var assignment = CadGridFamilyAssigner.Assign(
            new CadGridPoint2(1, 0),
            new CadGridPoint2(0, 1),
            new CadGridPoint2(1, 1),
            new CadGridPoint2(1, -1));

        Assert.True(assignment.IsAmbiguous);
    }

    [Fact]
    public void Names_NumericAnchor_ContinuesNumbering()
    {
        Assert.Equal(
            new[] { "2", "3", "4" },
            CadGridNameSequencer.Following("1", 3));
    }

    [Fact]
    public void Names_LetterAnchor_ContinuesAlphabet()
    {
        Assert.Equal(
            new[] { "B", "C", "D" },
            CadGridNameSequencer.Following("A", 3));
    }

    [Fact]
    public void Names_LetterAnchorAtZ_RollsOverToDoubleLetters()
    {
        Assert.Equal(
            new[] { "AA", "AB" },
            CadGridNameSequencer.Following("Z", 2));
    }

    [Fact]
    public void Names_MidSequenceAnchor_ContinuesFromThatAnchor()
    {
        Assert.Equal(new[] { "D", "E" }, CadGridNameSequencer.Following("C", 2));
        Assert.Equal(new[] { "6", "7" }, CadGridNameSequencer.Following("5", 2));
    }

    [Fact]
    public void Names_ZeroPaddedAnchor_PreservesWidth()
    {
        Assert.Equal(
            new[] { "09", "10" },
            CadGridNameSequencer.Following("08", 2));
    }

    [Fact]
    public void Names_PrefixedNumericAnchor_KeepsPrefix()
    {
        Assert.Equal(
            new[] { "X-2", "X-3" },
            CadGridNameSequencer.Following("X-1", 2));
    }

    [Fact]
    public void Names_UnrecognisedAnchor_ReturnsNull()
    {
        // No sequence to continue: better to let Revit name these than to guess.
        Assert.Null(CadGridNameSequencer.Following("Trục chính", 2));
        Assert.Null(CadGridNameSequencer.Following("", 2));
    }

    [Fact]
    public void Names_ZeroCount_ReturnsEmpty()
    {
        Assert.Empty(CadGridNameSequencer.Following("A", 0)!);
    }

    [Fact]
    public void Span_GrowsTowardAdvanceEnd_KeepingLineInPlace()
    {
        // Horizontal line 0..1000, crossing grids advance toward the far end, reach 5000.
        var (start, end) = CadGridSpanCalculator.Span(
            new CadGridPoint2(0, 0),
            new CadGridPoint2(1000, 0),
            advanceTowardEnd: true,
            reach: 5000,
            margin: 500);

        // Trailing end backs off by the margin only; leading end reaches past the network.
        Assert.Equal(-500, start.Xmm, 6);
        Assert.Equal(5500, end.Xmm, 6);
        // Never moves sideways.
        Assert.Equal(0, start.Ymm, 6);
        Assert.Equal(0, end.Ymm, 6);
    }

    [Fact]
    public void Span_AdvanceTowardStart_GrowsTheOtherEnd()
    {
        var (start, end) = CadGridSpanCalculator.Span(
            new CadGridPoint2(0, 0),
            new CadGridPoint2(1000, 0),
            advanceTowardEnd: false,
            reach: 5000,
            margin: 500);

        // Trailing end here is the far end (1000), so the span runs back from it.
        Assert.Equal(-4500, start.Xmm, 6);
        Assert.Equal(1500, end.Xmm, 6);
    }

    [Fact]
    public void Span_ShortAnchor_ReachesFullNetwork()
    {
        // The anchor the user drew is far shorter than the network it must now bound.
        var (start, end) = CadGridSpanCalculator.Span(
            new CadGridPoint2(0, 0),
            new CadGridPoint2(1000, 0),
            advanceTowardEnd: true,
            reach: 18000,
            margin: 500);

        Assert.Equal(-500, start.Xmm, 6);
        Assert.Equal(18500, end.Xmm, 6);
    }

    [Fact]
    public void Span_LineAlreadyLongEnough_IsNotShortened()
    {
        var (start, end) = CadGridSpanCalculator.Span(
            new CadGridPoint2(0, 0),
            new CadGridPoint2(20000, 0),
            advanceTowardEnd: true,
            reach: 5000,
            margin: 500);

        // The far end must not be cut back just because the network is shorter.
        Assert.Equal(20500, end.Xmm, 6);
        Assert.Equal(-500, start.Xmm, 6);
    }

    [Fact]
    public void Span_SkewedLine_StaysOnItsOwnAxis()
    {
        // 3-4-5 triangle: direction (0.6, 0.8), length 1000.
        var (start, end) = CadGridSpanCalculator.Span(
            new CadGridPoint2(0, 0),
            new CadGridPoint2(600, 800),
            advanceTowardEnd: true,
            reach: 2000,
            margin: 0);

        // Endpoints stay on the original axis: the skewed line must not be rotated
        // toward a global axis or shifted off its own line.
        Assert.Equal(0, start.Xmm, 6);
        Assert.Equal(0, start.Ymm, 6);
        Assert.Equal(1200, end.Xmm, 6);
        Assert.Equal(1600, end.Ymm, 6);
        Assert.Equal(2000, start.DistanceTo(end), 6);
    }

    [Fact]
    public void Span_ZeroLengthSegment_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CadGridSpanCalculator.Span(
                new CadGridPoint2(5, 5),
                new CadGridPoint2(5, 5),
                advanceTowardEnd: true,
                reach: 1000,
                margin: 0));
    }

    [Fact]
    public void Direct_DiagonalLine_IsPlacedNotRejected()
    {
        // The network analyzer rejects a third direction; direct placement must not.
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 0, 6000),
            new CadGridTransferLine(2, 6000, 0, 6000, 6000),
            new CadGridTransferLine(3, 0, 0, 6000, 0),
            new CadGridTransferLine(99, 0, 0, 6000, 6000));

        var result = CadGridDirectPlacer.Place(package);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(4, result.Lines.Count);
        Assert.Contains(result.Lines, line => line.Id == 99);
    }

    [Fact]
    public void Direct_OriginIsShiftedToLowerLeftCorner()
    {
        // Drawn far from the WCS origin; placement must not depend on absolute coords.
        var package = Package(
            new CadGridTransferLine(1, 500000, 250000, 500000, 256000),
            new CadGridTransferLine(2, 500000, 250000, 506000, 250000));

        var result = CadGridDirectPlacer.Place(package);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(0, result.Lines.Min(line => Math.Min(line.Start.Xmm, line.End.Xmm)), 6);
        Assert.Equal(0, result.Lines.Min(line => Math.Min(line.Start.Ymm, line.End.Ymm)), 6);
    }

    [Fact]
    public void Direct_PreservesLengthAndAngle()
    {
        var package = Package(
            new CadGridTransferLine(1, 1000, 1000, 4000, 5000),
            new CadGridTransferLine(2, 1000, 1000, 5000, 1000));

        var result = CadGridDirectPlacer.Place(package);

        Assert.True(result.IsValid, result.Error);
        var diagonal = result.Lines.Single(line => line.Id == 1);
        // 3-4-5 triangle scaled: length must survive the shift untouched.
        Assert.Equal(5000, diagonal.Start.DistanceTo(diagonal.End), 6);
    }

    [Fact]
    public void Direct_SkipsZeroLengthLines()
    {
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 6000, 0),
            new CadGridTransferLine(2, 0, 0, 0, 6000),
            new CadGridTransferLine(3, 2000, 2000, 2000, 2000));

        var result = CadGridDirectPlacer.Place(package);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(2, result.Lines.Count);
        Assert.Contains(3, result.SkippedIds);
    }

    [Fact]
    public void Direct_AppliesUnitConversion()
    {
        // Inches: a 100-unit line is 2540 mm long.
        var package = Package(
            DateTime.UtcNow,
            insUnits: 1,
            new CadGridTransferLine(1, 0, 0, 100, 0),
            new CadGridTransferLine(2, 0, 0, 0, 100));

        var result = CadGridDirectPlacer.Place(package);

        Assert.True(result.IsValid, result.Error);
        var horizontal = result.Lines.Single(line => line.Id == 1);
        Assert.Equal(2540, horizontal.Start.DistanceTo(horizontal.End), 3);
    }

    [Fact]
    public void Preview_LabelsFamiliesAndSkewSeparately()
    {
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 0, 6000),
            new CadGridTransferLine(2, 3000, 0, 3000, 6000),
            new CadGridTransferLine(10, 0, 0, 3000, 0),
            new CadGridTransferLine(11, 0, 6000, 3000, 6000),
            new CadGridTransferLine(99, 0, 0, 3000, 6000));

        var preview = CadGridPreviewBuilder.Build(package);

        Assert.True(preview.IsValid, preview.Error);
        Assert.Equal(5, preview.Axes.Count);

        var diagonal = preview.Axes.Single(axis => axis.Id == 99);
        Assert.Equal(CadGridAxisKind.Skew, diagonal.Kind);
        Assert.Equal(4, preview.Axes.Count(axis => axis.Kind == CadGridAxisKind.Family));
    }

    [Fact]
    public void Preview_NamesNumericAndLetterFamilies()
    {
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 0, 6000),
            new CadGridTransferLine(2, 3000, 0, 3000, 6000),
            new CadGridTransferLine(10, 0, 0, 3000, 0),
            new CadGridTransferLine(11, 0, 6000, 3000, 6000));

        var preview = CadGridPreviewBuilder.Build(package);

        Assert.True(preview.IsValid, preview.Error);
        var names = preview.Axes.Select(axis => axis.SuggestedName).ToArray();
        // One family numbers, the other letters; both start at the first member.
        Assert.Contains("1", names);
        Assert.Contains("A", names);
    }

    [Fact]
    public void Preview_NumbersVerticalAxesLeftToRight()
    {
        // Deliberately listed right-to-left: naming must follow position, not file order.
        var package = Package(
            new CadGridTransferLine(1, 9000, 0, 9000, 6000),
            new CadGridTransferLine(2, 6000, 0, 6000, 6000),
            new CadGridTransferLine(3, 0, 0, 0, 6000),
            new CadGridTransferLine(10, 0, 0, 9000, 0),
            new CadGridTransferLine(11, 0, 6000, 9000, 6000));

        var preview = CadGridPreviewBuilder.Build(package);

        var vertical = preview.Axes
            .Where(axis => Math.Abs(axis.AngleDegrees - 90) < 1)
            .OrderBy(axis => axis.Start.Xmm)
            .Select(axis => axis.SuggestedName)
            .ToArray();

        Assert.Equal(new[] { "1", "2", "3" }, vertical);
    }

    [Fact]
    public void Preview_LettersHorizontalAxesBottomToTop()
    {
        // Listed top-down; the lowest axis must still become "A".
        var package = Package(
            new CadGridTransferLine(10, 0, 8000, 9000, 8000),
            new CadGridTransferLine(11, 0, 4000, 9000, 4000),
            new CadGridTransferLine(12, 0, 0, 9000, 0),
            new CadGridTransferLine(1, 0, 0, 0, 8000),
            new CadGridTransferLine(2, 9000, 0, 9000, 8000));

        var preview = CadGridPreviewBuilder.Build(package);

        var horizontal = preview.Axes
            .Where(axis => axis.AngleDegrees < 1)
            .OrderBy(axis => axis.Start.Ymm)
            .Select(axis => axis.SuggestedName)
            .ToArray();

        Assert.Equal(new[] { "A", "B", "C" }, horizontal);
    }

    [Fact]
    public void Preview_ReportsAngleUndirected()
    {
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 1000, 0),
            new CadGridTransferLine(2, 1000, 1000, 0, 1000));

        var preview = CadGridPreviewBuilder.Build(package);

        // A line drawn right-to-left is still horizontal, not 180°.
        Assert.All(preview.Axes, axis => Assert.Equal(0, axis.AngleDegrees, 6));
    }

    [Fact]
    public void Preview_ExposesExtentsForZoomToFit()
    {
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 0, 8000),
            new CadGridTransferLine(2, 12000, 0, 12000, 8000),
            new CadGridTransferLine(10, 0, 0, 12000, 0));

        var preview = CadGridPreviewBuilder.Build(package);

        Assert.Equal(12000, preview.WidthMm, 6);
        Assert.Equal(8000, preview.HeightMm, 6);
    }

    [Fact]
    public void Preview_OnlyDiagonals_StillProducesAxes()
    {
        // No two-family network at all; the user must still be able to create these.
        var package = Package(
            new CadGridTransferLine(1, 0, 0, 3000, 6000),
            new CadGridTransferLine(2, 0, 6000, 4000, 0));

        var preview = CadGridPreviewBuilder.Build(package);

        Assert.True(preview.IsValid, preview.Error);
        Assert.Equal(2, preview.Axes.Count);
        Assert.All(preview.Axes, axis => Assert.Equal(CadGridAxisKind.Skew, axis.Kind));
    }

    [Fact]
    public void Extent_ShortGrid_GrowsToCoverCrossingGrids()
    {
        // Existing grid runs x 0..1000; new grids span x 0..20000 across it.
        var extended = CadGridExtentCalculator.Extend(
            new CadGridPoint2(0, 0),
            new CadGridPoint2(1000, 0),
            new[] { new CadGridPoint2(0, -5000), new CadGridPoint2(20000, 5000) },
            margin: 500);

        Assert.NotNull(extended);
        Assert.Equal(-500, extended!.Value.Start.Xmm, 6);
        Assert.Equal(20500, extended.Value.End.Xmm, 6);
        // Must stay on its own line.
        Assert.Equal(0, extended.Value.Start.Ymm, 6);
        Assert.Equal(0, extended.Value.End.Ymm, 6);
    }

    [Fact]
    public void Extent_AlreadyLongEnough_ReturnsNull()
    {
        var extended = CadGridExtentCalculator.Extend(
            new CadGridPoint2(-9000, 0),
            new CadGridPoint2(9000, 0),
            new[] { new CadGridPoint2(0, 0), new CadGridPoint2(3000, 0) },
            margin: 500);

        // Nothing to do: the grid must not be shortened to match the network.
        Assert.Null(extended);
    }

    [Fact]
    public void Extent_OffsetGrid_KeepsItsPerpendicularPosition()
    {
        // A horizontal grid sitting at y = 7000 must stay at y = 7000.
        var extended = CadGridExtentCalculator.Extend(
            new CadGridPoint2(0, 7000),
            new CadGridPoint2(1000, 7000),
            new[] { new CadGridPoint2(0, 0), new CadGridPoint2(15000, 0) },
            margin: 0);

        Assert.NotNull(extended);
        Assert.Equal(7000, extended!.Value.Start.Ymm, 6);
        Assert.Equal(7000, extended.Value.End.Ymm, 6);
        Assert.Equal(0, extended.Value.Start.Xmm, 6);
        Assert.Equal(15000, extended.Value.End.Xmm, 6);
    }

    [Fact]
    public void Extent_SkewedGrid_StaysOnItsOwnAxis()
    {
        // Direction (0.6, 0.8) through the origin; extending must not rotate it.
        var extended = CadGridExtentCalculator.Extend(
            new CadGridPoint2(0, 0),
            new CadGridPoint2(600, 800),
            new[] { new CadGridPoint2(1800, 2400) },
            margin: 0);

        Assert.NotNull(extended);
        // End point must remain collinear: 3000 along (0.6, 0.8) is (1800, 2400).
        Assert.Equal(1800, extended!.Value.End.Xmm, 6);
        Assert.Equal(2400, extended.Value.End.Ymm, 6);
    }

    [Fact]
    public void Extent_NoCrossingGrids_ReturnsNull()
    {
        Assert.Null(
            CadGridExtentCalculator.Extend(
                new CadGridPoint2(0, 0),
                new CadGridPoint2(1000, 0),
                Array.Empty<CadGridPoint2>(),
                margin: 500));
    }

    [Fact]
    public void Extent_ReversedEndpoints_ProducesSameSegment()
    {
        var crossing = new[] { new CadGridPoint2(0, 0), new CadGridPoint2(9000, 0) };

        var forward = CadGridExtentCalculator.Extend(
            new CadGridPoint2(0, 0), new CadGridPoint2(1000, 0), crossing, 0);
        var reversed = CadGridExtentCalculator.Extend(
            new CadGridPoint2(1000, 0), new CadGridPoint2(0, 0), crossing, 0);

        Assert.NotNull(forward);
        Assert.NotNull(reversed);
        // Endpoint order in the model must not change where the grid ends up.
        var forwardSpan = Math.Abs(forward!.Value.End.Xmm - forward.Value.Start.Xmm);
        var reversedSpan = Math.Abs(reversed!.Value.End.Xmm - reversed.Value.Start.Xmm);
        Assert.Equal(forwardSpan, reversedSpan, 6);
    }

    private static CadGridRelativeFamily FamilyContaining(
        CadGridRelativeLayout layout,
        int segmentId) =>
        layout.FirstFamily.OrderedSegmentIds.Contains(segmentId)
            ? layout.FirstFamily
            : layout.SecondFamily;

    private static CadGridTransferPackage Package(params CadGridTransferLine[] lines) =>
        Package(DateTime.UtcNow, lines);

    private static CadGridTransferPackage Package(
        DateTime createdUtc,
        params CadGridTransferLine[] lines) =>
        Package(createdUtc, 4, lines);

    private static CadGridTransferPackage Package(
        DateTime createdUtc,
        int insUnits,
        params CadGridTransferLine[] lines) =>
        new(
            CadGridTransferPackage.CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            createdUtc,
            "sample.dwg",
            "2025",
            insUnits,
            lines);
}
