namespace BeamRebarPro.Models;

/// <summary>
///     Thông tin một nhịp tính toán sau khi chọn dầm: chỉ số + chiều dài (mm). Dùng để tính tự động
///     chiều dài thép gia cường theo nguyên tắc TCVN (top 0.25L mỗi bên gối, bottom 1/8..6/8 L).
/// </summary>
public sealed record SpanInfo(int Index, double LengthMm, double LeftColumnHalfWidthMm = 200, double RightColumnHalfWidthMm = 200)
{
    /// <summary>Bề rộng tiết diện thật của nhịp (mm). 0 = chưa đọc được từ dầm.</summary>
    public double SectionWidthMm { get; init; }

    /// <summary>Chiều cao tiết diện thật của nhịp (mm), đo từ mặt trên xuống mặt dưới. 0 = chưa đọc được.</summary>
    public double SectionHeightMm { get; init; }

    /// <summary>Cao độ mặt trên dầm (mm) trong hệ toạ độ mô hình. Dùng để bản xem trước đặt đúng cao độ.</summary>
    public double TopElevationMm { get; init; }

    /// <summary>Toạ độ đầu và cuối trục nhịp (mm) trong hệ toạ độ mô hình.</summary>
    public double StartXMm { get; init; }
    public double StartYMm { get; init; }
    public double EndXMm { get; init; }
    public double EndYMm { get; init; }

    /// <summary>Lệch ngang từ đường trục tới tâm khối bê tông (mm), bù justification của dầm.</summary>
    public double LateralOffsetMm { get; init; }

    /// <summary>Đã có đủ hình học thật để dựng bản xem trước đúng vị trí và tiết diện.</summary>
    public bool HasRealGeometry => SectionWidthMm > 1 && SectionHeightMm > 1;


    /// <summary>Đoạn thép gia cường TRÊN vắt qua mỗi gối: 0.25L mỗi bên (TCVN).</summary>
    public double TopExtendEachSideMm => 0.25 * LengthMm;

    /// <summary>Thép gia cường DƯỚI giữa nhịp: bắt đầu cách gối 1/8 L, kết thúc cách gối kia ~2/8 L.</summary>
    public double BottomStartMm => LengthMm / 8.0;
    public double BottomEndMm => LengthMm * 6.0 / 8.0;
    public double BottomLengthMm => BottomEndMm - BottomStartMm;
}
