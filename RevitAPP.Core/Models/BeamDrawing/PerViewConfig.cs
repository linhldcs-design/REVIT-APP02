namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>
///     Cấu hình cho 1 loại view (sectional elevation hoặc cross section): scale + tên section type +
///     view template + viewport type. Tên null = dùng mặc định document (resolve + warn khi tạo view).
/// </summary>
public sealed record PerViewConfig(
    int Scale,
    string? SectionTypeName,
    string? ViewTemplateName,
    string? ViewportTypeName);
