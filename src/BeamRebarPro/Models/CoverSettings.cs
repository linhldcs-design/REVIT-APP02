namespace BeamRebarPro.Models;

/// <summary>
///     Lớp bê tông bảo vệ (mm) theo từng mặt. Mặc định 25mm theo TCVN cho dầm trong nhà.
/// </summary>
public sealed record CoverSettings
{
    public double TopMm { get; init; } = 25;
    public double BottomMm { get; init; } = 25;
    public double SideMm { get; init; } = 25;
}
