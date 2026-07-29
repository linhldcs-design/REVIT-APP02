using RevitAPP.Core.Chat.BeamLongitudinalDrawing;
using Xunit;

namespace RevitAPP.Tests;

public sealed class LongitudinalChatPlannerTests
{
    [Fact]
    public void Plan_AssignsInInputOrder()
    {
        var result = LongitudinalBatchAssignmentPlanner.Plan([11, 22, 33], 2, ["S1", "S2"]);
        Assert.Equal([(11L, "S1"), (22L, "S1"), (33L, "S2")],
            result.Select(item => (item.BeamId, item.SheetNumber)));
    }

    [Fact]
    public void Plan_RejectsInsufficientCapacity() =>
        Assert.Throws<ArgumentException>(() =>
            LongitudinalBatchAssignmentPlanner.Plan([1, 2, 3], 2, ["S1"]));

    [Fact]
    public void Plan_RejectsDuplicateBeamIds() =>
        Assert.Throws<ArgumentException>(() =>
            LongitudinalBatchAssignmentPlanner.Plan([1, 1], 2, ["S1"]));

    [Fact]
    public void Plan_RejectsDuplicateSheetNumbersIgnoringCase() =>
        Assert.Throws<ArgumentException>(() =>
            LongitudinalBatchAssignmentPlanner.Plan([1, 2], 1, ["S1", "s1"]));

    [Fact]
    public void SelectNearestMidpoint_UsesGridTowardStartOnTie()
    {
        var result = LongitudinalGridStationPlanner.SelectNearestMidpoint(10, [4, 6], 0.01);
        Assert.Equal(4, result);
    }

    [Fact]
    public void SelectNearestMidpoint_RejectsEndpointOnlyGrids() =>
        Assert.Throws<InvalidOperationException>(() =>
            LongitudinalGridStationPlanner.SelectNearestMidpoint(10, [0, 10], 0.01));

    [Fact]
    public void CropPlanner_CapsOverlapAtAvailableExtent()
    {
        var result = LongitudinalDependentCropPlanner.Plan(0, 10, 1, 4);
        Assert.Equal(new LongitudinalCropRange(0, 3), result.First);
        Assert.Equal(new LongitudinalCropRange(0, 10), result.Second);
    }

    [Fact]
    public void CropPlanner_CreatesSymmetricRequestedOverlap()
    {
        var result = LongitudinalDependentCropPlanner.Plan(0, 10, 5, 2);
        Assert.Equal(new LongitudinalCropRange(0, 6), result.First);
        Assert.Equal(new LongitudinalCropRange(4, 10), result.Second);
    }

    [Fact]
    public void CropAndBreakPlanners_PreserveFullDistanceOnAvailableSide()
    {
        var ranges = LongitudinalDependentCropPlanner.Plan(0, 10, 1, 4);
        var breaks = LongitudinalBreakLinePlanner.Plan(1, ranges.First, ranges.Second, 1.5, 0.5);
        Assert.Equal(new LongitudinalBreakLinePositions(2.5, 0.5), breaks);
    }

    [Fact]
    public void BreakPlanner_KeepsBothLinesOnTheirOwnSideOfGrid()
    {
        var ranges = LongitudinalDependentCropPlanner.Plan(0, 10, 0.1, 4);
        var breaks = LongitudinalBreakLinePlanner.Plan(0.1, ranges.First, ranges.Second, 1.5, 0.5);
        Assert.True(breaks.First > 0.1);
        Assert.InRange(breaks.First, 0.1, ranges.First.Maximum);
        Assert.True(breaks.Second < 0.1);
        Assert.InRange(breaks.Second, ranges.Second.Minimum, 0.1);
    }

    [Fact]
    public void TitleOffset_CentersAndMovesBelowView()
    {
        var result = LongitudinalViewportTitleOffsetPlanner.CenterBelowView(
            0, 10, 4, 2, 6, 3, 1, 2, 0.5);
        Assert.Equal(2, result.X);
        Assert.Equal(2.5, result.Y);
    }

    [Fact]
    public void TitleOffset_RejectsNegativeGap() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LongitudinalViewportTitleOffsetPlanner.CenterBelowView(
                0, 10, 4, 2, 6, 3, 1, 2, -0.5));

    [Fact]
    public void VerticalMargin_KeepsPreferredMarginWhenContentFits()
    {
        var margin = LongitudinalVerticalMarginPlanner.Select(420, 400, 2, 0.5);
        Assert.Equal(2, margin);
    }

    [Fact]
    public void VerticalMargin_ShrinksEvenlyForNearFit()
    {
        var margin = LongitudinalVerticalMarginPlanner.Select(420, 417.7, 2, 0.5);
        Assert.Equal(1.15, margin, 6);
    }

    [Fact]
    public void VerticalMargin_RejectsContentBeyondMinimumMargins() =>
        Assert.Throws<InvalidOperationException>(() =>
            LongitudinalVerticalMarginPlanner.Select(420, 419.1, 2, 0.5));
}
