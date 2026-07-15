namespace IsolatedFootingRebar.Models;

/// <summary>
///     Cấu hình thép cho MỘT phương (X hoặc Y) của một lớp lưới (bottom/top/mid). Khớp UI: chọn đường
///     kính, rải theo bước (UseSpacing=true → SpacingMm) HOẶC theo số lượng cố định (Count), kèm tùy
///     chọn móc bẻ ở 2 đầu (HookLengthMm).
/// </summary>
public sealed record LayerBarConfig
{
    /// <summary>Phương này có vẽ thép không (bỏ tick → bỏ qua).</summary>
    public bool Enabled { get; init; } = true;

    public RebarDiameter Diameter { get; init; } = new(6);

    /// <summary>true → rải theo bước <see cref="SpacingMm"/>; false → rải đúng <see cref="Count"/> thanh.</summary>
    public bool UseSpacing { get; init; } = true;

    public double SpacingMm { get; init; } = 150;

    public int Count { get; init; } = 5;

    /// <summary>Có bẻ móc 2 đầu thanh không.</summary>
    public bool HookEnabled { get; init; } = true;

    /// <summary>Chiều dài đoạn móc bẻ (mm).</summary>
    public double HookLengthMm { get; init; } = 600;
}
