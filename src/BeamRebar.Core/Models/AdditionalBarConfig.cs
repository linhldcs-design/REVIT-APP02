namespace BeamRebar.Core.Models;

/// <summary>Vùng đặt thép gia cường, quyết định cách cắt thanh theo % nhịp.</summary>
public enum AdditionalBarSide
{
    /// <summary>Quanh gối (thép trên chịu mô men âm) — cắt đối xứng qua gối.</summary>
    TopAtSupport,

    /// <summary>Giữa nhịp (thép dưới chịu mô men dương) — căn giữa nhịp.</summary>
    BottomAtMidspan
}

/// <summary>
///     Thép gia cường (additional bar) top/bottom — UI item 1, 3, 4, 7. Đặt thêm tại vùng mô men lớn
///     (gối với thép trên, giữa nhịp với thép dưới). <see cref="Enabled"/> = false (Count 0) → bỏ qua.
///     Hỗ trợ 2 lớp (Layer 1, Layer 2) qua field <see cref="Layer"/>.
/// </summary>
public sealed record AdditionalBarConfig
{
    public bool Enabled { get; init; }

    public int Count { get; init; } = 1;

    public RebarDiameter Diameter { get; init; } = new(16);

    /// <summary>Lớp thép (1 hoặc 2). Layer 2 nằm phía trong Layer 1.</summary>
    public int Layer { get; init; } = 1;

    /// <summary>
    ///     Chiều dài thanh tính theo % chiều dài nhịp (0..100). 0 → chạy suốt nhịp (hành vi v1).
    /// </summary>
    public double LengthPercent { get; init; }

    /// <summary>Vùng đặt — quyết định căn quanh gối hay giữa nhịp khi cắt theo %.</summary>
    public AdditionalBarSide Side { get; init; } = AdditionalBarSide.TopAtSupport;
}
