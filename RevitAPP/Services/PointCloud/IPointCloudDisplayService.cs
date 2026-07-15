using Autodesk.Revit.DB;
using RevitAPP.Core.Models;
using Color = Autodesk.Revit.DB.Color;

namespace RevitAPP.Services.PointCloud;

/// <summary>
///     Điều khiển hiển thị Point Cloud qua Revit API native (override cấp View).
///     Chỉ phơi bày những gì Revit API thật sự hỗ trợ: color mode, fixed color, scan visibility.
/// </summary>
public interface IPointCloudDisplayService
{
    /// <summary>Liệt kê mọi Point Cloud instance trong document.</summary>
    IReadOnlyList<PointCloudInfo> GetPointClouds(Document document);

    /// <summary>Đọc color mode hiện tại của instance trong view.</summary>
    PointCloudColorModeOption GetColorMode(View view, long instanceId);

    /// <summary>
    ///     Đổi color mode cho instance trong view. Với <see cref="PointCloudColorModeOption.FixedColor" />
    ///     thì <paramref name="fixedColor" /> bắt buộc. Tự wrap Transaction. Trả false + log nếu lỗi.
    /// </summary>
    bool SetColorMode(Document document, View view, long instanceId, PointCloudColorModeOption mode, Color? fixedColor);

    /// <summary>Bật/tắt hiển thị một scan trong instance. Tự wrap Transaction.</summary>
    bool SetScanVisibility(Document document, View view, long instanceId, string scanName, bool visible);
}
