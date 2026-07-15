using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services.Rebar;

/// <summary>
///     Tra cứu &amp; xác thực họ thép trong document: <see cref="RebarBarType"/> theo đường kính và
///     <see cref="RebarHookType"/> theo góc móc. Document trống thì <see cref="Validate"/> trả lỗi tiếng
///     Việt rõ ràng để người dùng load họ thép trước khi tạo.
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

    /// <summary>Trả về danh sách lỗi (rỗng = hợp lệ) cho mọi đường kính/móc mà model sử dụng.</summary>
    public IReadOnlyList<string> Validate(QuickSettingModel model)
    {
        var errors = new List<string>();
        foreach (var mm in CollectDiameters(model))
            if (!_barTypesByMm.ContainsKey(mm))
                errors.Add($"Document thiếu RebarBarType D{mm} — hãy load họ thép đường kính {mm}mm.");

        if (UsesHook(model) && _hookTypes.Count == 0)
            errors.Add("Document thiếu RebarHookType — hãy load họ móc neo trước khi bật móc.");

        return errors;
    }

    public RebarBarType? GetBarType(RebarDiameter diameter)
        => _barTypesByMm.GetValueOrDefault(diameter.Millimeters);

    /// <summary>Chọn RebarHookType gần đúng góc yêu cầu; null nếu document không có móc nào.</summary>
    public RebarHookType? GetHookType(HookAngle angle)
    {
        if (_hookTypes.Count == 0) return null;

        var target = angle switch
        {
            HookAngle.Deg90 => 90.0,
            HookAngle.Deg135 => 135.0,
            HookAngle.Deg180 => 180.0,
            _ => 135.0
        };

        // Chọn móc có góc gần target nhất (HookAngle lưu bằng radian trong API).
        return _hookTypes
            .OrderBy(h => Math.Abs(h.HookAngle * 180.0 / Math.PI - target))
            .First();
    }

    private static IEnumerable<int> CollectDiameters(QuickSettingModel model)
    {
        var set = new HashSet<int>
        {
            model.MainTop.Diameter.Millimeters,
            model.MainBottom.Diameter.Millimeters,
            model.Stirrup.Diameter.Millimeters
        };
        foreach (var a in Additionals(model))
            if (a.Enabled) set.Add(a.Diameter.Millimeters);
        if (model.AntiBulge.Enabled)
        {
            set.Add(model.AntiBulge.Diameter.Millimeters);
            set.Add(model.AntiBulge.TieDiameter.Millimeters);
        }

        foreach (var ov in model.SpanOverrides)
        {
            if (ov.MainTop is { } mt) set.Add(mt.Diameter.Millimeters);
            if (ov.MainBottom is { } mb) set.Add(mb.Diameter.Millimeters);
            if (ov.Stirrup is { } st) set.Add(st.Diameter.Millimeters);
            if (ov.TopAdditional is { Enabled: true } ta) set.Add(ta.Diameter.Millimeters);
            if (ov.BottomAdditional is { Enabled: true } ba) set.Add(ba.Diameter.Millimeters);
        }

        return set;
    }

    private static IEnumerable<AdditionalBarConfig> Additionals(QuickSettingModel m)
    {
        yield return m.TopAdditional;
        yield return m.TopAdditionalLayer2;
        foreach (var item in m.TopAdditionalItems)
            yield return item;
        yield return m.BottomAdditional;
        yield return m.BottomAdditionalLayer2;
        foreach (var item in m.BottomAdditionalItems)
            yield return item;
    }

    private static bool UsesHook(QuickSettingModel model)
    {
        if (model.MainTop.HookStart.Enabled || model.MainTop.HookEnd.Enabled) return true;
        if (model.MainBottom.HookStart.Enabled || model.MainBottom.HookEnd.Enabled) return true;
        foreach (var ov in model.SpanOverrides)
        {
            if (ov.MainTop is { } mt && (mt.HookStart.Enabled || mt.HookEnd.Enabled)) return true;
            if (ov.MainBottom is { } mb && (mb.HookStart.Enabled || mb.HookEnd.Enabled)) return true;
        }

        return false;
    }
}
