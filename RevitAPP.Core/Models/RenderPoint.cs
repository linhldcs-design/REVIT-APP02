namespace RevitAPP.Core.Models;

/// <summary>
///     Một điểm point cloud đã transform về model coords (offset theo reference origin),
///     dạng thuần để render. Color đóng gói RGBA dạng uint (0xAABBGGRR).
/// </summary>
public readonly struct RenderPoint
{
    public RenderPoint(float x, float y, float z, uint color)
    {
        X = x;
        Y = y;
        Z = z;
        Color = color;
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    /// <summary>RGBA đóng gói 0xAABBGGRR (A=alpha, B, G, R) — khớp ColorWithTransparency của Revit.</summary>
    public uint Color { get; }
}
