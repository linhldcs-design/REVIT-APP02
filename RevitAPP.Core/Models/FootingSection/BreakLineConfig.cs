namespace RevitAPP.Core.Models.FootingSection;

/// <summary>
///     Cấu hình Break Line (ký hiệu bẻ gãy cắt ngang cột phía trên mặt cắt móng).
///     Bật/tắt + tên family break line (line-based detail component). Tham chiếu bằng TÊN.
/// </summary>
public sealed record BreakLineConfig(
    bool Enabled,
    string? FamilyName)
{
    public static readonly BreakLineConfig Empty = new(Enabled: true, null);
}
