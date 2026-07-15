namespace BeamRebar.Core.Models;

/// <summary>
///     Lớp bê tông bảo vệ (mm) theo TCVN 5574:2018. Mặc định 25mm cho dầm trong nhà.
///     Đo từ mép ngoài bê tông tới mép ngoài cốt đai.
/// </summary>
public sealed record CoverSettings
{
    public double TopMm { get; init; } = 25;
    public double BottomMm { get; init; } = 25;
    public double SideMm { get; init; } = 25;
}
