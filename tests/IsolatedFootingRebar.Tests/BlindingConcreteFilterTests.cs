using IsolatedFootingRebar.Services;

namespace IsolatedFootingRebar.Tests;

public sealed class BlindingConcreteFilterTests
{
    [Theory]
    [InlineData("Bê tông lót")]
    [InlineData("BT LÓT M100")]
    [InlineData("Concrete - Blinding")]
    [InlineData("Lean Concrete 10MPa")]
    public void IsBlindingName_RecognizesCommonNames(string name)
        => Assert.True(BlindingConcreteFilter.IsBlindingName(name));

    [Theory]
    [InlineData("Concrete")]
    [InlineData("Bê tông móng")]
    [InlineData("Structural Foundation")]
    public void IsBlindingName_DoesNotMatchStructuralConcrete(string name)
        => Assert.False(BlindingConcreteFilter.IsBlindingName(name));

    [Fact]
    public void LooksLikeBlindingSlab_AcceptsThinWideBottomSlab()
        => Assert.True(BlindingConcreteFilter.LooksLikeBlindingSlab(
            0, 100, 2100, 2100,
            100, 500, 2000, 2000,
            150, 20, 100));

    [Fact]
    public void LooksLikeBlindingSlab_RejectsThickStructuralBase()
        => Assert.False(BlindingConcreteFilter.LooksLikeBlindingSlab(
            0, 300, 2200, 2200,
            300, 900, 2000, 2000,
            150, 20, 100));

    [Fact]
    public void LooksLikeBlindingSlab_RejectsSlabWithoutOverhangInBothDirections()
        => Assert.False(BlindingConcreteFilter.LooksLikeBlindingSlab(
            0, 100, 2200, 2050,
            100, 500, 2000, 2000,
            150, 20, 100));

    [Fact]
    public void LooksLikeBlindingSlab_RejectsThinStructuralStep()
        => Assert.False(BlindingConcreteFilter.LooksLikeBlindingSlab(
            0, 150, 2200, 2200,
            150, 500, 2000, 2000,
            150, 20, 100));
}
