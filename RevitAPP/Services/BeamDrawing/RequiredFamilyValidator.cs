using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Kiểm tra family/type bắt buộc cho annotation theo Flags. Trả danh sách cảnh báo (không chặn),
///     để command hiện rõ project thiếu gì trước khi sinh bản vẽ.
/// </summary>
public sealed class RequiredFamilyValidator
{
    public IReadOnlyList<string> FindMissing(Document doc, BeamDrawingSetting setting)
    {
        var warnings = new List<string>();

        if (FindRebarTagTypeId(doc) == null)
            warnings.Add("Project chưa có Rebar Tag — tag thép sẽ bị bỏ qua.");

        if (setting.Spot.Enabled && new FilteredElementCollector(doc)
                .OfClass(typeof(SpotDimensionType)).Any() == false)
            warnings.Add("Project chưa có Spot Elevation type — cao độ sẽ bị bỏ qua.");

        if (setting.Dim.Enabled && new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Any() == false)
            warnings.Add("Project chưa có Dimension type — dimension sẽ bị bỏ qua.");

        return warnings;
    }

    public static ElementId? FindRebarTagTypeId(Document doc)
    {
        var tag = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_RebarTags)
            .WhereElementIsElementType()
            .FirstElement();
        return tag?.Id;
    }

    private static bool HasType(Document doc, BuiltInCategory category) =>
        new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsElementType()
            .FirstElement() != null;
}
