namespace BeamDrawing.Core.Models;

/// <summary>
///     Cấu hình view cho MỘT loại (Sectional Elevation hoặc Cross Section): view template,
///     viewport type, section type và scale. Khớp các hàng "View Template / Viewport /
///     Section Type / Scale" trong UI (mỗi loại view 1 cột).
/// </summary>
public sealed record ViewConfig
{
    public string? ViewTemplateName { get; init; }
    public string? ViewportTypeName { get; init; }
    public string? SectionTypeName { get; init; }

    /// <summary>Tỉ lệ bản vẽ (vd 25 → 1:25). Phải &gt; 0.</summary>
    public int Scale { get; init; } = 25;

    /// <summary>Tên view sinh ra (View Name trong UI).</summary>
    public string? ViewNamePattern { get; init; }
}
