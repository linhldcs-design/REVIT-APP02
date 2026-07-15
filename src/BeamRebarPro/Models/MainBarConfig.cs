namespace BeamRebarPro.Models;

/// <summary>
///     Cấu hình thép chủ một lớp (top hoặc bottom). Thép chạy suốt chiều dài nhịp/dầm, neo vào gối
///     hai đầu; mỗi đầu có thể uốn móc (<see cref="HookStart"/>/<see cref="HookEnd"/>).
/// </summary>
public sealed record MainBarConfig
{
    public bool Enabled { get; init; } = true;

    /// <summary>Số thanh trên một lớp (vd 3 → 3xD16).</summary>
    public int Count { get; init; } = 3;

    public RebarDiameter Diameter { get; init; } = new(16);

    /// <summary>Chiều dài neo vào gối mỗi đầu (mm).</summary>
    public double AnchorLengthMm { get; init; }

    public double AnchorLeftMm { get; init; }

    public double AnchorRightMm { get; init; }

    /// <summary>Điểm bắt đầu theo chỉ số support/detail point. 0 = đầu trái run.</summary>
    public int StartPointIndex { get; init; }

    /// <summary>Điểm kết thúc theo chỉ số support/detail point. int.MaxValue = cuối run.</summary>
    public int EndPointIndex { get; init; } = int.MaxValue;

    /// <summary>Chiều dài neo ngang từ Start Point vào trong dầm (mm).</summary>
    public double AnchorXLeftMm { get; init; }

    /// <summary>Chiều dài neo ngang từ End Point vào trong dầm (mm).</summary>
    public double AnchorXRightMm { get; init; }

    /// <summary>
    /// Chiều dài đoạn bẻ xuống ở hai đầu thép chủ lớp trên (mm). 0 = để thẳng / dùng hook cũ nếu bật.
    /// </summary>
    public double TopEndBendDownLengthMm { get; init; }

    public string PositionInSection { get; init; } = string.Empty;

    /// <summary>Móc neo đầu thanh (phía gối đầu). Disabled → để thẳng.</summary>
    public HookConfig HookStart { get; init; } = new();

    /// <summary>Móc neo cuối thanh (phía gối cuối). Disabled → để thẳng.</summary>
    public HookConfig HookEnd { get; init; } = new();
}
