namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>
///     Cấu hình tổng cho 1 lần sinh bản vẽ dầm (v2 — đầy đủ như form BIMSpeed). Thuần (không Revit API).
///     Tham chiếu family/type bằng TÊN; engine resolve sang ElementId (fallback + warn).
///     Xem đặc tả: RevitAPP/docs/BEAM-DRAWING-BIMSPEED-SPEC.md.
/// </summary>
public sealed record BeamDrawingSetting(
    string? SettingName,
    PerViewConfig Sectional,
    PerViewConfig CrossSection,
    RebarTagSet Tags,
    SpotElevationConfig Spot,
    DimensionConfig Dim,
    bool BreakLine,
    string? BreakLineFamilyName,
    SheetConfig Sheet,
    DrawingFlags Flags,
    CrossAnnotationConfig? CrossAnnotation = null);
