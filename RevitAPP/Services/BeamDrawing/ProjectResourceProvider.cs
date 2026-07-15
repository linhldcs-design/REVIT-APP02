using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Nạp tên tài nguyên document cho toàn bộ combo UI v2, và resolve type theo tên khi generate.
///     Section type / view template / viewport / title block là DOCUMENT SETTINGS →
///     không thấy theo tên thì fallback mặc định + warn (không crash).
/// </summary>
public sealed class ProjectResourceProvider
{
    public ProjectResources LoadResources(Document doc) => new(
        RebarTagTypeNames: TypeNames(doc, BuiltInCategory.OST_RebarTags),
        MultiRebarAnnotationTypeNames: MultiRebarAnnotationTypes(doc).Select(type => type.Name).OrderBy(n => n).ToList(),
        SpotElevationTypeNames: SpotTypes(doc).Select(TypeDisplayName).OrderBy(n => n).ToList(),
        DimensionTypeNames: DimensionTypes(doc).Select(TypeDisplayName).OrderBy(n => n).ToList(),
        SectionTypeNames: SectionTypes(doc).Select(v => v.Name).OrderBy(n => n).ToList(),
        ViewTemplateNames: ViewTemplates(doc).Select(v => v.Name).OrderBy(n => n).ToList(),
        ViewportTypeNames: ViewportTypes(doc).Select(TypeDisplayName).OrderBy(n => n).ToList(),
        BreakLineFamilyNames: TypeNames(doc, BuiltInCategory.OST_DetailComponents),
        TitleBlockNames: TitleBlocks(doc).Select(TypeDisplayName).OrderBy(n => n).ToList(),
        ExistingSheets: ExistingSheets(doc));

    /// <summary>Resolve ViewFamilyType dùng để tạo section/detail. Không thấy theo tên → cái đầu tiên + warn.</summary>
    public ElementId ResolveSectionType(Document doc, string? typeName, List<string> warnings)
    {
        var sectionTypes = SectionTypes(doc);
        if (sectionTypes.Count == 0)
            throw new InvalidOperationException("Project không có ViewFamilyType dạng Section hoặc Detail.");

        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var match = sectionTypes.FirstOrDefault(v => v.Name == typeName);
            if (match != null) return match.Id;
            warnings.Add($"Không tìm thấy Section Type '{typeName}', dùng '{sectionTypes[0].Name}'.");
        }

