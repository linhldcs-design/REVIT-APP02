using Autodesk.Revit.DB;
using FootingDrawing.Addin.ViewModels;

namespace FootingDrawing.Addin.Services;

/// <summary>
///     Nạp nguồn dữ liệu combobox từ document, và resolve type theo tên khi generate.
///     Nguyên tắc: liệt kê ĐẦY ĐỦ type (không lọc theo đoán) để user tùy chọn linh hoạt.
///     Không tìm thấy theo tên → fallback + warn (không crash).
/// </summary>
public sealed class ProjectResourceProvider
{
    public ProjectResources LoadResources(Document doc) => new()
    {
        DimensionTypes = DimensionTypeNames(doc),
        RebarTagTypes = TypeNames(doc, BuiltInCategory.OST_RebarTags),
#if REVIT2025_OR_GREATER
        BendingDetailTypes = TypeNames(doc, BuiltInCategory.OST_RebarBendingDetails),
#else
        BendingDetailTypes = Array.Empty<ComboOption>(),
#endif
        ParentPlanViews = ParentPlanViews(doc),
        CalloutTypes = CalloutTypeNames(doc),
        ViewTemplates = DetailViewTemplateNames(doc),
        ViewportTypes = ViewportTypeNames(doc),
        TitleBlocks = TypeNames(doc, BuiltInCategory.OST_TitleBlocks),
        Sheets = SheetList(doc)
    };

    /// <summary>Resolve ElementType theo tên trong 1 category (dimension/tag/bending/title/viewport).</summary>
    public ElementId? ResolveType(Document doc, BuiltInCategory category, string? typeName, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return null;

        var match = category == BuiltInCategory.OST_Viewports
            ? ViewportTypes(doc).FirstOrDefault(e => e.Name == typeName)
            : new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsElementType()
                .FirstOrDefault(e => e.Name == typeName);

        if (match != null) return match.Id;
        warnings.Add($"Không tìm thấy type '{typeName}' trong {category} — bỏ qua.");
        return null;
    }

    public ElementId? ResolveDimensionType(Document doc, string? typeName, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return null;

        var match = new FilteredElementCollector(doc)
            .OfClass(typeof(DimensionType))
            .Cast<DimensionType>()
            .FirstOrDefault(type => type.Name == typeName);

        if (match != null) return match.Id;
        warnings.Add($"Không tìm thấy Dimension Type '{typeName}' — bỏ qua dimension.");
        return null;
    }

    /// <summary>Resolve ViewTemplate theo tên (null nếu không đặt hoặc không thấy → warn).</summary>
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

    public View ResolveParentPlanView(Document doc, string? viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName))
            throw new InvalidOperationException("Chưa chọn Parent Plan View cho callout.");

        return new FilteredElementCollector(doc)
                   .OfClass(typeof(View))
                   .Cast<View>()
                   .FirstOrDefault(v => !v.IsTemplate && v.Name == viewName && IsParentPlan(v))
               ?? throw new InvalidOperationException($"Không tìm thấy Parent Plan View '{viewName}'.");
    }

    public ElementId ResolveCalloutType(Document doc, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new InvalidOperationException("Chưa chọn Callout Type.");

        return new FilteredElementCollector(doc)
                   .OfClass(typeof(ViewFamilyType))
                   .Cast<ViewFamilyType>()
                   .FirstOrDefault(v => v.ViewFamily == ViewFamily.Detail && v.Name == typeName)?.Id
               ?? throw new InvalidOperationException($"Không tìm thấy Callout Type '{typeName}'.");
    }

    private static IReadOnlyList<SheetOption> SheetList(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(s => new SheetOption(s.SheetNumber, s.Name))
            .OrderBy(s => s.Number)
            .ToList();

    private static IReadOnlyList<ComboOption> TypeNames(Document doc, BuiltInCategory category)
        => new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsElementType()
            .Select(e => new ComboOption(e.Name, e.Id.ToLong().ToString()))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ComboOption> DimensionTypeNames(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(DimensionType))
            .Select(e => new ComboOption(e.Name, e.Id.ToLong().ToString()))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ComboOption> ParentPlanViews(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate && IsParentPlan(v))
            .Select(v => new ComboOption(v.Name, v.Id.ToLong().ToString()))
            .OrderBy(o => o.Name)
            .ToList();

    private static bool IsParentPlan(View view)
        => view.ViewType is ViewType.FloorPlan or ViewType.EngineeringPlan or ViewType.CeilingPlan;

    private static IReadOnlyList<ComboOption> CalloutTypeNames(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .Where(v => v.ViewFamily == ViewFamily.Detail)
            .Select(v => new ComboOption(v.Name, v.Id.ToLong().ToString()))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ComboOption> DetailViewTemplateNames(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate && v.ViewType == ViewType.Detail)
            .Select(v => new ComboOption(v.Name))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ComboOption> ViewportTypeNames(Document doc)
        => ViewportTypes(doc)
            .Select(e => new ComboOption(e.Name, e.Id.ToLong().ToString()))
            .OrderBy(o => o.Name)
            .ToList();

    private static IReadOnlyList<ElementType> ViewportTypes(Document doc)
    {
        var byCategory = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Viewports)
            .WhereElementIsElementType()
            .OfType<ElementType>()
            .ToList();
        if (byCategory.Count > 0) return byCategory;

        return new FilteredElementCollector(doc)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .SelectMany(vp => vp.GetValidTypes().Cast<ElementId>())
            .GroupBy(id => id.ToLong()).Select(group => group.First())
            .Select(id => doc.GetElement(id))
            .OfType<ElementType>()
            .ToList();
    }
}
