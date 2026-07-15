using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamRebar.Core.Models;

namespace BeamRebar.Addin.Services.Rebar;

/// <summary>
///     Kiểm tra document có sẵn <see cref="RebarBarType"/> cho mọi đường kính dùng trong cấu hình,
///     và ít nhất một <see cref="RebarHookType"/>. Document trống → <see cref="Rebar"/>.CreateFromCurves
///     sẽ fail; validate trước để báo lỗi tiếng Việt rõ ràng thay vì để Revit throw.
/// </summary>
public sealed class RebarFamilyValidator
{
    private readonly Dictionary<int, RebarBarType> _barTypesByMm = new();

    public RebarFamilyValidator(Document document)
    {
        foreach (var barType in new FilteredElementCollector(document)
                     .OfClass(typeof(RebarBarType))
                     .Cast<RebarBarType>())
        {
            var mm = (int)Math.Round(UnitUtils.ConvertFromInternalUnits(barType.BarModelDiameter, UnitTypeId.Millimeters));
            _barTypesByMm.TryAdd(mm, barType);
        }
    }

    /// <summary>Lấy RebarBarType khớp đường kính (mm); null nếu document thiếu.</summary>
    public RebarBarType? GetBarType(RebarDiameter diameter)
        => _barTypesByMm.GetValueOrDefault(diameter.Millimeters);

    /// <summary>
    ///     Trả danh sách lỗi tiếng Việt nếu thiếu RebarBarType cho bất kỳ đường kính nào trong cấu hình.
    /// </summary>
    public IReadOnlyList<string> Validate(QuickSettingModel model)
    {
        var errors = new List<string>();
        var needed = CollectDiameters(model);

        foreach (var mm in needed)
            if (!_barTypesByMm.ContainsKey(mm))
                errors.Add($"Document thiếu RebarBarType đường kính D{mm}. Hãy load loại thép D{mm} trước khi tạo.");

        if (_barTypesByMm.Count == 0)
            errors.Add("Document chưa có loại thép (RebarBarType) nào. Hãy load family thép trước.");

        return errors;
    }

    private static SortedSet<int> CollectDiameters(QuickSettingModel m)
    {
        var set = new SortedSet<int> { m.MainTop.Diameter.Millimeters, m.MainBottom.Diameter.Millimeters, m.Stirrup.Diameter.Millimeters };
        AddIf(set, m.TopAdditional);
        AddIf(set, m.TopAdditionalLayer2);
        AddIf(set, m.BottomAdditional);
        AddIf(set, m.BottomAdditionalLayer2);
        if (m.AntiBulge.Enabled) set.Add(m.AntiBulge.Diameter.Millimeters);
        return set;
    }

    private static void AddIf(SortedSet<int> set, AdditionalBarConfig config)
    {
        if (config.Enabled) set.Add(config.Diameter.Millimeters);
    }
}
