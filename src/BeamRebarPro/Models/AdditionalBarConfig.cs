namespace BeamRebarPro.Models;

/// <summary>Vùng đặt thép gia cường, quyết định cách cắt thanh theo % nhịp.</summary>
public enum AdditionalBarSide
{
    /// <summary>Quanh gối (thép trên chịu mô men âm) — cắt đối xứng qua gối.</summary>
    TopAtSupport,

    /// <summary>Giữa nhịp (thép dưới chịu mô men dương) — căn giữa nhịp.</summary>
    BottomAtMidspan
}

/// <summary>
///     Thép gia cường (additional bar) đặt thêm tại vùng mô men lớn: gối với thép trên, giữa nhịp với
///     thép dưới. <see cref="Enabled"/> = false → bỏ qua. Hỗ trợ 2 lớp qua <see cref="Layer"/>.
///     Cắt theo <see cref="LengthPercent"/> (% nhịp); 0 → chạy suốt nhịp.
/// </summary>
public sealed record AdditionalBarConfig
{
    public bool Enabled { get; init; }

    public int Count { get; init; } = 1;

    public RebarDiameter Diameter { get; init; } = new(16);

    /// <summary>Lớp thép (1 hoặc 2). Layer 2 nằm phía trong Layer 1.</summary>
    public int Layer { get; init; } = 1;

    public int StartPointIndex { get; init; }

    public int EndPointIndex { get; init; } = int.MaxValue;

    public string StartType { get; init; } = "Attached to column";

    public string EndType { get; init; } = "Attached to column";

    public double LeftRatio { get; init; }

    public double RightRatio { get; init; }

    public double LeftLengthMm { get; init; }

    public double RightLengthMm { get; init; }

    public double DLeftMm { get; init; }

    public double DRightMm { get; init; }

    /// <summary>(Legacy/Quick Setting) chiều dài thanh theo % nhịp. Ưu tiên thấp hơn <see cref="LengthMm"/>.</summary>
    public double LengthPercent { get; init; }

    /// <summary>
    ///     Chiều dài thép gia cường tính tuyệt đối (mm). Top: chiều dài MỖI BÊN gối. Bottom: chiều dài
    ///     đoạn giữa nhịp. = 0 → tự tính theo TCVN (top 0.25L mỗi bên, bottom 1/8..6/8 L). Ưu tiên cao nhất.
    /// </summary>
    public double LengthMm { get; init; }

    /// <summary>
    /// Chiều dài móc bẻ xuống ở đầu ngoài của thép gia cường top tại hai gối biên (mm). 0 = không bẻ.
    /// </summary>
    public double EdgeHookDownLengthMm { get; init; }

    /// <summary>
    ///     Thép gia cường BOT (giữa nhịp): chiều dài neo thêm từ mép cột TRÁI/PHẢI (mm). Dương = kéo dài
    ///     ra phía gối; ÂM = neo thụt vào trong nhịp. Khi LeftLengthMm/RightLengthMm > 0, thanh = từ
    ///     (mép cột trái - AnchorLeft) đến (mép cột phải + AnchorRight). 0 = dùng TCVN 1/8..7/8.
    /// </summary>
    public double AnchorLeftMm { get; init; }
    public double AnchorRightMm { get; init; }

    /// <summary>Vùng đặt — căn quanh gối hay giữa nhịp.</summary>
    public AdditionalBarSide Side { get; init; } = AdditionalBarSide.TopAtSupport;

    public string PositionInSection { get; init; } = "0,1";

    /// <summary>Đai C giữ thép gia cường lớp 2 (khi ≥3 cây): thanh thẳng + 2 hook 180° ôm 1 cặp thanh gia
    ///     cường, rải theo bước. >0 = bật. Đường kính + bước riêng.</summary>
    public double TieCDiameterMm { get; init; }

    /// <summary>Bước rải đai C (mm). 0 → không tạo đai C.</summary>
    public double TieCSpacingMm { get; init; }
}
