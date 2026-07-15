namespace BeamRebarPro.Models;

/// <summary>
///     Một nhịp dầm: đoạn trục từ <see cref="Start"/> đến <see cref="End"/> (feet) với tiết diện
///     <see cref="Section"/>. <see cref="Index"/> 0-based theo thứ tự dọc trục dầm liên tục.
/// </summary>
public sealed record Span(int Index, Point3 Start, Point3 End, BeamSection Section)
{
    public double LengthFeet => Start.DistanceTo(End);
}
