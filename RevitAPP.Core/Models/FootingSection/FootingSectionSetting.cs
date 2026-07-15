using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Core.Models.FootingSection;

/// <summary>
///     Cấu hình tổng cho 1 lần sinh bản vẽ mặt cắt móng (MC 2-2). Thuần (không Revit API).
///     Tham chiếu section type / view template / viewport / tag / detail item bằng TÊN;
///     engine resolve sang ElementId (fallback + warn). Reuse <see cref="SheetConfig"/> của BeamDrawing.
/// </summary>
public sealed record FootingSectionSetting(
    string? SettingName,
    int Scale,
    string? SectionTypeName,
    string? ViewTemplateName,
    string? ViewportTypeName,
    RebarTagConfig Tags,
    FootingDimensionConfig Dim,
    BreakLineConfig BreakLine,
    SheetConfig Sheet,
    FootingSectionFlags Flags,
    FootingSectionDirection Direction = FootingSectionDirection.X,
    string? ViewBottomLevelName = null,
    string? ViewTopLevelName = null);
