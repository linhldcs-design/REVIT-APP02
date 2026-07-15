using Autodesk.Revit.DB;
using RevitAPP.Core.Models;

namespace RevitAPP.Models;

/// <summary>
///     Một đoạn cột đã phát hiện: ghép hình học thuần (<see cref="Storey"/>) với thông tin Revit
///     cần để đặt thép ở Phase 4 (id cột để host, tâm tiết diện, góc xoay quanh trục Z).
/// </summary>
public sealed record ColumnStackItem(
    ColumnStorey Storey,
    ElementId ColumnId,
    double CenterXFeet,
    double CenterYFeet,
    double RotationRad,
    double AutoBeamDepthMm = 0);