        return sectionTypes[0].Id;
    }

    /// <summary>Resolve view template theo tên; null nếu không đặt hoặc không tìm thấy (+warn).</summary>
    public ElementId? ResolveViewTemplate(Document doc, string? templateName, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(templateName)) return null;

        var template = ViewTemplates(doc).FirstOrDefault(v => v.Name == templateName);
        if (template != null) return template.Id;

        warnings.Add($"Không tìm thấy View Template '{templateName}', bỏ qua gán template.");
        return null;
    }

    public ElementId? ResolveRebarTagType(Document doc, string? typeName, List<string> warnings) =>
        ResolveElementType(doc, TypeElements(doc, BuiltInCategory.OST_RebarTags), typeName,
            "Rebar Tag", warnings);

    public ElementId? ResolveMultiRebarAnnotationType(Document doc, string? typeName, List<string> warnings) =>
        ResolveElementType(doc, MultiRebarAnnotationTypes(doc).Cast<Element>().ToList(), typeName,
            "Multi-Rebar Annotation Type", warnings);

    public ElementId? ResolveSpotType(Document doc, string? typeName, List<string> warnings) =>
        ResolveElementType(doc, SpotTypes(doc).Cast<Element>().ToList(), typeName,
            "Spot Elevation", warnings);

    public ElementId? ResolveDimType(Document doc, string? typeName, List<string> warnings) =>
        ResolveElementType(doc, DimensionTypes(doc).Cast<Element>().ToList(), typeName,
            "Dimension Type", warnings);

    public ElementId? ResolveViewportType(Document doc, string? typeName, List<string> warnings) =>
        ResolveElementType(doc, ViewportTypes(doc).Cast<Element>().ToList(), typeName,
            "Viewport Type", warnings);

    public ElementId? ResolveBreakLineSymbol(Document doc, string? typeName, List<string> warnings) =>
        ResolveElementType(doc, TypeElements(doc, BuiltInCategory.OST_DetailComponents), typeName,
            "Break Line", warnings);

    public ElementId? ResolveTitleBlock(Document doc, string? typeName, List<string> warnings) =>
        ResolveElementType(doc, TitleBlocks(doc), typeName, "Title Block", warnings);

    private static List<ViewFamilyType> SectionTypes(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            // Revit tách các Section Type trong project thành hai ViewFamily.
            // Chỉ lọc ViewFamily.Section sẽ làm mất nhóm Detail Section trong combo.
            .Where(v => v.ViewFamily is ViewFamily.Section or ViewFamily.Detail)
            .ToList();

    private static List<View> ViewTemplates(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .ToList();

    private static List<DimensionType> DimensionTypes(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(DimensionType))
            .Cast<DimensionType>()
            .ToList();

    private static List<SpotDimensionType> SpotTypes(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(SpotDimensionType))
            .Cast<SpotDimensionType>()
            .ToList();

    private static List<ElementType> ViewportTypes(Document doc)
    {
        // Viewport type = ElementType có FamilyName "Viewport" (KHÔNG lấy được qua OST_Viewports category).
        var byFamily = new FilteredElementCollector(doc)
            .OfClass(typeof(ElementType))
            .Cast<ElementType>()
            .Where(t => t.FamilyName == "Viewport")
            .ToList();
        if (byFamily.Count > 0) return byFamily;

        // Fallback: lấy valid types từ 1 Viewport instance có sẵn trong project.
        var vp = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().FirstOrDefault();
        if (vp != null)
            return vp.GetValidTypes()
                .Select(id => doc.GetElement(id) as ElementType)
                .Where(t => t != null)
                .Cast<ElementType>()
                .ToList();

        return new List<ElementType>();
    }

    private static List<MultiReferenceAnnotationType> MultiRebarAnnotationTypes(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(MultiReferenceAnnotationType))
            .Cast<MultiReferenceAnnotationType>()
            .ToList();

    private static List<Element> TitleBlocks(Document doc) =>
        TypeElements(doc, BuiltInCategory.OST_TitleBlocks);

    private static IReadOnlyList<ProjectSheetOption> ExistingSheets(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(sheet => !sheet.IsPlaceholder)
            .OrderBy(sheet => sheet.SheetNumber)
            .Select(sheet => new ProjectSheetOption(sheet.SheetNumber, sheet.Name))
            .ToList();

    private static List<Element> TypeElements(Document doc, BuiltInCategory category) =>
        new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsElementType()
            .ToList();

    private static IReadOnlyList<string> TypeNames(Document doc, BuiltInCategory category) =>
        TypeElements(doc, category)
            .Select(TypeDisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

    private static ElementId? ResolveElementType(Document doc, IReadOnlyList<Element> candidates,
        string? requestedName, string label, List<string> warnings)
    {
        if (candidates.Count == 0)
        {
            warnings.Add($"Project không có {label}; bỏ qua cấu hình này.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            var match = candidates.FirstOrDefault(e => TypeNameMatches(e, requestedName));
            if (match != null) return match.Id;

            warnings.Add($"Không tìm thấy {label} '{requestedName}', dùng '{TypeDisplayName(candidates[0])}'.");
        }

        return candidates[0].Id;
    }

    private static bool TypeNameMatches(Element element, string requestedName) =>
        string.Equals(element.Name, requestedName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(TypeDisplayName(element), requestedName, StringComparison.OrdinalIgnoreCase);

    private static string TypeDisplayName(Element element)
    {
        if (element is FamilySymbol symbol && !string.IsNullOrWhiteSpace(symbol.FamilyName))
            return $"{symbol.FamilyName}: {symbol.Name}";

        return element.Name;
    }
}
