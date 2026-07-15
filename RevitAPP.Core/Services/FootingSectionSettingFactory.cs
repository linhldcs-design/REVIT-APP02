using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.FootingSection;

namespace RevitAPP.Core.Services;

/// <summary>
///     Tạo cấu hình mặc định cho bản vẽ mặt cắt móng: tỉ lệ 1:25, mọi thành phần bật, type = null (default option).
/// </summary>
public static class FootingSectionSettingFactory
{
    public const int DefaultScale = 25;
    public const double DefaultDimOffsetMm = 200;

    public static FootingSectionSetting CreateDefault() => new(
        SettingName: null,
        Scale: DefaultScale,
        SectionTypeName: null,
        ViewTemplateName: null,
        ViewportTypeName: null,
        Tags: RebarTagConfig.Empty,
        Dim: new FootingDimensionConfig(Enabled: true, DimTypeName: null, OffsetMm: DefaultDimOffsetMm),
        BreakLine: BreakLineConfig.Empty,
        Sheet: new SheetConfig(Number: "KC-01", Name: "TRIEN KHAI MONG", TitleBlockName: null),
        Flags: new FootingSectionFlags(TagEnabled: true, DimEnabled: true, BreakLineEnabled: true),
        Direction: FootingSectionDirection.X);
}
