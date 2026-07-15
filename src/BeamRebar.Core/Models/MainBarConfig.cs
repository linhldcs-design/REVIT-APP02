namespace BeamRebar.Core.Models;

/// <summary>
///     Cấu hình thép chủ một lớp (top hoặc bottom) — UI item 2 (Main Top 1) và item 6 (Main Bottom 1).
///     Thép chạy suốt chiều dài dầm/nhịp, neo vào gối hai đầu.
/// </summary>
public sealed record MainBarConfig
{
    /// <summary>Số thanh trên một lớp (vd 3 → 3xD16).</summary>
    public int Count { get; init; } = 3;

    public RebarDiameter Diameter { get; init; } = new(16);

    /// <summary>Chiều dài neo vào gối mỗi đầu (mm).</summary>
    public double AnchorLengthMm { get; init; } = 300;

    /// <summary>Móc neo đầu thanh (phía gối đầu). Disabled → để thẳng như v1.</summary>
    public HookConfig HookStart { get; init; } = new();

    /// <summary>Móc neo cuối thanh (phía gối cuối). Disabled → để thẳng như v1.</summary>
    public HookConfig HookEnd { get; init; } = new();
}
