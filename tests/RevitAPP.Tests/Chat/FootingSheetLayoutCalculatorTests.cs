using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.Chat;

public sealed class FootingSheetLayoutCalculatorTests
{
    [Fact]
    public void Pack_PlacesPlansAboveMatchingSectionsFromLeftToRight()
    {
        var pairs = new[]
        {
            new FootingViewportPairSize(0.7, 0.5, 0.8, 0.6),
            new FootingViewportPairSize(0.9, 0.5, 0.7, 0.6)
        };

        var slots = FootingSheetLayoutCalculator.Pack(0, 0, 4, 3, pairs);

        Assert.Equal(2, slots.Count);
        Assert.True(slots[0].X < slots[1].X);
        Assert.All(slots, slot => Assert.True(slot.PlanY > slot.SectionY));
        Assert.All(slots, slot => Assert.True(slot.X < 4 * (1 - 0.22)));
    }

    [Fact]
    public void Pack_RejectsPairsThatOverflowAvailableWidth()
    {
        var pairs = new[]
        {
            new FootingViewportPairSize(2, .5, 2, .5),
            new FootingViewportPairSize(2, .5, 2, .5)
        };
        Assert.Throws<InvalidOperationException>(() => FootingSheetLayoutCalculator.Pack(0, 0, 4, 3, pairs));
    }

    [Fact]
    public void Pack_RejectsPairsThatOverflowAvailableHeight()
    {
        var pairs = new[] { new FootingViewportPairSize(.5, 1.5, .5, 1.5) };
        Assert.Throws<InvalidOperationException>(() => FootingSheetLayoutCalculator.Pack(0, 0, 4, 3, pairs));
    }

    [Fact]
    public void Pack_ReturnsEmptyForNoPairs()
    {
        Assert.Empty(FootingSheetLayoutCalculator.Pack(0, 0, 4, 3, Array.Empty<FootingViewportPairSize>()));
    }
}
