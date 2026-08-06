namespace RevitAPP.Core.Models.CadStructure;

public readonly record struct CadStructurePoint2(double X, double Y)
{
    public double DistanceTo(CadStructurePoint2 other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static CadStructurePoint2 operator +(CadStructurePoint2 a, CadStructurePoint2 b) =>
        new(a.X + b.X, a.Y + b.Y);

    public static CadStructurePoint2 operator -(CadStructurePoint2 a, CadStructurePoint2 b) =>
        new(a.X - b.X, a.Y - b.Y);

    public static CadStructurePoint2 operator *(CadStructurePoint2 point, double scale) =>
        new(point.X * scale, point.Y * scale);
}

public sealed record CadStructureSegment(
    int Id,
    CadStructurePoint2 Start,
    CadStructurePoint2 End,
    string Layer,
    string SourcePath,
    string? SourceText = null);

public sealed record CadStructureAnnotation(
    int Id,
    CadStructurePoint2 Position,
    string Text,
    double RotationDegrees,
    string Layer,
    string SourcePath,
    bool IsMText);

public sealed record CadStructureTransferPackage(
    int SchemaVersion,
    string SelectionId,
    DateTime CreatedUtc,
    string SourceDrawing,
    string AutoCadVersion,
    int InsUnits,
    CadStructurePoint2 SourceAnchor,
    IReadOnlyList<CadStructureSegment> Segments)
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// TEXT/MTEXT metadata is optional so schema V1 packages created by the Grid/Column
    /// workflow remain source and binary compatible.
    /// </summary>
    public IReadOnlyList<CadStructureAnnotation> Annotations { get; init; } =
        Array.Empty<CadStructureAnnotation>();
}

public sealed record CadBeamAnalysisOptions(
    double MinimumLineLengthMm = 500.0,
    double GapJoinToleranceMm = 300.0,
    // Labels sit well clear of the beam they name so they stay readable among dimensions, so a
    // metre is not far enough to reach them on a real drawing.
    double TextSearchDistanceMm = 2000.0,
    double MinimumRailCoverageRatio = 0.5,
    double MinimumWidthMm = 100.0,
    double MaximumWidthMm = 1000.0,
    // Pieces of one drawn boundary rarely share an exact offset: trimming, snapping to a nearby
    // element or copying a bay leaves a few millimetres of drift. Grouping them within this
    // distance keeps a broken boundary as one rail, so the beam stays a single run instead of
    // splitting into shorter runs whose widths drift away from the annotated section.
    double RailOffsetToleranceMm = 10.0,
    // Largest break that still reads as one beam interrupted by a support. Beyond it, two
    // stretches sharing an axis and a section are taken to be separate beams. The default is
    // wide enough that only a deliberately lowered value separates them.
    double MaximumRunGapMm = 1_000_000.0);

public enum CadBeamCandidateStatus
{
    Ready,
    MissingText,
    TextWidthMismatch,
    AmbiguousText,
    AmbiguousGeometry,
    InsufficientRailCoverage
}

public sealed record CadBeamCandidate(
    int Id,
    CadStructurePoint2 StartMm,
    CadStructurePoint2 EndMm,
    double GeometryWidthMm,
    double? TextWidthMm,
    double? TextHeightMm,
    double EffectiveWidthMm,
    double EffectiveHeightMm,
    string Mark,
    string MatchedText,
    CadBeamCandidateStatus Status,
    bool ReconstructedOnGridAxis,
    IReadOnlyList<int> SourceSegmentIds,
    IReadOnlyList<int> SourceAnnotationIds)
{
    public double LengthMm => StartMm.DistanceTo(EndMm);
    public bool CanCreate => TextHeightMm is > 0
                             && Status is CadBeamCandidateStatus.Ready
                                 or CadBeamCandidateStatus.TextWidthMismatch;
}

public sealed record CadBeamAnalysis(
    CadStructurePoint2 SourceOriginMm,
    CadStructurePoint2 SourceAnchorRelativeMm,
    IReadOnlyList<CadBeamCandidate> Beams,
    int ShortLinesIgnored,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public bool IsValid => Error is null && Beams.Count > 0;
}

public sealed record CadColumnCandidate(
    int Id,
    IReadOnlyList<CadStructurePoint2> CornersMm,
    CadStructurePoint2 CenterMm,
    double WidthMm,
    double HeightMm,
    double AngleDegrees,
    string Layer,
    string SourcePath,
    string? SourceText,
    IReadOnlyList<int> SourceSegmentIds);

public sealed record CadStructureAnalysis(
    CadStructurePoint2 SourceOriginMm,
    CadStructurePoint2 SourceAnchorRelativeMm,
    IReadOnlyList<CadStructureSegment> GridSegmentsMm,
    IReadOnlyList<CadColumnCandidate> Columns,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public bool IsValid => GridSegmentsMm.Count > 0 || Columns.Count > 0;
}
