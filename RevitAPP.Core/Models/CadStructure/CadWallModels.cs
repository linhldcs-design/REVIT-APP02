namespace RevitAPP.Core.Models.CadStructure;

/// <summary>
/// How a wall was drawn, which is worth keeping: a pair of lines and a rectangle need different
/// checks, and a reader looking at the review can tell why a wall came out the way it did.
/// </summary>
public enum CadWallSource
{
    /// <summary>Two parallel boundaries with the wall between them.</summary>
    ParallelLines,

    /// <summary>A closed rectangle, its short side the thickness.</summary>
    Rectangle
}

public enum CadWallCandidateStatus
{
    Ready,

    /// <summary>The thickness read falls outside what the user said a wall can be.</summary>
    ThicknessOutOfRange,

    /// <summary>Too short to be worth building.</summary>
    TooShort
}

/// <summary>
/// One wall the drawing describes: where its centre line runs, and how thick it is.
///
/// A wall carries no label. Its thickness is measured between the boundaries that draw it, and
/// its height comes from the levels chosen in Revit -- so nothing here is read from text.
/// </summary>
public sealed record CadWallCandidate(
    int Id,
    CadStructurePoint2 StartMm,
    CadStructurePoint2 EndMm,
    double ThicknessMm,
    CadWallSource Source,
    IReadOnlyList<int> SourceSegmentIds,
    CadWallCandidateStatus Status)
{
    /// <summary>
    /// A thickness the user typed over the measured one, for a wall the reader measured wrongly.
    /// </summary>
    public double? OverrideThicknessMm { get; init; }

    public double EffectiveThicknessMm => OverrideThicknessMm ?? ThicknessMm;

    public double LengthMm => StartMm.DistanceTo(EndMm);

    public bool CanCreate => Status is CadWallCandidateStatus.Ready;
}

public sealed record CadWallAnalysis(
    CadStructurePoint2 OriginMm,
    CadStructurePoint2 AnchorMm,
    IReadOnlyList<CadWallCandidate> Walls,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public bool IsValid => Error is null;

    /// <summary>
    /// Every layer the scan covered, with how many objects each holds. The user picks which of
    /// them draw walls: a pair of lines two hundred apart could be a wall, a beam, or a pair of
    /// dimension lines, and no amount of geometry tells them apart.
    /// </summary>
    public IReadOnlyList<CadLayerTally> Layers { get; init; } = Array.Empty<CadLayerTally>();
}

/// <summary>
/// A layer in the scan, and how much of the drawing sits on it.
/// </summary>
public sealed record CadLayerTally(string Layer, int SegmentCount)
{
    /// <summary>
    /// Whether the reader thinks this layer draws walls. Only a suggestion: it ticks the box for
    /// the user, who is free to untick it. A guess that decides on its own turned away a whole
    /// scan once, when the grid axes turned out to sit on a layer called S-GRID.
    /// </summary>
    public bool SuggestedAsWall { get; init; }
}

public sealed record CadWallAnalysisOptions(
    // What the user says a wall can be. Both are typed before the scan, because they decide what
    // is picked up at all: a drawing carries beams and dimension lines drawn exactly like walls.
    double MinimumThicknessMm = 100.0,
    double MaximumThicknessMm = 400.0,
    double MinimumLengthMm = 300.0,
    // A rectangle no longer than this many times its width is a column, not a wall.
    double MinimumLengthRatio = 3.0,
    // Pieces of one drawn boundary rarely share an exact offset: trimming, snapping or copying
    // leaves a few millimetres of drift.
    double RailOffsetToleranceMm = 10.0,
    // Largest break along a boundary that still reads as one run.
    double GapJoinToleranceMm = 300.0,
    // How far a centre line reaches to meet another at a corner. Without it a room comes out as
    // four walls with four open corners.
    double JoinDistanceMm = 200.0)
{
    /// <summary>
    /// Layers the user picked as drawing walls. Empty means nothing was picked yet, and no wall
    /// is read: a scan that guessed for itself would take the beams too.
    /// </summary>
    public IReadOnlyList<string> WallLayers { get; init; } = Array.Empty<string>();
}
