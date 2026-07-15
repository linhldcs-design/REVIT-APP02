namespace BeamRebarPro.Models;

/// <summary>Kiểu phân bố cốt đai.</summary>
public enum StirrupMode
{
    /// <summary>Đai đều suốt nhịp với bước <see cref="StirrupConfig.SpacingEndMm"/> (A1).</summary>
    Uniform,

    /// <summary>Đai dày hai đầu (A1) trên đoạn End1/End2, thưa ở giữa (A2). Mặc định theo TCVN.</summary>
    TwoEnds
}

/// <summary>
///     Cấu hình cốt đai. TwoEnds: hai đầu dày @A1 (vùng gần gối), giữa thưa @A2.
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

    /// <summary>Chiều dài đoạn đai dày mỗi đầu (mm). 0 → tự suy = L/4 mỗi đầu khi tạo.</summary>
    public double EndZoneLengthMm { get; init; }
    public double EndZoneStartMm { get; init; }
    public double EndZoneEndMm { get; init; }

    /// <summary>Khoảng cách từ mép cột/gối đến đai đầu tiên (mm).</summary>
    public double FirstDistanceFromSupportMm { get; init; } = 50;

    /// <summary>Các đai phụ (đai con chữ nhật ôm các thanh chủ giữa), rải cùng bước/vùng với đai chính.</summary>
    public IReadOnlyList<AdditionalStirrupConfig> AdditionalStirrups { get; init; } = [];
}

/// <summary>Kiểu đai phụ (Additional Stirrup) — 2 icon trong video.</summary>
public enum AdditionalStirrupType
{
    /// <summary>Đai MÓC C (C-stirrup): chữ C hở 1 cạnh, 2 đầu có móc 135°, ôm các thanh từ Start→End.</summary>
    CHook,

    /// <summary>Đai LỒNG KÍN (closed): khung chữ nhật kín, ôm các thanh từ Start→End.</summary>
    Closed
}

/// <summary>
///     Đai phụ: đai chữ nhật con LỒNG TRONG đai chính, ôm các thanh chủ giữa theo phương ngang tiết diện —
///     giữ thanh chủ giữa khỏi phình (TCVN khi >3 thanh/lớp). <see cref="Type"/> chọn cách bố trí (như video).
/// </summary>
public sealed record AdditionalStirrupConfig
{
    public RebarDiameter Diameter { get; init; } = new(8);

    public AdditionalStirrupType Type { get; init; } = AdditionalStirrupType.Closed;

    /// <summary>Thanh chủ bắt đầu ôm (1-based như video, thanh 1 = trái cùng). Vd Start=2,End=3 → ôm thanh 2,3.</summary>
    public int StartBar { get; init; } = 2;

    /// <summary>Thanh chủ kết thúc ôm (1-based).</summary>
    public int EndBar { get; init; } = 3;

    public bool Enabled { get; init; } = true;
}
