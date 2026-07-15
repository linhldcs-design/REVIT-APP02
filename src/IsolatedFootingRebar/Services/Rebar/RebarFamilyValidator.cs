using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using IsolatedFootingRebar.Models;

namespace IsolatedFootingRebar.Services.Rebar;

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
    public IReadOnlyList<string> Validate(FootingRebarModel model)
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
            _ => 90.0
        };

        // Chọn móc có góc gần target nhất (HookAngle lưu bằng radian trong API).
        return _hookTypes
            .OrderBy(h => Math.Abs(h.HookAngle * 180.0 / Math.PI - target))
            .First();
    }

    private static IEnumerable<int> CollectDiameters(FootingRebarModel model)
    {
        var set = new HashSet<int>();

        if (model.BottomEnabled)
        {
            if (model.BottomX.Enabled) set.Add(model.BottomX.Diameter.Millimeters);
            if (model.BottomY.Enabled) set.Add(model.BottomY.Diameter.Millimeters);
        }

        if (model.TopEnabled)
        {
            if (model.TopX.Enabled) set.Add(model.TopX.Diameter.Millimeters);
            if (model.TopY.Enabled) set.Add(model.TopY.Diameter.Millimeters);
        }

        if (model.MidEnabled)
        {
            if (model.MidX.Enabled) set.Add(model.MidX.Diameter.Millimeters);
            if (model.MidY.Enabled) set.Add(model.MidY.Diameter.Millimeters);
        }

        if (model.VerticalEnabled)
            set.Add(model.Vertical.Diameter.Millimeters);

        if (model.HorizontalEnabled)
        {
            set.Add(model.Horizontal.DiameterX.Millimeters);
            set.Add(model.Horizontal.DiameterY.Millimeters);
        }

        return set;
    }

    /// <summary>Móng dùng móc khi: lưới bottom/top/mid bật hook, hoặc thép đứng cổ móng (luôn có móc chân),
    ///     hoặc đai ngang (đai kín có móc 135°).</summary>
    private static bool UsesHook(FootingRebarModel model)
    {
        if (model.BottomEnabled && (model.BottomX.HookEnabled || model.BottomY.HookEnabled)) return true;
        if (model.TopEnabled && (model.TopX.HookEnabled || model.TopY.HookEnabled)) return true;
        if (model.MidEnabled && (model.MidX.HookEnabled || model.MidY.HookEnabled)) return true;
        if (model.VerticalEnabled && model.Vertical.HookLengthMm > 0) return true;
        if (model.HorizontalEnabled) return true;
        return false;
    }
}
