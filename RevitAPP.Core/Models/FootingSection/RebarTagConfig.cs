namespace RevitAPP.Core.Models.FootingSection;

/// <summary>
///     Cấu hình tag thép cho mặt cắt móng: bật/tắt + tên tag type cho từng nhóm thép
///     (đế móng, đai cổ, thép chờ cột). Tham chiếu bằng TÊN; engine resolve sang ElementId.
/// </summary>
public sealed record RebarTagConfig(
    bool TagFooting,
    string? FootingBarTagName,
    bool TagStirrup,
    string? StirrupTagName,
    bool TagStarter,
    string? StarterTagName)
{
    public static readonly RebarTagConfig Empty =
        new(TagFooting: true, null, TagStirrup: true, null, TagStarter: true, null);
}
