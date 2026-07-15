namespace RevitAPP.Core.Models;

/// <summary>
///     Một thanh thép chủ đã phân loại: vị trí (mm, tâm tiết diện), đường kính áp dụng,
///     và có phải thanh góc không (góc luôn dùng đường kính thép chủ).
/// </summary>
public sealed record PlacedBar(Point2D Position, double DiameterMm, bool IsCorner);
