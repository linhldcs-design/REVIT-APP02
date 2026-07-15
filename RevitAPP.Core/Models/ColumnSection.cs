namespace RevitAPP.Core.Models;

/// <summary>Hình dáng tiết diện cột. v1 chỉ hỗ trợ chữ nhật/vuông.</summary>
public enum ColumnShape
{
    Rectangular
}

/// <summary>
///     Tiết diện cột (mm). <paramref name="WidthMm"/> = B theo phương X,
///     <paramref name="HeightMm"/> = H theo phương Y.
/// </summary>
public sealed record ColumnSection(double WidthMm, double HeightMm, ColumnShape Shape = ColumnShape.Rectangular);
