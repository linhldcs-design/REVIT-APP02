using RevitAPP.Core.Models.BeamDrawing;

namespace RevitAPP.Core.Services;

/// <summary>
///     Kiểm tra cấu hình bản vẽ dầm trước khi sinh view. Trả danh sách lỗi tiếng Việt (rỗng = hợp lệ).
/// </summary>
public static class BeamDrawingSettingValidator
{
    public static IReadOnlyList<string> Validate(BeamDrawingSetting setting)
    {
        var errors = new List<string>();

        if (setting.Sectional.Scale <= 0)
            errors.Add("Tỉ lệ mặt cắt dọc phải lớn hơn 0.");

        if (setting.Flags.CrossSection && setting.CrossSection.Scale <= 0)
            errors.Add("Tỉ lệ mặt cắt ngang phải lớn hơn 0.");

        if (setting.Dim.Enabled && setting.Dim.SpacingFactor <= 0)
            errors.Add("Dim spacing factor phải lớn hơn 0.");

        if (string.IsNullOrWhiteSpace(setting.Sheet.Number))
            errors.Add("Số hiệu sheet không được để trống.");

        if (string.IsNullOrWhiteSpace(setting.Sheet.Name))
            errors.Add("Tên sheet không được để trống.");

        return errors;
    }
}
