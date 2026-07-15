namespace RevitAPP.Core.Models;

/// <summary>
///     Tùy chọn rải đai áp dụng toàn cột (Stirrup tab).
/// </summary>
/// <param name="DistanceToFirstMm">Khoảng cách từ chân cột tới thanh đai đầu tiên (mm).</param>
/// <param name="SpreadThroughBeam">
///     true = đai rải xuyên qua cả vùng dầm (hết chiều cao tầng);
///     false = đai dừng dưới đáy dầm (mặc định, không băng qua dầm).
/// </param>
/// <param name="MinConfineZoneMm">Giá trị tối thiểu của vùng gia cường l0 (mm).</param>
/// <param name="ConfineClearanceDivisor">Hệ số chia chiều cao thông thuỷ để tính l0 (l0 ≥ H/N).</param>
/// <param name="ReinforceJoint">Đặt thêm đai gia cường trong vùng dầm/nút (khi đai dừng dưới dầm).</param>
/// <param name="JointStirrupCount">Số đai gia cường trong vùng dầm (≥ 2).</param>
/// <param name="CrosstieDirection">Phương đặt móc chéo (X / Y / cả hai) khi kiểu đai = Crosstie.</param>
public sealed record StirrupSpreadOptions(
    double DistanceToFirstMm = 0,
    bool SpreadThroughBeam = false,
    double MinConfineZoneMm = 450,
    double ConfineClearanceDivisor = 6,
    bool ReinforceJoint = false,
    int JointStirrupCount = 3,
    CrosstieDirection CrosstieDirection = CrosstieDirection.X);
