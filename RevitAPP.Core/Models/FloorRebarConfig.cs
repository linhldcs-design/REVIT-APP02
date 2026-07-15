namespace RevitAPP.Core.Models;

/// <summary>
///     Cấu hình cốt thép cho một tầng cột (do người dùng nhập ở UI).
/// </summary>
/// <param name="MainBarDiameterMm">Đường kính thép chủ (mm).</param>
/// <param name="BarsX">Số thanh thép chủ trên mỗi mặt phương X (cạnh trên & dưới), tính cả 2 góc. ≥ 2.</param>
/// <param name="BarsY">Số thanh thép chủ trên mỗi mặt phương Y (cạnh trái & phải), tính cả 2 góc. ≥ 2.</param>
/// <param name="StirrupDiameterMm">Đường kính thép đai (mm).</param>
/// <param name="SpacingEndMm">Khoảng cách đai vùng đầu/chân cột (mm), mặc định ~100.</param>
/// <param name="SpacingMidMm">Khoảng cách đai vùng thân cột (mm), mặc định ~200.</param>
/// <param name="ConfineZoneLenMm">
///     Chiều dài vùng gia cường đầu/chân cột (mm). 0 = tự tính theo TCVN:
///     max(H_thông_thủy/6, max(B,H), 450).
/// </param>
/// <param name="BeamDepthMm">
///     Chiều cao dầm tại đỉnh cột (mm). Vùng đặt đai bị cắt còn (H_thông_thủy − BeamDepthMm)
///     để thép đai không băng qua dầm. 0 = không có dầm (đai chạy hết chiều cao tầng).
/// </param>
/// <param name="UseDistributionBar">Bật thép phụ (distribution) cho các thanh giữa cạnh (không phải góc).</param>
/// <param name="DistributionBarDiameterMm">Đường kính thép phụ (mm) khi <paramref name="UseDistributionBar"/> bật.</param>
/// <param name="StirrupSectionType">Kiểu đai trong tiết diện: kín / crosstie / tách rời.</param>
public sealed record FloorRebarConfig(
    double MainBarDiameterMm,
    int BarsX,
    int BarsY,
    double StirrupDiameterMm,
    double SpacingEndMm,
    double SpacingMidMm,
    double ConfineZoneLenMm = 0,
    double BeamDepthMm = 0,
    bool UseDistributionBar = false,
    double DistributionBarDiameterMm = 0,
    SectionStirrupType StirrupSectionType = SectionStirrupType.ClosedTie);
