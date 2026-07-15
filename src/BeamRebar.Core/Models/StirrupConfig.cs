namespace BeamRebar.Core.Models;

/// <summary>Kiểu phân bố cốt đai — UI item 5.</summary>
public enum StirrupMode
{
    /// <summary>Đai đều suốt nhịp với bước <see cref="StirrupConfig.SpacingEndMm"/> (A1).</summary>
    Uniform,

    /// <summary>Đai dày hai đầu (A1) trên đoạn End1/End2, thưa ở giữa (A2). Mặc định theo TCVN.</summary>
    TwoEnds
}

/// <summary>
///     Cấu hình cốt đai — UI item 5. TwoEnds: hai đầu dày @A1 (vùng gần gối), giữa thưa @A2.
///     Uniform: đều @A1 suốt chiều dài L.
/// </summary>
public sealed record StirrupConfig
{
    public RebarDiameter Diameter { get; init; } = new(6);

    public StirrupMode Mode { get; init; } = StirrupMode.TwoEnds;

    /// <summary>Bước đai vùng dày hai đầu (A1), mm. Cũng là bước đai khi Uniform.</summary>
    public double SpacingEndMm { get; init; } = 150;

    /// <summary>Bước đai vùng giữa (A2), mm. Chỉ dùng khi TwoEnds.</summary>
    public double SpacingMidMm { get; init; } = 200;

    /// <summary>Chiều dài đoạn đai dày mỗi đầu (mm). 0 → tự suy = L/4 mỗi đầu ở Phase 4.</summary>
    public double EndZoneLengthMm { get; init; }

    /// <summary>Chiều dài tham chiếu cho chế độ Uniform (L), mm. Phase 4 dùng chiều dài span thực tế.</summary>
    public double UniformLengthMm { get; init; } = 1000;
}
