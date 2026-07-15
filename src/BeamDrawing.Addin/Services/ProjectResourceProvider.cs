using Autodesk.Revit.DB;
using BeamDrawing.Addin.ViewModels;

namespace BeamDrawing.Addin.Services;

/// <summary>
///     Nạp nguồn dữ liệu combobox từ document hiện hành cho UI, và resolve các loại view/section
///     theo tên khi generate. View template / section type là DOCUMENT SETTINGS (không phải family);
///     nếu không tìm thấy theo tên → fallback mặc định + warn (không crash).
/// </summary>
public sealed class ProjectResourceProvider
{
    public ProjectResources LoadResources(Document doc) => new()
    {
        RebarTagTypes = TypeNames(doc, BuiltInCategory.OST_RebarTags),
        ViewTemplates = ViewTemplateNames(doc),
        ViewportTypes = ViewportTypeNames(doc),
        SectionTypes = SectionTypeNames(doc),
        Scales = CommonScales(),
        SpotElevationTypes = TypeNames(doc, BuiltInCategory.OST_SpotElevations),
        DimensionTypes = DimensionTypeNames(doc),
        BreakLineFamilies = TypeNames(doc, BuiltInCategory.OST_DetailComponents),
        TitleBlocks = TypeNames(doc, BuiltInCategory.OST_TitleBlocks),
        Sheets = SheetList(doc)
    };

    private static IReadOnlyList<SheetOption> SheetList(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(s => new SheetOption(s.SheetNumber, s.Name))
            .OrderBy(s => s.Number)
            .ToList();

    /// <summary>
    ///     Resolve ViewFamilyType cho section. Tìm theo tên trong setting; không thấy → ViewFamilyType
    ///     Section đầu tiên + warn.
    /// </summary>
    public ElementId ResolveSectionType(Document doc, string? typeName, List<string> warnings)
    {
        var sectionTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .Where(vft => vft.ViewFamily == ViewFamily.Section)
            .ToList();

        if (sectionTypes.Count == 0)
            throw new InvalidOperationException("Project không có ViewFamilyType dạng Section.");

        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var match = sectionTypes.FirstOrDefault(v => v.Name == typeName);
            if (match != null) return match.Id;
            warnings.Add($"Không tìm thấy Section Type '{typeName}', dùng mặc định '{sectionTypes[0].Name}'.");
        }

        return sectionTypes[0].Id;
    }

    /// <summary>Resolve ViewTemplate theo tên (null nếu không đặt hoặc không tìm thấy → warn).</summary>
    public ElementId? ResolveViewTemplate(Document doc, string? templateName, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(templateName)) return null;

        var template = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(v => v.IsTemplate && v.Name == templateName);

        if (template != null) return template.Id;
        warnings.Add($"Không tìm thấy View Template '{templateName}', bỏ qua gán template.");
        return null;
    }

    private static IReadOnlyList<ComboOption> TypeNames(Document doc, BuiltInCategory category)
        => new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsElementType()
            .Select(e => new ComboOption(e.Name, e.Id.Value.ToString()))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ComboOption> ViewTemplateNames(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .Select(v => new ComboOption(v.Name))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ComboOption> ViewportTypeNames(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(ElementType))
            .Where(e => e.Category?.Id.Value == (long)BuiltInCategory.OST_Viewports)
            .Select(e => new ComboOption(e.Name, e.Id.Value.ToString()))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ComboOption> SectionTypeNames(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .Where(v => v.ViewFamily == ViewFamily.Section)
            .Select(v => new ComboOption(v.Name, v.Id.Value.ToString()))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ComboOption> DimensionTypeNames(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(DimensionType))
            .Select(e => new ComboOption(e.Name, e.Id.Value.ToString()))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ComboOption> CommonScales()
        => new[] { 10, 15, 20, 25, 50, 75, 100 }
            .Select(s => new ComboOption(s.ToString(), s.ToString()))
            .ToList();
}
