namespace RevitAPP.Core.Models;

/// <summary>
///     Tùy chọn xử lý đầu thép chủ (Settings tab).
/// </summary>
/// <param name="TopHookBending">
///     true = bẻ móc 90° vào trong lõi tại đỉnh cột tầng trên cùng;
///     false = để thẳng (nối tiếp lên tầng trên / mái).
/// </param>
/// <param name="TopHookLengthMm">Chiều dài đoạn móc bẻ tại đỉnh (mm).</param>
/// <param name="CrankAtLap">
///     true = uốn lệch (crank) đoạn nối chồng vào trong lõi ~1 đường kính để tránh đè thanh trên;
///     false = để thẳng (2 thanh nối song song).
/// </param>
public sealed record ColumnEndOptions(
    bool TopHookBending = false,
    double TopHookLengthMm = 100,
    bool CrankAtLap = false);
