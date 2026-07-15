namespace BeamDrawing.Core.Models;

/// <summary>
///     Ánh xạ tag thép cho 2 loại view, khớp nhóm "Rebar Tag Sectional Elevation" và
///     "Rebar Tag Cross Section" trong UI.
/// </summary>
public sealed record RebarTagMapping
{
    /// <summary>Bật/tắt cả nhóm tag mặt cắt dọc (checkbox header REBAR TAG SECTIONAL ELEVATION).</summary>
    public bool SectionalEnabled { get; init; } = true;

    /// <summary>Bật/tắt cả nhóm tag mặt cắt ngang (checkbox header REBAR TAG CROSS SECTION).</summary>
    public bool CrossEnabled { get; init; } = true;

    // Sectional Elevation: T1, T2, item 4 + tuỳ chọn vẽ ký hiệu cắt thép.
    public string? T1TagTypeName { get; init; }
    public string? T2TagTypeName { get; init; }
    public string? Item4TagTypeName { get; init; }
    public bool RebarBreakSymbol { get; init; }

    // Cross Section: D0–D5.
    public string? D0TagTypeName { get; init; }
    public string? D1TagTypeName { get; init; }
    public string? D2TagTypeName { get; init; }
    public string? D3TagTypeName { get; init; }
    public string? D4TagTypeName { get; init; }
    public string? D5TagTypeName { get; init; }
}
