using FootingDrawing.Core.Models;
using FootingDrawing.Core.Services;
using Xunit;

namespace FootingDrawing.Tests;

public class SettingValidatorTests
{
    [Fact]
    public void Validate_ValidSetting_Passes()
    {
        var setting = new FootingDrawingSetting { Name = "MB-M3", Scale = 25 };
        var result = SettingValidator.Validate(setting);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        var setting = new FootingDrawingSetting { Name = "  ", Scale = 25 };
        var result = SettingValidator.Validate(setting);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Tên"));
    }

    [Fact]
    public void Validate_ZeroScale_Fails()
    {
        var setting = new FootingDrawingSetting { Name = "MB-M3", Scale = 0 };
        var result = SettingValidator.Validate(setting);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Tỉ lệ"));
    }

    [Fact]
    public void Validate_MissingTypes_StillPasses()
    {
        // Nguyên tắc: user tùy chọn linh hoạt — thiếu type KHÔNG chặn validate (chỉ warn khi chạy).
        var setting = new FootingDrawingSetting { Name = "MB-M3", Scale = 25 };
        var result = SettingValidator.Validate(setting);
        Assert.True(result.IsValid);
    }
}
