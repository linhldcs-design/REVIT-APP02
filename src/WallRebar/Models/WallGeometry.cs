using Autodesk.Revit.DB;

namespace WallRebar.Models;

/// <summary>
///     Khung tọa độ hình học của một tường thẳng (đơn vị feet). Gốc tại đầu LocationCurve, đáy tường.
///     3 trục trực giao:
///     <list type="bullet">
///         <item><see cref="DirAlong"/>: dọc chiều dài (hướng LocationCurve).</item>
///         <item><see cref="DirUp"/>: theo chiều cao (Z).</item>
///         <item><see cref="DirThickness"/>: qua bề dày tường (= DirAlong × DirUp).</item>
///     </list>
/// </summary>
public sealed record WallGeometry
{
    public required XYZ Origin { get; init; }
    public required XYZ DirAlong { get; init; }
    public required XYZ DirUp { get; init; }
    public required XYZ DirThickness { get; init; }

    public required double LengthFeet { get; init; }
    public required double HeightFeet { get; init; }
    public required double ThicknessFeet { get; init; }
}
