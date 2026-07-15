namespace IsolatedFootingRebar.Models;

/// <summary>
///     Kết quả tạo thép móng: số set thanh theo nhóm + danh sách cảnh báo/lỗi (tiếng Việt). Errors không
///     rỗng và mọi count = 0 → coi như thất bại (vd thiếu họ thép). Dùng để hiển thị TaskDialog tổng kết.
/// </summary>
public sealed record RebarCreationResult(
    int MeshCount,
    int VerticalCount,
    int StirrupCount,
    IReadOnlyList<string> Warnings)
{
    public int Total => MeshCount + VerticalCount + StirrupCount;
    public bool Succeeded => Total > 0;
}
