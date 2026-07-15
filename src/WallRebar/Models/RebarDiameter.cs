namespace WallRebar.Models;

/// <summary>
///     Đường kính cốt thép thông dụng theo TCVN (mm). Lưu là số nguyên mm để khớp tên RebarBarType
///     trong Revit ("D16", "D6"...).
/// </summary>
public readonly record struct RebarDiameter(int Millimeters)
{
    /// <summary>Các đường kính chuẩn TCVN dùng trong UI ComboBox.</summary>
    public static readonly IReadOnlyList<RebarDiameter> Standard =
    [
        new(6), new(8), new(10), new(12), new(14), new(16),
        new(18), new(20), new(22), new(25), new(28), new(32)
    ];

    /// <summary>Tên hiển thị + khớp tên RebarBarType trong document, vd "D16".</summary>
    public override string ToString() => $"D{Millimeters}";

    /// <summary>Đường kính quy đổi sang feet (đơn vị nội bộ Revit).</summary>
    public double Feet => Millimeters / 304.8;
}
