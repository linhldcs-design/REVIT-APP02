namespace WallRebar.Models;

/// <summary>
///     Cấu hình một nhóm thanh thép (1 hàng "Ø Dx @ spacing" trong dialog). Dùng chung cho:
///     thép dọc (vertical), thép ngang (horizontal) và thép giằng (tie).
/// </summary>
public sealed record WallLayerConfig
{
    public bool Enabled { get; init; } = true;
    public RebarDiameter Diameter { get; init; } = new(6);

    /// <summary>Bước rải các thanh (mm) theo phương vuông góc trục thanh.</summary>
    public double SpacingMm { get; init; } = 150;
}
