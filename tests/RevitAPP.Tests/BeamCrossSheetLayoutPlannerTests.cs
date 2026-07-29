using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests;

public sealed class BeamCrossSheetLayoutPlannerTests
{
    [Fact]
    public void Plan_ReturnsPlacementsOnlyForNewViewportIds()
    {
        var occupied = new[]
        {
            new BeamSheetRect(0.55, 0.75, 1.05, 1.05)
        };
        var groups = new[]
        {
            Group(101, 102),
            Group(201, 202)
        };

        var placements = BeamCrossSheetLayoutPlanner.Plan(0, 1.6, 1.3, 0, groups, occupied);

        Assert.Equal(new long[] { 101, 102, 201, 202 },
            placements.Select(placement => placement.ViewportId).OrderBy(id => id));
        Assert.DoesNotContain(1, placements.Select(placement => placement.ViewportId));
        Assert.DoesNotContain(2, placements.Select(placement => placement.ViewportId));
    }

    [Fact]
    public void Plan_SkipsOccupiedSlotWithoutMovingExistingContent()
    {
        var centralSlot = new BeamSheetRect(0.08, 1.15, 0.50, 1.30);
        var placements = BeamCrossSheetLayoutPlanner.Plan(
            0, 1.6, 1.3, 0,
            new[] { Group(101, 102) },
            new[] { centralSlot });

        Assert.Equal(2, placements.Count);
        var targets = placements.Select(placement =>
            BeamSheetRect.Centered(placement.BoxCenterX, placement.BoxCenterY, 0.08, 0.08));
        Assert.All(targets, target => Assert.False(target.Intersects(centralSlot)));
    }

    [Fact]
    public void Plan_ThrowsWhenNoFreeSlotRemains()
    {
        var occupied = new[] { new BeamSheetRect(0, 0, 1.6, 1.3) };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BeamCrossSheetLayoutPlanner.Plan(
                0, 1.6, 1.3, 0,
                new[] { Group(101, 102) },
                occupied));

        Assert.Contains("Không còn vùng trống", exception.Message);
    }

    [Fact]
    public void Plan_ExpandsPairSpacingForWideViewportTitles()
    {
        var group = new BeamCrossViewportGroup(new[]
        {
            new BeamCrossViewportFootprint(101, 0.30, 0.08, 0, 0),
            new BeamCrossViewportFootprint(102, 0.30, 0.08, 0, 0)
        });

        var placements = BeamCrossSheetLayoutPlanner.Plan(
            0, 1.6, 1.3, 0, new[] { group }, Array.Empty<BeamSheetRect>());
        var first = BeamSheetRect.Centered(
            placements[0].BoxCenterX, placements[0].BoxCenterY, 0.30, 0.08);
        var second = BeamSheetRect.Centered(
            placements[1].BoxCenterX, placements[1].BoxCenterY, 0.30, 0.08);

        Assert.False(first.Intersects(second));
    }

    [Fact]
    public void Rectangles_RequireAtLeastOneMillimeterClearance()
    {
        var oneMillimeterFeet = 1.0 / 304.8;
        var existing = new BeamSheetRect(0, 0, 1, 1);
        var onlyHalfMillimeterAway = new BeamSheetRect(
            1 + oneMillimeterFeet / 2.0, 0, 2, 1);

        Assert.True(existing.Intersects(onlyHalfMillimeterAway));
    }

    private static BeamCrossViewportGroup Group(long firstId, long secondId) =>
        new(new[]
        {
            new BeamCrossViewportFootprint(firstId, 0.08, 0.08, 0, 0),
            new BeamCrossViewportFootprint(secondId, 0.08, 0.08, 0, 0)
        });
}
