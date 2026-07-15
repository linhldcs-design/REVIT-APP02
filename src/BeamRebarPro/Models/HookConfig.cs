namespace BeamRebarPro.Models;

/// <summary>Góc uốn móc neo theo TCVN — quyết định chọn RebarHookType tương ứng trong document.</summary>
public enum HookAngle
{
    Deg90,
    Deg135,
    Deg180
}

/// <summary>
///     Cấu hình móc neo ở một đầu thanh thép dọc. <see cref="Enabled"/> = false → đầu thanh để thẳng
///     (truyền hookType null vào CreateFromCurves). LengthMm = 0 → Revit suy chiều dài từ RebarHookType.
/// </summary>
public sealed record HookConfig
{
    public bool Enabled { get; init; }

    public HookAngle Angle { get; init; } = HookAngle.Deg135;

    /// <summary>Chiều dài đoạn móc (mm). 0 → dùng chiều dài mặc định của RebarHookType.</summary>
    public double LengthMm { get; init; }
}
