namespace RevitAPP.Core.Models;

/// <summary>
///     Một đoạn cột theo tầng (hình học đọc từ Revit ở Phase 2). Cao độ tính bằng mm.
/// </summary>
public sealed record ColumnStorey(
    int Index,
    string LevelName,
    double BaseElevationMm,
    double TopElevationMm,
    ColumnSection Section)
{
    /// <summary>Chiều cao thông thủy đoạn cột (mm).</summary>
    public double ClearHeightMm => TopElevationMm - BaseElevationMm;
}

/// <summary>Ghép một tầng cột với cấu hình thép + loại thanh đã chọn (kết quả từ UI).</summary>
public sealed record StoreyRebarPlan(
    ColumnStorey Storey,
    FloorRebarConfig Config,
    RebarBarTypeOption MainBar,
    RebarBarTypeOption Stirrup,
    RebarBarTypeOption? DistributionBar = null);
