using RevitAPP.Core.Models.FootingSection;

namespace RevitAPP.Core.Services;

/// <summary>
///     Kiểm tra cấu hình mặt cắt móng trước khi sinh view. Trả danh sách lỗi tiếng Việt (rỗng = hợp lệ).
/// </summary>
public static class FootingSectionSettingValidator
{
    public static IReadOnlyList<string> Validate(FootingSectionSetting setting)
    {
        var errors = new List<string>();

        if (setting.Scale <= 0)
            errors.Add("Tỉ lệ mặt cắt phải lớn hơn 0.");

        if (string.IsNullOrWhiteSpace(setting.Sheet.Number))
            errors.Add("Số hiệu sheet không được để trống.");

        if (string.IsNullOrWhiteSpace(setting.Sheet.Name))
            errors.Add("Tên sheet không được để trống.");

        return errors;
    }
}
