using Autodesk.Revit.DB;
using RevitAPP.Core.Models;

namespace RevitAPP.Services.PointCloud;

/// <summary>Đọc điểm point cloud (LOD, transform về model coords) để render qua DirectContext3D.</summary>
public interface IPointCloudReader
{
    /// <summary>
    ///     Đọc điểm của instance trong phạm vi view, theo mật độ <paramref name="density" />.
    ///     Có cache theo (instance, view, density). Chạy trong Revit API context.
    /// </summary>
    PointCloudReadResult Read(PointCloudInstance instance, View view, double density);

    /// <summary>Xóa cache (gọi khi đổi project / unload).</summary>
    void ClearCache();
}

/// <summary>
///     Kết quả đọc: danh sách điểm đã offset theo <see cref="Origin" /> (model coords - origin)
///     để giữ độ chính xác float. Render cộng lại Origin.
/// </summary>
public sealed record PointCloudReadResult(IReadOnlyList<RenderPoint> Points, XYZ Origin);
