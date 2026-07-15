using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using WallRebar.Models;

namespace WallRebar.Services.Rebar;

/// <summary>
///     Tra cứu &amp; xác thực họ thép trong document: <see cref="RebarBarType"/> theo đường kính.
///     Document thiếu họ thép → <see cref="Validate"/> trả lỗi tiếng Việt rõ ràng để người dùng load trước.
/// </summary>
public sealed class RebarFamilyValidator
{
    private readonly Dictionary<int, RebarBarType> _barTypesByMm = new();
    private readonly List<RebarHookType> _hookTypes;

    public RebarFamilyValidator(Document document)
    {
        foreach (var bt in new FilteredElementCollector(document).OfClass(typeof(RebarBarType)).Cast<RebarBarType>())
        {
            var mm = (int)Math.Round(bt.BarNominalDiameter * 304.8);
            _barTypesByMm.TryAdd(mm, bt);
        }

        _hookTypes = new FilteredElementCollector(document)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>().ToList();
    }

    /// <summary>Trả về danh sách lỗi (rỗng = hợp lệ) cho mọi đường kính mà model sử dụng.</summary>
    public IReadOnlyList<string> Validate(WallRebarModel model)
    {
        var errors = new List<string>();
        foreach (var mm in CollectDiameters(model))
            if (!_barTypesByMm.ContainsKey(mm))
                errors.Add($"Document thiếu RebarBarType D{mm} — hãy load họ thép đường kính {mm}mm.");

        if (model.Tie.Enabled && Get180HookType() == null)
            errors.Add("Document thiếu RebarHookType 180° — hãy load đúng họ móc 180° để tạo thép tie.");

        return errors;
    }

    public RebarBarType? GetBarType(RebarDiameter diameter)
        => _barTypesByMm.GetValueOrDefault(diameter.Millimeters);

    public RebarHookType? Get180HookType()
        => _hookTypes
            .Where(h => Math.Abs(h.HookAngle * 180.0 / Math.PI - 180.0) <= 1.0)
            .OrderBy(h => Math.Abs(h.HookAngle * 180.0 / Math.PI - 180.0))
            .FirstOrDefault();

    private static IEnumerable<int> CollectDiameters(WallRebarModel model)
    {
        var set = new HashSet<int>();
        if (model.Vertical.Enabled) set.Add(model.Vertical.Diameter.Millimeters);
        if (model.Horizontal.Enabled) set.Add(model.Horizontal.Diameter.Millimeters);
        if (model.Tie.Enabled) set.Add(model.Tie.Diameter.Millimeters);
        return set;
    }
}
