namespace WallRebar.Models;

/// <summary>
///     Lớp bê tông bảo vệ (mm) — khớp 3 ô "Cover Setting" trong dialog:
///     Top/Bottom (đỉnh &amp; chân tường), Left/Right (2 mặt tường, qua bề dày), Start/End (2 đầu chiều dài).
/// </summary>
public sealed record CoverSettings
{
    public double TopBottomMm { get; init; } = 25;
    public double LeftRightMm { get; init; } = 25;
    public double StartEndMm { get; init; } = 25;
}
