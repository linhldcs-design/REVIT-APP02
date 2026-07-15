namespace BeamRebarPro.Models;

/// <summary>
///     Kết quả tạo thép: số thanh theo nhóm + danh sách cảnh báo/lỗi (tiếng Việt). Errors không rỗng và
///     mọi count = 0 → coi như thất bại (vd thiếu họ thép). Dùng để hiển thị TaskDialog tổng kết.
/// </summary>
public sealed record RebarCreationResult(
    int LongitudinalCount,
    int StirrupCount,
    int AntiBulgeCount,
    IReadOnlyList<string> Warnings)
{
    public int Total => LongitudinalCount + StirrupCount + AntiBulgeCount;
    public bool Succeeded => Total > 0;
}
