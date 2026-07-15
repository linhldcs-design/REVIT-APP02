using BeamRebar.Core.Models;

namespace BeamRebar.Core.Services;

/// <summary>Kết quả validate một <see cref="QuickSettingModel"/>.</summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok { get; } = new(true, []);
}

/// <summary>
///     Kiểm tra hợp lệ cấu hình Quick Setting trước khi tạo thép. Logic thuần (không Revit) → test xUnit.
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

        ValidateStirrup(model.Stirrup, errors);
        ValidateAntiBulge(model.AntiBulge, errors);
        ValidateCover(model.Cover, errors);

        return errors.Count == 0 ? ValidationResult.Ok : new ValidationResult(false, errors);
    }

    private static void ValidateMain(MainBarConfig config, string label, List<string> errors)
    {
        if (config.Count <= 0)
            errors.Add($"{label}: số thanh phải lớn hơn 0.");
        if (config.Diameter.Millimeters <= 0)
            errors.Add($"{label}: đường kính không hợp lệ.");
        if (config.AnchorLengthMm < 0)
            errors.Add($"{label}: chiều dài neo không được âm.");
    }

    private static void ValidateAdditional(AdditionalBarConfig config, string label, List<string> errors)
    {
        if (!config.Enabled) return;
        if (config.Count <= 0)
            errors.Add($"{label}: số thanh phải lớn hơn 0 khi bật.");
        if (config.Diameter.Millimeters <= 0)
            errors.Add($"{label}: đường kính không hợp lệ.");
    }

    private static void ValidateStirrup(StirrupConfig config, List<string> errors)
    {
        if (config.Diameter.Millimeters <= 0)
            errors.Add("Cốt đai: đường kính không hợp lệ.");
        if (config.SpacingEndMm <= 0)
            errors.Add("Cốt đai: bước đai A1 phải lớn hơn 0.");
        if (config.Mode == StirrupMode.TwoEnds && config.SpacingMidMm <= 0)
            errors.Add("Cốt đai: bước đai giữa A2 phải lớn hơn 0 khi phân bố 2 đầu.");
        if (config.EndZoneLengthMm < 0)
            errors.Add("Cốt đai: chiều dài vùng đai dày hai đầu không được âm.");
    }

    private static void ValidateAntiBulge(AntiBulgeConfig config, List<string> errors)
    {
        if (!config.Enabled) return;
        if (config.Diameter.Millimeters <= 0)
            errors.Add("Thép chống phình: đường kính không hợp lệ.");
        if (config.SpacingMm <= 0)
            errors.Add("Thép chống phình: khoảng cách phải lớn hơn 0.");
    }

    private static void ValidateCover(CoverSettings cover, List<string> errors)
    {
        if (cover.TopMm < 0 || cover.BottomMm < 0 || cover.SideMm < 0)
            errors.Add("Lớp bảo vệ không được âm.");
    }
}
