namespace RevitAPP.Core.Models;

/// <summary>
///     Cấu hình cốt thép cột của một tầng dạng thuần dữ liệu để lưu/khôi phục (JSON).
///     Loại thanh thép lưu theo <see cref="MainBarDiameterMm"/>/<see cref="StirrupDiameterMm"/>…
///     thay vì ElementId — khi khôi phục sẽ dò lại <see cref="RebarBarTypeOption"/> theo đường kính,
///     nhờ đó preset dùng lại được ở dự án khác (ElementId khác nhau).
/// </summary>
public sealed record ColumnRebarFloorConfig(
    string LevelName,
    double MainBarDiameterMm,
    int BarsX,
    int BarsY,
    double StirrupDiameterMm,
    double SpacingEndMm,
    double SpacingMidMm,
    double ConfineZoneLenMm,
    bool UseDistributionBar,
    double DistributionBarDiameterMm,
    SectionStirrupType StirrupSectionType);

/// <summary>
///     Toàn bộ cấu hình dialog Vẽ Thép Cột (tham số chung + danh sách cấu hình từng tầng)
///     kèm tên preset. Serialize qua System.Text.Json để lưu vào document (ExtensibleStorage).
/// </summary>
public sealed record ColumnRebarConfig(
    string Name,
    // ===== Tham số chung =====
    double CoverMm,
    double LapFactor,
    bool StaggerLap,
    LapPosition LapPosition,
    double LapDistanceFromBottomMm,
    // ===== Thép chờ móng =====
    bool FoundationEnabled,
    double FoundationHmMm,
    double FoundationLbMm,
    StarterBendDirection FoundationDirection,
    bool FoundationSplitBothSides,
    // ===== Rải đai =====
    double DistanceToFirstStirrupMm,
    bool SpreadThroughBeam,
    double MinConfineZoneMm,
    double ConfineClearanceDivisor,
    bool ReinforceJoint,
    double JointStirrupCount,
    CrosstieDirection CrosstieDirection,
    // ===== Xử lý đầu thép =====
    bool TopHookBending,
    double TopHookLengthMm,
    bool CrankAtLap,
    double BendIfOffsetLeMm,
    double SlopeRatioHdOverE,
    LargeStepMode LargeStepMode,
    double JointAnchorDownMm,
    bool AddPartition,
    // ===== Cấu hình từng tầng =====
    IReadOnlyList<ColumnRebarFloorConfig> Floors);
