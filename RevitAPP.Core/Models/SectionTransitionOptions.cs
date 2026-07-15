namespace RevitAPP.Core.Models;

/// <summary>Cách xử lý thép chủ tại ranh giới cột bị bóp (thu) tiết diện.</summary>
public enum SectionTransition
{
    None,    // không thay đổi tiết diện → nối thẳng/so le bình thường
    Crank,   // bóp nhẹ (e ≤ ngưỡng, dốc ≤ 1:N) → uốn vát thanh dưới vào vị trí cột trên
    Dowel    // bóp lớn → neo thanh dưới tại sàn + thép chờ riêng cho cột trên
}

/// <summary>Cách xử lý khi bóp LỚN (e > ngưỡng uốn vát).</summary>
public enum LargeStepMode
{
    /// <summary>Hình 1: thanh dưới neo móc ngang tại sàn; cột trên có thép riêng (không liên tục).</summary>
    AnchorAtSlab,

    /// <summary>Hình 2: uốn vát liên tục (dốc thoải) đưa thanh dưới lên cột trên.</summary>
    CrankContinuous
}

/// <summary>
///     Điều kiện uốn/nối thép khi cột thu tiết diện (Bar Bending / Splicing condition).
/// </summary>
/// <param name="BendIfOffsetLeMm">
///     Nếu độ lệch ngang e giữa thanh dưới và thanh trên ≤ giá trị này → uốn vát êm (crank); lớn hơn → theo <paramref name="LargeStepMode"/>.
/// </param>
/// <param name="SlopeRatioHdOverE">Tỷ lệ dốc tối thiểu Hd/e của đoạn vát (mặc định 6 → dốc ≤ 1:6).</param>
/// <param name="LargeStepMode">Cách xử lý khi bóp lớn (e > ngưỡng): neo móc tại sàn (Hình 1) hay uốn vát liên tục (Hình 2).</param>
/// <param name="JointAnchorDownMm">Đoạn thép cột TRÊN neo xuống nút (dưới sàn) tại nút bóp (mm).</param>
public sealed record SectionTransitionOptions(
    double BendIfOffsetLeMm = 75,
    double SlopeRatioHdOverE = 6,
    LargeStepMode LargeStepMode = LargeStepMode.AnchorAtSlab,
    double JointAnchorDownMm = 300);
