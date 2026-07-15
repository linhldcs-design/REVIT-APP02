namespace BeamDrawing.Core.Models;

/// <summary>
///     Danh sách tên LOADABLE FAMILY bắt buộc phải có trong project để generate đầy đủ
///     (tag, dimension style, break-line, title block). Phase 5 đối chiếu với project và warn
///     nếu thiếu thay vì crash.
///     LƯU Ý: view template / section type KHÔNG nằm ở đây — chúng là document settings, không
///     phải family loadable; Phase 4 xử lý riêng bằng fallback + warn.
/// </summary>
public sealed record RequiredFamilies
{
    public IReadOnlyList<string> TagFamilyNames { get; init; } = [];
    public IReadOnlyList<string> DimensionTypeNames { get; init; } = [];
    public string? BreakLineFamilyName { get; init; }
    public string? TitleBlockName { get; init; }
}
