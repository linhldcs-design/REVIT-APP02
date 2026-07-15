namespace WallRebar.Models;

/// <summary>
///     Kết quả tạo thép tường: số set thanh dọc/ngang + số thanh giằng (tie) + cảnh báo/lỗi (tiếng Việt).
///     Errors không rỗng và mọi count = 0 → coi như thất bại (vd thiếu họ thép).
/// </summary>
public sealed record RebarCreationResult(
    int VerticalBarCount,
    int HorizontalBarCount,
    int TieCount,
    IReadOnlyList<string> Warnings)
{
    public int Total => VerticalBarCount + HorizontalBarCount + TieCount;
    public bool Succeeded => Total > 0;
}
