namespace RevitAPP.Core.Models.BeamLongitudinalDrawing;

/// <summary>Một lớp thép dọc tại station, dùng đơn vị feet và mm.</summary>
public sealed record RebarLayerFingerprint(
    double ElevationFeet,
    double DiameterMm,
    int Quantity);

/// <summary>Chữ ký đai tại station. SpacingFeet là bước đai theo trục dầm.</summary>
public sealed record StirrupZoneFingerprint(
    double DiameterMm,
    double SpacingFeet);

/// <summary>
/// Chữ ký tiết diện tại station. Không chứa ElementId để có thể test và lưu vết độc lập Revit session.
/// </summary>
public sealed record RebarStationFingerprint(
    double WidthFeet,
    double HeightFeet,
    IReadOnlyList<RebarLayerFingerprint> LongitudinalLayers,
    IReadOnlyList<StirrupZoneFingerprint> StirrupZones,
    bool IsUncertain)
{
    /// <summary>
    /// Có thép tăng cường ngoài các lớp thép chủ. Chỉ station không tăng cường mới được xét rút gọn.
    /// </summary>
    public bool HasAdditionalReinforcement { get; init; }
}

public sealed record RebarFingerprintTolerance(
    double SectionFeet,
    double LayerElevationFeet,
    double DiameterMm,
    double StirrupSpacingFeet)
{
    public static RebarFingerprintTolerance Default { get; } = new(0.01, 0.01, 0.1, 0.01);
}
