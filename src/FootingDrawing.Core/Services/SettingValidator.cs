using FootingDrawing.Core.Models;

namespace FootingDrawing.Core.Services;

/// <summary>Kết quả validate một setting.</summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok() => new(true, []);
    public static ValidationResult Fail(params string[] errors) => new(false, errors);
}

/// <summary>
///     Validate <see cref="FootingDrawingSetting"/> — chỉ ràng buộc tối thiểu (Name, Scale). KHÔNG bắt
///     buộc chọn đủ type: user tùy chọn linh hoạt, thiếu type nào thì bỏ qua thành phần đó + warn khi chạy.
/// </summary>
public static class SettingValidator
{
    public static ValidationResult Validate(FootingDrawingSetting setting)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(setting.Name))
            errors.Add("Tên setting không được để trống.");

        if (setting.Scale <= 0)
            errors.Add("Tỉ lệ (Scale) phải lớn hơn 0.");

        return errors.Count == 0 ? ValidationResult.Ok() : new ValidationResult(false, errors);
    }
}
