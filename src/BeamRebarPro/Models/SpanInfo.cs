namespace BeamRebarPro.Models;

/// <summary>
///     Thông tin một nhịp tính toán sau khi chọn dầm: chỉ số + chiều dài (mm). Dùng để tính tự động
///     chiều dài thép gia cường theo nguyên tắc TCVN (top 0.25L mỗi bên gối, bottom 1/8..6/8 L).
/// </summary>
public sealed record SpanInfo(int Index, double LengthMm, double LeftColumnHalfWidthMm = 200, double RightColumnHalfWidthMm = 200)
{
    /// <summary>Đoạn thép gia cường TRÊN vắt qua mỗi gối: 0.25L mỗi bên (TCVN).</summary>
    public double TopExtendEachSideMm => 0.25 * LengthMm;

    /// <summary>Thép gia cường DƯỚI giữa nhịp: bắt đầu cách gối 1/8 L, kết thúc cách gối kia ~2/8 L.</summary>
    public double BottomStartMm => LengthMm / 8.0;
    public double BottomEndMm => LengthMm * 6.0 / 8.0;
    public double BottomLengthMm => BottomEndMm - BottomStartMm;
}
