namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>
///     Danh mục tài nguyên document (tên) nạp cho toàn bộ combo của dialog Beam Drawing v2.
///     Thuần (list string) để ViewModel test được không cần Revit.
/// </summary>
public sealed record ProjectResources(
    IReadOnlyList<string> RebarTagTypeNames,
    IReadOnlyList<string> MultiRebarAnnotationTypeNames,
    IReadOnlyList<string> SpotElevationTypeNames,
    IReadOnlyList<string> DimensionTypeNames,
    IReadOnlyList<string> SectionTypeNames,
    IReadOnlyList<string> ViewTemplateNames,
    IReadOnlyList<string> ViewportTypeNames,
    IReadOnlyList<string> BreakLineFamilyNames,
    IReadOnlyList<string> TitleBlockNames,
    IReadOnlyList<ProjectSheetOption> ExistingSheets)
{
    public static ProjectResources Empty { get; } =
        new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<ProjectSheetOption>());
}

/// <summary>Sheet đã tồn tại trong document, dùng để chọn đích đặt viewport.</summary>
public sealed record ProjectSheetOption(string Number, string Name)
{
    public string Display => $"{Number} — {Name}";
    public override string ToString() => Display;
}
