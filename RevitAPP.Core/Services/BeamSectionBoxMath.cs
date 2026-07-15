namespace RevitAPP.Core.Services;

/// <summary>Chuyển đổi kích thước section box thuần, không phụ thuộc Revit API.</summary>
public static class BeamSectionBoxMath
{
    public const double MillimetersPerFoot = 304.8;
    public const double CrossFarClipOffsetMm = 150.0;
    public const double SupportSafetyGapMm = 10.0;

    /// <summary>
    /// Trả nửa chiều sâu section box theo feet. Far Clip Offset của Revit bằng toàn bộ chiều sâu box.
    /// </summary>
    public static double HalfDepthFeet(double farClipOffsetMm)
    {
        if (!MathCompat.IsFinite(farClipOffsetMm) || farClipOffsetMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(farClipOffsetMm));

        return farClipOffsetMm / MillimetersPerFoot / 2.0;
    }

    /// <summary>Khoảng từ mép cột tới tâm lát cắt để toàn bộ Far Clip thoát khỏi cột.</summary>
    public static double CrossSupportClearanceFeet() =>
        HalfDepthFeet(CrossFarClipOffsetMm) + SupportSafetyGapMm / MillimetersPerFoot;
}
