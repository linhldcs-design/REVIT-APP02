namespace BeamDrawing.Core.Models;

/// <summary>
///     Cấu hình template hoàn chỉnh cho một lần tạo bản vẽ chi tiết thép dầm — tương ứng MỘT mục
///     trong "Setting List" của UI (vd "BS-A1-25-BEAM-DX"). Immutable; serialize JSON ở Phase 6.
/// </summary>
public sealed record BeamDrawingSetting
{
    /// <summary>Tên setting (UI: "Setting Name"). Bắt buộc, dùng làm khoá trong list.</summary>
    public string Name { get; init; } = string.Empty;

    public RebarTagMapping TagMapping { get; init; } = new();

    /// <summary>Cấu hình view mặt cắt dọc (Sectional Elevation).</summary>
    public ViewConfig Sectional { get; init; } = new();

    /// <summary>Cấu hình view mặt cắt ngang (Cross Section).</summary>
    public ViewConfig CrossSection { get; init; } = new();

    public SpotElevationConfig SpotElevation { get; init; } = new();
    public DimensionConfig Dimension { get; init; } = new();
    public BreakLineConfig BreakLine { get; init; } = new();

    /// <summary>Title block (UI: "TITLE BLOCK").</summary>
    public string? TitleBlockName { get; init; }

    /// <summary>Số hiệu sheet (UI: "SHEET NUMBER", vd "KC-0011.1.1").</summary>
    public string? SheetNumber { get; init; }

    /// <summary>Tên sheet (UI: "SHEET NAME", vd "CHI TIẾT THÉP DẦM LẦU 1").</summary>
    public string? SheetName { get; init; }

    public BeamDrawingFlags Flags { get; init; } = new();
}
