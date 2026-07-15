namespace RevitAPP.Core.Models;

/// <summary>
///     Điểm 2D trong mặt phẳng tiết diện cột (mm), gốc (0,0) tại tâm tiết diện.
///     X theo phương rộng (B), Y theo phương cao (H) của tiết diện.
/// </summary>
public readonly record struct Point2D(double Xmm, double Ymm);
