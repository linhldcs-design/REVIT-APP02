namespace BeamRebarPro.Models;

/// <summary>
///     Gối đỡ tại một điểm trên trục dầm liên tục (đầu mút hoặc giao với cột/dầm khác).
///     <see cref="IsEnd"/> = true cho hai gối biên (đầu/cuối dầm).
///     <see cref="HalfWidthFeet"/> = nửa bề rộng cột theo phương dầm → tính chiều dài thép gia cường
///     TỪ MÉP CỘT (mép = tâm ± HalfWidth). 0 nếu gối không phải cột (đầu mút).
/// </summary>
public sealed record Support(int Index, Point3 Location, bool IsEnd, double HalfWidthFeet = 0);
