namespace RevitAPP.Core.Services;

/// <summary>Pure positioning math for slab break-line stubs in a beam cross section.</summary>
public static class SlabBreakLineMath
{
    public sealed record Positions(double? LeftX, double? RightX);

    public static Positions Calculate(double beamLeft, double beamRight, double slabLeft, double slabRight,
        double preferredStub, double minimumOverhang)
    {
        if (!AllFinite(beamLeft, beamRight, slabLeft, slabRight, preferredStub, minimumOverhang) ||
            beamRight <= beamLeft || slabRight <= slabLeft || preferredStub <= 0 || minimumOverhang < 0)
            throw new ArgumentOutOfRangeException(nameof(beamRight));

        var leftOverhang = beamLeft - slabLeft;
        var rightOverhang = slabRight - beamRight;
        var left = leftOverhang >= minimumOverhang
            ? beamLeft - Math.Min(preferredStub, leftOverhang * 0.75)
            : (double?)null;
        var right = rightOverhang >= minimumOverhang
            ? beamRight + Math.Min(preferredStub, rightOverhang * 0.75)
            : (double?)null;
        return new Positions(left, right);
    }

    private static bool AllFinite(params double[] values) => values.All(MathCompat.IsFinite);
}
