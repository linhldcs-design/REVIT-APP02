namespace RevitAPP.Core.Models.FootingSection;

/// <summary>
///     Cấu hình dimension cho mặt cắt móng: bật/tắt + tên dim type + khoảng cách dim tới mép hình (mm).
/// </summary>
public sealed record FootingDimensionConfig(
    bool Enabled,
    string? DimTypeName,
    double OffsetMm)
{
    public static readonly FootingDimensionConfig Empty = new(Enabled: true, null, OffsetMm: 200);
}
