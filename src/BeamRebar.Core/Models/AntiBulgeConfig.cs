namespace BeamRebar.Core.Models;

/// <summary>
///     Thép cấu tạo chống phình (anti-shrinkage / thép giá) — UI item 12 "more setting".
///     Theo TCVN, dầm cao (h > <see cref="HeightThresholdMm"/>, mặc định 550mm) cần bố trí thép
///     dọc cấu tạo ở hai mặt bên để chống nứt do co ngót / phình.
/// </summary>
public sealed record AntiBulgeConfig
{
    public bool Enabled { get; init; }

    /// <summary>Chiều cao dầm tối thiểu để cần thép chống phình (mm).</summary>
    public double HeightThresholdMm { get; init; } = 550;

    public RebarDiameter Diameter { get; init; } = new(12);

    /// <summary>Khoảng cách theo phương đứng giữa các thanh trên mặt bên (mm).</summary>
    public double SpacingMm { get; init; } = 500;
}
