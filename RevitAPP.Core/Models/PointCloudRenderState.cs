namespace RevitAPP.Core.Models;

/// <summary>
///     Trạng thái hiển thị point cloud do slider điều khiển. Thuần (Core) để test logic màu.
///     Contrast / X-ray KHÔNG có ở đây — Revit/DirectContext3D không hỗ trợ (xem plan).
/// </summary>
public sealed record PointCloudRenderState
{
    private const double GeometryTolerance = 1e-6;

    /// <summary>Kích thước điểm (feet) — bán kính quad. Mặc định ~30mm.</summary>
    public double PointSizeFeet { get; init; } = 0.1;

    /// <summary>Độ sáng -100..100. 0 = giữ nguyên màu gốc.</summary>
    public int Brightness { get; init; }

    /// <summary>Độ trong suốt 0..100. 0 = đục, 100 = vô hình.</summary>
    public int Transparency { get; init; }

    /// <summary>Dùng màu gốc RGB của điểm (true) hay 1 màu cố định (false).</summary>
    public bool UseOriginalColor { get; init; } = true;

    /// <summary>Màu cố định RGBA 0xAABBGGRR khi <see cref="UseOriginalColor" /> = false.</summary>
    public uint FixedColor { get; init; } = 0xFF0050FF; // cam

    public static PointCloudRenderState Default => new();

    public bool RequiresGeometryRebuild(PointCloudRenderState previous) =>
        Math.Abs(PointSizeFeet - previous.PointSizeFeet) > GeometryTolerance;

    public bool RequiresColorRebuild(PointCloudRenderState previous) =>
        Brightness != previous.Brightness ||
        UseOriginalColor != previous.UseOriginalColor ||
        FixedColor != previous.FixedColor;

    public bool RequiresEffectUpdate(PointCloudRenderState previous) =>
        Transparency != previous.Transparency;

    /// <summary>
    ///     Áp brightness + transparency + (tùy chọn) màu cố định lên màu gốc của điểm,
    ///     trả RGBA 0xAABBGGRR cuối cùng để đẩy vào vertex.
    /// </summary>
    public uint ResolveColor(uint sourceRgba)
    {
        var src = UseOriginalColor ? sourceRgba : FixedColor;

        var r = (int)(src & 0xFF);
        var g = (int)((src >> 8) & 0xFF);
        var b = (int)((src >> 16) & 0xFF);

        if (Brightness != 0)
        {
            var delta = (int)(Brightness / 100.0 * 255);
            r = Clamp(r + delta);
            g = Clamp(g + delta);
            b = Clamp(b + delta);
        }

        var alpha = (int)Math.Round((100 - Transparency) / 100.0 * 255);
        alpha = Clamp(alpha);

        return (uint)((alpha << 24) | (b << 16) | (g << 8) | r);
    }

    /// <summary>
    ///     Vertex colors stay opaque; transparency is applied through DirectContext3D EffectInstance.
    ///     This lets transparency changes avoid rebuilding the vertex buffer.
    /// </summary>
    public uint ResolveOpaqueVertexColor(uint sourceRgba)
    {
        var resolved = ResolveColor(sourceRgba);
        return resolved | 0xFF000000u;
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}
