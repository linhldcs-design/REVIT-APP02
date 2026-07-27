using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Core.Models.BeamLongitudinalDrawing;

public sealed record LongitudinalDrawingSetting(
    string? SettingName,
    int Scale,
    string DimensionTypeName,
    string LongitudinalRebarTagTypeName,
    string StirrupTagTypeName,
    string DetailComponentTypeName,
    string ViewportTypeName,
    string LongitudinalSectionTypeName,
    string CrossSectionTypeName,
    string SpotElevationTypeName,
    string? ViewTemplateName,
    string? CrossViewTemplateName,
    string TitleBlockName,
    string SheetNumber,
    string SheetName,
    double AnnotationOffsetMm,
    double EndpointToleranceMm,
    double AlignmentToleranceMm,
    string? CrossSupportLongitudinalMraTypeName = null,
    string? CrossSupportStirrupTagTypeName = null,
    string? CrossMidLongitudinalMraTypeName = null,
    string? CrossMidStirrupTagTypeName = null,
    string? CrossViewportTypeName = null,
    string? CrossSupportReinforceL1TagTypeName = null,
    string? CrossMidReinforceL1TagTypeName = null,
    string? CrossSupportReinforceL2MraTypeName = null,
    string? CrossMidReinforceL2MraTypeName = null,
    string? CrossBreakLineTypeName = null);

public sealed record LongitudinalProjectResources(
    IReadOnlyList<string> DimensionTypes,
    IReadOnlyList<string> RebarTagTypes,
    IReadOnlyList<string> DetailComponentTypes,
    IReadOnlyList<string> ViewportTypes,
    IReadOnlyList<string> SectionTypes,
    IReadOnlyList<string> SpotElevationTypes,
    IReadOnlyList<string> ViewTemplates,
    IReadOnlyList<ProjectSheetOption> ExistingSheets,
    IReadOnlyList<string> MultiRebarAnnotationTypes);
