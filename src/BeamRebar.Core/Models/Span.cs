namespace BeamRebar.Core.Models;

/// <summary>
///     Một nhịp dầm giữa hai gối liên tiếp (như "Span 0/1/2" trong UI). Toạ độ feet.
/// </summary>
public sealed record Span(
    int Index,
    Point3 Start,
    Point3 End,
    BeamSection Section)
{
    public double LengthFeet => Start.DistanceTo(End);
}
