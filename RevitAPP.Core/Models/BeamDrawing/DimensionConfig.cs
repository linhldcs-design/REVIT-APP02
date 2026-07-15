namespace RevitAPP.Core.Models.BeamDrawing;

/// <summary>
///     Cấu hình dimension: bật/tắt, tên dim type cho mặt đứng (SE) và mặt cắt ngang (CS),
///     hệ số spacing, và khoảng cách dim tới dầm bên/mặt đáy (mm) — theo ô nhập form BIMSpeed.
/// </summary>
public sealed record DimensionConfig(
    bool Enabled,
    string? SectionalDimTypeName,
    string? CrossDimTypeName,
    int SpacingFactor,
    double DistanceToSideBeamMm,
    double DistanceToBotFaceMm);
