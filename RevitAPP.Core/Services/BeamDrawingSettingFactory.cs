using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Core.Services;

/// <summary>
///     Tạo cấu hình mặc định cho bản vẽ dầm khớp giá trị mặc định form BIMSpeed:
///     tỉ lệ 1:25, spacing factor 6, DS1 = 200mm, dim tới mặt đáy = 200mm, sinh cross section.
/// </summary>
public static class BeamDrawingSettingFactory
{
    public const int DefaultScale = 25;
    public const int DefaultSpacingFactor = 6;
    public const double DefaultDistanceToSideBeamMm = 200;
    public const double DefaultDistanceToBotFaceMm = 200;

    public static BeamDrawingSetting CreateDefault() => new(
        SettingName: null,
        Sectional: new PerViewConfig(DefaultScale, null, null, null),
        CrossSection: new PerViewConfig(DefaultScale, null, null, null),
        Tags: RebarTagSet.Empty,
        Spot: new SpotElevationConfig(Enabled: true, TypeName: null, OffsetMm: 0),
        Dim: new DimensionConfig(
            Enabled: true,
            SectionalDimTypeName: null,
            CrossDimTypeName: null,
            SpacingFactor: DefaultSpacingFactor,
            DistanceToSideBeamMm: DefaultDistanceToSideBeamMm,
            DistanceToBotFaceMm: DefaultDistanceToBotFaceMm),
        BreakLine: true,
        BreakLineFamilyName: null,
        Sheet: new SheetConfig(Number: "KC-01", Name: "TRIEN KHAI DAM", TitleBlockName: null),
        Flags: new DrawingFlags(
            LongSection: false,
            CrossSection: true,
            CrossSectionForMultiBeam: false,
            PickPillowToDim: false,
            CreateView3D: false),
        CrossAnnotation: CrossAnnotationConfig.Empty);
}
