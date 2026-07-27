namespace RevitAPP.Core.Models.BeamLongitudinalDrawing;

/// <summary>Warning có cấu trúc để hand-off/smoke truy ra đúng view, element và thao tác.</summary>
public sealed record BeamLongitudinalDrawingWarning(
    string Code,
    string Message,
    long? ViewId = null,
    long? ElementId = null);

/// <summary>Snapshot đã được người dùng review; Phase 03 phải dùng đúng chain/station/hướng này.</summary>
public sealed record LongitudinalDrawingReviewResult(
    LongitudinalDrawingSetting Setting,
    BeamChainModel Chain,
    IReadOnlyList<SectionStation> Stations,
    bool IsReversed);

public sealed class BeamLongitudinalDrawingResult
{
    private readonly List<long> _longitudinalViewIds = [];
    private readonly List<long> _crossSectionViewIds = [];
    private readonly List<BeamLongitudinalDrawingWarning> _warnings = [];

    public IReadOnlyList<long> LongitudinalViewIds => _longitudinalViewIds.AsReadOnly();
    public IReadOnlyList<long> CrossSectionViewIds => _crossSectionViewIds.AsReadOnly();
    public IReadOnlyList<BeamLongitudinalDrawingWarning> Warnings => _warnings.AsReadOnly();
    public long? SheetId { get; set; }

    public void AddLongitudinalView(long viewId) => _longitudinalViewIds.Add(viewId);
    public void AddCrossSectionView(long viewId) => _crossSectionViewIds.Add(viewId);
    public void AddWarning(BeamLongitudinalDrawingWarning warning) => _warnings.Add(warning);
}
