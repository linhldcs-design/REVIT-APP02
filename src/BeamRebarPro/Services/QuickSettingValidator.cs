using BeamRebarPro.Models;

namespace BeamRebarPro.Services;

/// <summary>Kết quả validate một <see cref="QuickSettingModel"/>.</summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok { get; } = new(true, []);
}

/// <summary>
///     Kiểm tra hợp lệ cấu hình Quick Setting (kể cả per-span override) trước khi tạo thép.
///     Logic thuần (không Revit) → test xUnit.
/// </summary>
public static class QuickSettingValidator
{
    public static ValidationResult Validate(QuickSettingModel model)
    {
        var errors = new List<string>();

        ValidateMain(model.MainTop, "Thép chủ trên", errors);
        ValidateMain(model.MainBottom, "Thép chủ dưới", errors);

        ValidateAdditional(model.TopAdditional, "Thép gia cường trên lớp 1", errors);
        ValidateAdditional(model.TopAdditionalLayer2, "Thép gia cường trên lớp 2", errors);
        ValidateAdditional(model.BottomAdditional, "Thép gia cường dưới lớp 1", errors);
        ValidateAdditional(model.BottomAdditionalLayer2, "Thép gia cường dưới lớp 2", errors);

        ValidateStirrup(model.Stirrup, "Cốt đai", errors);
        for (var i = 0; i < model.TopAdditionalItems.Count; i++)
            ValidateAdditional(model.TopAdditionalItems[i], $"Top additional item {i + 1}", errors);
        for (var i = 0; i < model.BottomAdditionalItems.Count; i++)
            ValidateAdditional(model.BottomAdditionalItems[i], $"Bottom additional item {i + 1}", errors);
        ValidateAntiBulge(model.AntiBulge, errors);
        ValidateCover(model.Cover, errors);

        foreach (var ov in model.SpanOverrides)
            ValidateOverride(ov, errors);

        return errors.Count == 0 ? ValidationResult.Ok : new ValidationResult(false, errors);
    }

    private static void ValidateMain(MainBarConfig config, string label, List<string> errors)
    {
        if (!config.Enabled) return;
        if (config.Count <= 0)
            errors.Add($"{label}: số thanh phải lớn hơn 0.");
        if (config.Diameter.Millimeters <= 0)
            errors.Add($"{label}: đường kính không hợp lệ.");
        if (config.AnchorLengthMm < 0)
            errors.Add($"{label}: chiều dài neo không được âm.");
        if (config.StartPointIndex < 0 || config.EndPointIndex < 0 || config.EndPointIndex <= config.StartPointIndex)
            errors.Add($"{label}: Start Point/End Point không hợp lệ.");
        if (config.AnchorXLeftMm < 0 || config.AnchorXRightMm < 0)
            errors.Add($"{label}: chiều dài neo ngang không được âm.");
        if (config.TopEndBendDownLengthMm < 0)
            errors.Add($"{label}: chiều dài đoạn bẻ xuống không được âm.");
        if (config.HookStart.LengthMm < 0 || config.HookEnd.LengthMm < 0)
            errors.Add($"{label}: chiều dài móc neo không được âm.");
    }

    private static void ValidateAdditional(AdditionalBarConfig config, string label, List<string> errors)
    {
        if (!config.Enabled) return;
        if (config.Count <= 0)
            errors.Add($"{label}: số thanh phải lớn hơn 0 khi bật.");
        if (config.Diameter.Millimeters <= 0)
            errors.Add($"{label}: đường kính không hợp lệ.");
        if (config.LengthPercent is < 0 or > 100)
            errors.Add($"{label}: phần trăm chiều dài phải trong khoảng 0–100.");
        if (config.StartPointIndex < 0 || config.EndPointIndex < 0 || config.EndPointIndex < config.StartPointIndex)
            errors.Add($"{label}: Start Point/End Point không hợp lệ.");
        if (config.LeftRatio < 0 || config.RightRatio < 0)
            errors.Add($"{label}: tỷ lệ trái/phải không được âm.");
        if (config.LeftLengthMm < 0 || config.RightLengthMm < 0 || config.DLeftMm < 0 || config.DRightMm < 0)
            errors.Add($"{label}: chiều dài trái/phải hoặc D trái/phải không được âm.");
        if (config.EdgeHookDownLengthMm < 0)
            errors.Add($"{label}: chiều dài móc bẻ xuống không được âm.");
    }

    private static void ValidateStirrup(StirrupConfig config, string label, List<string> errors)
    {
        if (config.Diameter.Millimeters <= 0)
            errors.Add($"{label}: đường kính không hợp lệ.");
        if (config.SpacingEndMm <= 0)
            errors.Add($"{label}: bước đai A1 phải lớn hơn 0.");
        if (config.Mode == StirrupMode.TwoEnds && config.SpacingMidMm <= 0)
            errors.Add($"{label}: bước đai giữa A2 phải lớn hơn 0 khi phân bố 2 đầu.");
        if (config.EndZoneLengthMm < 0)
            errors.Add($"{label}: chiều dài vùng đai dày hai đầu không được âm.");
        if (config.EndZoneStartMm < 0 || config.EndZoneEndMm < 0)
            errors.Add($"{label}: End 1/End 2 khong duoc am.");
        if (config.FirstDistanceFromSupportMm < 0)
            errors.Add($"{label}: khoảng cách đai đầu tiên tới cột không được âm.");
    }

    private static void ValidateAntiBulge(AntiBulgeConfig config, List<string> errors)
    {
        if (!config.Enabled) return;
        if (config.Diameter.Millimeters <= 0)
            errors.Add("Thép chống phình: đường kính không hợp lệ.");
        if (config.Count <= 0)
            errors.Add("Thep chong phinh: so thanh phai lon hon 0.");
        if (config.TieDiameter.Millimeters <= 0)
            errors.Add("Thep chong phinh: duong kinh tie khong hop le.");
        if (config.SpacingMm <= 0)
            errors.Add("Thép chống phình: khoảng cách phải lớn hơn 0.");
    }

    private static void ValidateCover(CoverSettings cover, List<string> errors)
    {
        if (cover.TopMm < 0 || cover.BottomMm < 0 || cover.SideMm < 0)
            errors.Add("Lớp bảo vệ không được âm.");
    }

    private static void ValidateOverride(SpanRebarOverride ov, List<string> errors)
    {
        var label = $"Nhịp {ov.SpanIndex}";
        if (ov.MainTop is { } mt) ValidateMain(mt, $"{label} – thép chủ trên", errors);
        if (ov.MainBottom is { } mb) ValidateMain(mb, $"{label} – thép chủ dưới", errors);
        if (ov.TopAdditional is { } ta) ValidateAdditional(ta, $"{label} – gia cường trên", errors);
        if (ov.BottomAdditional is { } ba) ValidateAdditional(ba, $"{label} – gia cường dưới", errors);
        if (ov.Stirrup is { } st) ValidateStirrup(st, $"{label} – cốt đai", errors);
    }
}
