using BeamDrawing.Core.Models;

namespace BeamDrawing.Core.Services;

/// <summary>Kết quả validate một <see cref="BeamDrawingSetting"/>.</summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok { get; } = new(true, []);
}

/// <summary>
///     Kiểm tra tính hợp lệ của setting trước khi generate. Logic thuần (không Revit) → test bằng xUnit.
/// </summary>
public static class SettingValidator
{
    public static ValidationResult Validate(BeamDrawingSetting setting)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(setting.Name))
            errors.Add("Tên setting không được để trống.");

        if (setting.Sectional.Scale <= 0)
            errors.Add("Scale của Sectional Elevation phải lớn hơn 0.");

        if (setting.CrossSection.Scale <= 0)
            errors.Add("Scale của Cross Section phải lớn hơn 0.");

        if (setting.Dimension.SpacingFactor <= 0)
            errors.Add("DIM spacing factor phải lớn hơn 0.");

        if (setting.Dimension.DistanceToSideBeam < 0)
            errors.Add("Distance dim to side beam không được âm.");

        if (setting.Dimension.DistanceToBotFace < 0)
            errors.Add("Distance dim to bot face không được âm.");

        return errors.Count == 0 ? ValidationResult.Ok : new ValidationResult(false, errors);
    }
}
