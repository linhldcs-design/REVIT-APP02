namespace RevitAPP.Core.Models.FootingSection;

/// <summary>
///     Cờ bật/tắt các thành phần khi sinh mặt cắt móng + tên view (null = tự đặt theo Mark móng).
/// </summary>
public sealed record FootingSectionFlags(
    bool TagEnabled,
    bool DimEnabled,
    bool BreakLineEnabled,
    string? ViewName = null);
