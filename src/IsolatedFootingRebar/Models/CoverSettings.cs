namespace IsolatedFootingRebar.Models;

/// <summary>
///     Lớp bê tông bảo vệ (mm) theo từng mặt của móng. Mặc định theo screenshot "Isolated Footing v1.1":
///     đáy 185mm (lớp bê tông lót dày), trên/cạnh 35mm.
/// </summary>
public sealed record CoverSettings
{
    public double BottomMm { get; init; } = 185;
    public double TopMm { get; init; } = 35;
    public double SideMm { get; init; } = 35;
}
