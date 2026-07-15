namespace RevitAPP.Core.Models;

/// <summary>
///     Thông tin một Point Cloud instance trong dự án, dạng thuần (không phụ thuộc Revit API).
///     <paramref name="InstanceId" /> giữ giá trị long của ElementId; lớp Revit chuyển đổi ngược lại.
/// </summary>
public sealed record PointCloudInfo(
    long InstanceId,
    string Name,
    bool SupportsOverrides,
    IReadOnlyList<string> Scans,
    IReadOnlyList<string> Regions)
{
    /// <summary>Nhãn hiển thị trên combo.</summary>
    public string Display => SupportsOverrides ? Name : $"{Name} (không hỗ trợ override)";

    public override string ToString() => Display;
}
