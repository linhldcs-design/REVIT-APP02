namespace RevitAPP.Core.Services;

/// <summary>Logic thuần dùng khi lọc rebar theo cross station và map thứ tự sang slot D0–D5.</summary>
public static class BeamAnnotationMath
{
    public static bool IntersectsStation(double stationProjection, double minProjection,
        double maxProjection, double tolerance)
    {
        if (minProjection > maxProjection) (minProjection, maxProjection) = (maxProjection, minProjection);
        return stationProjection >= minProjection - tolerance && stationProjection <= maxProjection + tolerance;
    }

    public static int CrossTagSlot(int rebarIndex, int availableSlots = 6)
    {
        if (rebarIndex < 0) throw new ArgumentOutOfRangeException(nameof(rebarIndex));
        if (availableSlots <= 0) throw new ArgumentOutOfRangeException(nameof(availableSlots));
        return Math.Min(rebarIndex, availableSlots - 1);
    }
}
