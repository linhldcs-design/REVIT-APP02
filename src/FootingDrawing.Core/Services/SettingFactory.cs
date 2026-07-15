using FootingDrawing.Core.Models;

namespace FootingDrawing.Core.Services;

/// <summary>Tạo setting mặc định khi mở dialog lần đầu (chưa có setting nào lưu).</summary>
public static class SettingFactory
{
    public static FootingDrawingSetting CreateDefault() => new()
    {
        Name = "MB-Móng-M",
        Scale = 25
    };
}
