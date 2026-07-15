namespace RevitAPP.Core.Models;

/// <summary>Vị trí nối chồng thép chủ theo chiều cao tầng.</summary>
public enum LapPosition
{
    /// <summary>Gần chân cột — mối nối cách đáy tầng một đoạn L (mặc định, L=0 = ngay sàn).</summary>
    NearBottom,

    /// <summary>Giữa cột — mối nối tại nửa chiều cao tầng (nơi mô-men nhỏ, kháng chấn).</summary>
    Middle
}

/// <summary>
///     Tùy chọn neo/nối thép áp dụng cho cả cột.
/// </summary>
/// <param name="LapFactor">Hệ số nối chồng (L_neo = factor × d). Mặc định 30d.</param>
/// <param name="CoverMm">Lớp bê tông bảo vệ tới mép thép đai (mm). Mặc định 25mm cho cột.</param>
/// <param name="StaggerLap">
///     Nối so le: các thanh lẻ được dịch lên một đoạn = chiều dài nối chồng để mối nối không
///     trùng tiết diện (≤50% thanh nối tại một mặt cắt). Mặc định bật.
/// </param>
/// <param name="LapPosition">Vị trí mối nối theo chiều cao tầng (gần chân / giữa cột).</param>
/// <param name="LapDistanceFromBottomMm">Khoảng cách từ đáy tầng (sàn) tới mối nối khi chọn NearBottom (mm). Mặc định 50.</param>
public sealed record RebarLapOptions(
    double LapFactor = 30,
    double CoverMm = 25,
    bool StaggerLap = true,
    LapPosition LapPosition = LapPosition.NearBottom,
    double LapDistanceFromBottomMm = 50);
