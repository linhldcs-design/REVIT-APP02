namespace RevitAPP.Core.Services;

/// <summary>Tính vị trí cắt GỐI/NHỊP từ các vị trí cột đã chiếu lên trục dầm.</summary>
public static class BeamSectionStationMath
{
    private const double FallbackSupport = 0.035;
    private const double FallbackMidSpan = 0.5;
    private const double EndClamp = 0.01;
    private const double SupportInsetRatio = 0.035;

    public static (double Support, double MidSpan) Resolve(
        IEnumerable<double> rawColumnStations,
        double duplicateTolerance)
    {
        if (duplicateTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(duplicateTolerance));

        var distinct = new List<double>();
        foreach (var station in rawColumnStations
                     .Where(MathCompat.IsFinite)
                     .Select(Clamp01)
                     .OrderBy(value => value))
        {
            if (distinct.Count == 0 || station - distinct[^1] > duplicateTolerance)
                distinct.Add(station);
        }

        if (distinct.Count == 0) return (FallbackSupport, FallbackMidSpan);

        double spanStart;
        double spanEnd;
        bool supportAtStart;

        if (distinct.Count >= 2)
        {
            spanStart = distinct[0];
            spanEnd = distinct[1];
            supportAtStart = true;
        }
        else if (distinct[0] <= 0.5)
        {
            spanStart = distinct[0];
            spanEnd = 1.0;
            supportAtStart = true;
        }
        else
        {
            spanStart = 0.0;
            spanEnd = distinct[0];
            supportAtStart = false;
        }

        var spanLength = spanEnd - spanStart;
        if (spanLength <= duplicateTolerance)
            return (FallbackSupport, FallbackMidSpan);

        var support = supportAtStart
            ? spanStart + spanLength * SupportInsetRatio
            : spanEnd - spanLength * SupportInsetRatio;
        var midSpan = (spanStart + spanEnd) * 0.5;

        return (ClampForSection(support), ClampForSection(midSpan));
    }

    /// <summary>
    /// Đẩy station GỐI ra ngoài khoảng chiếm chỗ của cột nếu mốc danh nghĩa còn phạm cột.
    /// </summary>
    public static double EnsureOutsideSupport(double nominalSupport, double supportMin, double supportMax,
        bool spanTowardIncreasingStation, double clearance)
    {
        if (clearance < 0) throw new ArgumentOutOfRangeException(nameof(clearance));
        if (supportMin > supportMax) (supportMin, supportMax) = (supportMax, supportMin);

        var adjusted = spanTowardIncreasingStation
            ? Math.Max(nominalSupport, supportMax + clearance)
            : Math.Min(nominalSupport, supportMin - clearance);
        return ClampForSection(adjusted);
    }

    private static double Clamp01(double value) => Math.Min(Math.Max(value, 0), 1);
    private static double ClampForSection(double value) => Math.Min(Math.Max(value, EndClamp), 1 - EndClamp);
}
