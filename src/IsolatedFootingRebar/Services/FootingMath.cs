namespace IsolatedFootingRebar.Services;

/// <summary>
///     Toán bố trí thép THUẦN (không chạm Revit API) → test được out-of-process. Quy đổi đơn vị, suy số
///     thanh từ bước rải, và sinh vị trí phân bố đều trên một dải.
/// </summary>
public static class FootingMath
{
    public const double MmPerFoot = 304.8;

    public static double MmToFeet(double mm) => mm / MmPerFoot;
    public static double FeetToMm(double feet) => feet * MmPerFoot;

    /// <summary>
    ///     Số thanh khi rải đều theo bước trên dải dài <paramref name="usableLengthMm"/>: số khoảng =
    ///     floor(L/spacing), số thanh = khoảng + 1 (cả 2 thanh biên). Trả 0 nếu tham số không hợp lệ.
    /// </summary>
    public static int SpacingToCount(double usableLengthMm, double spacingMm)
    {
        if (usableLengthMm <= 0 || spacingMm <= 0) return 0;
        var gaps = (int)Math.Floor(usableLengthMm / spacingMm);
        return gaps + 1;
    }

    /// <summary>
    ///     Vị trí (mm, từ 0) của <paramref name="count"/> thanh phân bố đều phủ hết dải dài
    ///     <paramref name="usableLengthMm"/>: thanh đầu tại 0, thanh cuối tại L, các thanh giữa chia đều.
    ///     count ≤ 1 → 1 thanh tại giữa dải.
    /// </summary>
    public static IReadOnlyList<double> EvenPositions(double usableLengthMm, int count)
    {
        if (count <= 0 || usableLengthMm < 0) return [];
        if (count == 1) return [usableLengthMm / 2];

        var step = usableLengthMm / (count - 1);
        var positions = new double[count];
        for (var i = 0; i < count; i++)
            positions[i] = i * step;
        return positions;
    }

    /// <summary>Dải hữu dụng (mm) để rải thép = kích thước móng trừ 2 lần lớp bảo vệ cạnh. Không âm.</summary>
    public static double UsableLengthMm(double footingDimensionMm, double sideCoverMm)
        => Math.Max(0, footingDimensionMm - 2 * sideCoverMm);
}
