namespace BeamRebar.Core.Models;

/// <summary>
///     Gối đỡ (cột/dầm chính) tại điểm nối các nhịp hoặc hai đầu mút dầm liên tục. Toạ độ feet.
///     <see cref="WidthFeet"/> = bề rộng cột theo phương dầm; v1 mặc định = bề rộng dầm gối.
/// </summary>
public sealed record Support(
    Point3 Location,
    double WidthFeet,
    bool IsEnd);
