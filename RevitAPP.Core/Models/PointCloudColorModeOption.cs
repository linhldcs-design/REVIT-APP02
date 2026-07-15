namespace RevitAPP.Core.Models;

/// <summary>
///     Các chế độ tô màu Point Cloud mà Revit API hỗ trợ (cấp View override).
///     Ánh xạ 1-1 với <c>Autodesk.Revit.DB.PointCloudColorMode</c>; lớp Revit chuyển đổi.
/// </summary>
/// <remarks>
///     Revit KHÔNG có mode "RGB" riêng — màu RGB gốc của scan hiển thị khi
///     <see cref="NoOverride" /> (không áp override). Point size / brightness / contrast /
///     transparency KHÔNG có trong Revit API nên không xuất hiện ở đây.
/// </remarks>
public enum PointCloudColorModeOption
{
    /// <summary>Không override — hiện màu RGB gốc của point cloud.</summary>
    NoOverride,

    /// <summary>Một màu cố định cho toàn bộ.</summary>
    FixedColor,

    /// <summary>Tô màu theo cao độ (gradient).</summary>
    Elevation,

    /// <summary>Tô màu theo cường độ phản xạ (intensity).</summary>
    Intensity,

    /// <summary>Tô màu theo pháp tuyến bề mặt.</summary>
    Normals
}

/// <summary>Mục chọn color mode kèm nhãn hiển thị tiếng Việt cho ComboBox.</summary>
public sealed record PointCloudColorModeItem(PointCloudColorModeOption Mode, string Display)
{
    public override string ToString() => Display;

    /// <summary>Danh sách mọi mode kèm nhãn — nguồn cho ComboBox.</summary>
    public static IReadOnlyList<PointCloudColorModeItem> All { get; } = new[]
    {
        new PointCloudColorModeItem(PointCloudColorModeOption.NoOverride, "Màu gốc (RGB)"),
        new PointCloudColorModeItem(PointCloudColorModeOption.FixedColor, "Màu cố định"),
        new PointCloudColorModeItem(PointCloudColorModeOption.Elevation, "Theo cao độ"),
        new PointCloudColorModeItem(PointCloudColorModeOption.Intensity, "Theo cường độ"),
        new PointCloudColorModeItem(PointCloudColorModeOption.Normals, "Theo pháp tuyến")
    };
}
