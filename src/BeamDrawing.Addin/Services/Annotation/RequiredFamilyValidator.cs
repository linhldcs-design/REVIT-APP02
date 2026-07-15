using Autodesk.Revit.DB;
using BeamDrawing.Core.Models;

namespace BeamDrawing.Addin.Services.Annotation;

/// <summary>
///     Đối chiếu các loadable family bắt buộc (tag, dimension, break-line, title block) trong setting
///     với project. Trả danh sách thiếu để orchestrator warn — KHÔNG chặn cứng (vẫn generate phần có thể).
/// </summary>
public sealed class RequiredFamilyValidator
{
    public IReadOnlyList<string> FindMissing(Document doc, BeamDrawingSetting setting)
    {
        var missing = new List<string>();

        // Tag types: tất cả tên tag được dùng trong mapping.
        var tagNames = CollectTagNames(setting.TagMapping);
        var availableTags = TypeNames(doc, BuiltInCategory.OST_RebarTags);
        foreach (var name in tagNames.Where(n => !availableTags.Contains(n)))
            missing.Add($"Tag type '{name}' không có trong project.");

        if (!string.IsNullOrWhiteSpace(setting.BreakLine.BreakLineFamilyName) &&
            !TypeNames(doc, BuiltInCategory.OST_DetailComponents).Contains(setting.BreakLine.BreakLineFamilyName))
            missing.Add($"Break-line family '{setting.BreakLine.BreakLineFamilyName}' không có trong project.");

        if (!string.IsNullOrWhiteSpace(setting.TitleBlockName) &&
            !TypeNames(doc, BuiltInCategory.OST_TitleBlocks).Contains(setting.TitleBlockName))
            missing.Add($"Title block '{setting.TitleBlockName}' không có trong project.");

        return missing;
    }

    private static HashSet<string> CollectTagNames(RebarTagMapping m)
    {
        var names = new[]
        {
            m.T1TagTypeName, m.T2TagTypeName, m.Item4TagTypeName,
            m.D0TagTypeName, m.D1TagTypeName, m.D2TagTypeName,
            m.D3TagTypeName, m.D4TagTypeName, m.D5TagTypeName
        };
        return names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToHashSet();
    }

    private static HashSet<string> TypeNames(Document doc, BuiltInCategory category)
        => new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsElementType()
            .Select(e => e.Name)
            .ToHashSet();
}
