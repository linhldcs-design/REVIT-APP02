using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Services;
using Xunit;

namespace RevitAPP.Tests.BeamLongitudinalDrawing;

public sealed class RebarFingerprintComparerTests
{
    [Fact]
    public void AreEquivalent_SameContentDifferentOrder_ReturnsTrue()
    {
        var first = Fingerprint(
            [new RebarLayerFingerprint(0.1, 16, 3), new RebarLayerFingerprint(1.8, 18, 2)],
            [new StirrupZoneFingerprint(8, 0.3)]);
        var second = Fingerprint(
            [new RebarLayerFingerprint(1.8, 18, 2), new RebarLayerFingerprint(0.1, 16, 3)],
            [new StirrupZoneFingerprint(8, 0.3)]);

        Assert.True(RebarFingerprintComparer.AreEquivalent(first, second, RebarFingerprintTolerance.Default));
    }

    [Theory]
    [InlineData(17, 3, 0.1, 8, 0.3)]
    [InlineData(16, 2, 0.1, 8, 0.3)]
    [InlineData(16, 3, 0.25, 8, 0.3)]
    [InlineData(16, 3, 0.1, 10, 0.3)]
    [InlineData(16, 3, 0.1, 8, 0.5)]
    public void AreEquivalent_DifferentRebarOrStirrupSignature_ReturnsFalse(
        double diameter, int quantity, double elevation, double stirrupDiameter, double spacing)
    {
        var baseline = Fingerprint(
            [new RebarLayerFingerprint(0.1, 16, 3)],
            [new StirrupZoneFingerprint(8, 0.3)]);
        var changed = Fingerprint(
            [new RebarLayerFingerprint(elevation, diameter, quantity)],
            [new StirrupZoneFingerprint(stirrupDiameter, spacing)]);

        Assert.False(RebarFingerprintComparer.AreEquivalent(
            baseline, changed, new RebarFingerprintTolerance(0.01, 0.01, 0.01, 0.01)));
    }

    [Fact]
    public void AreEquivalent_UncertainFingerprint_FailsSafe()
    {
        var certain = Fingerprint([], []);
        var uncertain = certain with { IsUncertain = true };

        Assert.False(RebarFingerprintComparer.AreEquivalent(certain, uncertain, RebarFingerprintTolerance.Default));
    }

    [Fact]
    public void AreEquivalent_DifferentAdditionalReinforcementFlag_ReturnsFalse()
    {
        var baseline = Fingerprint([new RebarLayerFingerprint(0.1, 16, 3)],
            [new StirrupZoneFingerprint(8, 0.3)]);
        var reinforced = baseline with { HasAdditionalReinforcement = true };

        Assert.False(RebarFingerprintComparer.AreEquivalent(
            baseline, reinforced, RebarFingerprintTolerance.Default));
    }

    private static RebarStationFingerprint Fingerprint(
        IReadOnlyList<RebarLayerFingerprint> layers,
        IReadOnlyList<StirrupZoneFingerprint> stirrups) =>
        new(1, 2, layers, stirrups, false);
}
