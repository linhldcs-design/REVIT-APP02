namespace BeamRebarPro.Models;

/// <summary>
///     Thép cấu tạo chống phình (side/skin reinforcement) cho dầm cao. Chỉ tạo khi <see cref="Enabled"/>
///     và chiều cao tiết diện vượt <see cref="HeightThresholdMm"/>. Thanh chạy dọc hai mặt bên.
/// </summary>
public sealed record AntiBulgeConfig
{
    public bool Enabled { get; init; }

    /// <summary>Ngưỡng chiều cao tiết diện (mm) bắt đầu cần thép chống phình. TCVN ~ h > 700.</summary>
    public double HeightThresholdMm { get; init; } = 550;

    public RebarDiameter Diameter { get; init; } = new(12);

    public int Count { get; init; } = 2;

    public RebarDiameter TieDiameter { get; init; } = new(6);

    /// <summary>Khoảng cách theo phương đứng giữa các thanh chống phình (mm).</summary>
    public double SpacingMm { get; init; } = 500;

    public double ColumnEmbedMm { get; init; }
}
