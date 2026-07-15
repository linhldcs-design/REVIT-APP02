namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>
///     Tên tag family theo VỊ TRÍ thép cho mặt đứng (T1/T2/Mid) và mặt cắt ngang (D0–D5), khớp form BIMSpeed.
///     Tên null = bỏ qua tag vị trí đó (fallback tag mặc định + warn khi engine resolve).
/// </summary>
public sealed record RebarTagSet(
    string? T1,
    string? T2,
    string? MidItem,
    string? D0,
    string? D1,
    string? D2,
    string? D3,
    string? D4,
    string? D5,
    bool RebarBreakSymbol)
{
    public static RebarTagSet Empty { get; } =
        new(null, null, null, null, null, null, null, null, null, RebarBreakSymbol: true);
}
