namespace BeamRebar.Core.Models;

/// <summary>
///     Cấu hình thép ghi đè cho một nhịp cụ thể (UI per-span). Mỗi field nullable: null → kế thừa
///     <see cref="QuickSettingModel"/> chung (giữ tương thích v1, per-span chỉ là lớp phủ).
/// </summary>
public sealed record SpanRebarOverride
{
    /// <summary>Chỉ số nhịp trong BeamRun (0-based) mà override này áp dụng.</summary>
    public int SpanIndex { get; init; }

    public MainBarConfig? MainTop { get; init; }
    public MainBarConfig? MainBottom { get; init; }

    public AdditionalBarConfig? TopAdditional { get; init; }
    public AdditionalBarConfig? BottomAdditional { get; init; }

    public StirrupConfig? Stirrup { get; init; }
}
