namespace RevitAPP.Core.Models;

/// <summary>
///     Một vùng đai theo cao độ đoạn cột. <paramref name="StartElevationMm"/> tính từ
///     đáy đoạn cột (0 = mặt dưới). <paramref name="Count"/> = số thanh đai trong vùng.
/// </summary>
public sealed record StirrupZone(double StartElevationMm, double LengthMm, double SpacingMm, int Count);

/// <summary>
///     3 vùng đai TCVN theo chiều cao đoạn cột: chân (dưới) — thân (giữa) — đầu (trên).
///     Khi cột quá thấp, vùng thân có thể rỗng (LengthMm = 0, Count = 0).
/// </summary>
public sealed record StirrupZones(StirrupZone Bottom, StirrupZone Middle, StirrupZone Top);
