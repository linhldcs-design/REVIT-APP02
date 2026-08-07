namespace RevitAPP.Core.Models.CadStructure;

/// <summary>
/// A closed boundary in millimetres. The first and last vertex are distinct; the closing edge is
/// implied, which is what <c>Floor.Create</c> expects from a CurveLoop.
/// </summary>
public sealed record CadSlabLoop(IReadOnlyList<CadStructurePoint2> VerticesMm)
{
    /// <summary>
    /// Signed area by the shoelace formula. The sign tells an outer boundary from a hole, so it is
    /// kept rather than taking the absolute value here.
    /// </summary>
    public double SignedAreaMm2
    {
        get
        {
            var total = 0.0;
            for (var index = 0; index < VerticesMm.Count; index++)
            {
                var current = VerticesMm[index];
                var next = VerticesMm[(index + 1) % VerticesMm.Count];
                total += current.X * next.Y - next.X * current.Y;
            }
            return total / 2.0;
        }
    }

    public double AreaMm2 => Math.Abs(SignedAreaMm2);
}

public enum CadSlabRegionStatus
{
    Ready,
    MissingThickness,
    MissingElevation,
    AmbiguousThickness,
    AmbiguousElevation,
    Opening,
    CurvedEdge,
    OpenLoop
}

/// <summary>
/// One cell of the planar subdivision: the smallest area the scanned lines enclose. Cells are the
/// input to merging, not the result -- a slab is poured in one piece across the beams inside it.
/// </summary>
public sealed record CadSlabCell(
    int Id,
    CadSlabLoop Loop,
    IReadOnlyList<int> SourceSegmentIds)
{
    public double? ThicknessMm { get; init; }
    public double? ElevationMm { get; init; }
    public bool IsOpening { get; init; }
    public bool IsLowered { get; init; }
    /// <summary>
    /// A narrow strip between two cells is the footprint of a beam drawn by both its faces, not a
    /// slab of its own, so it is absorbed into the slab rather than kept as a region.
    /// </summary>
    public bool IsBeamStrip { get; init; }
    public string MatchedText { get; init; } = string.Empty;

    public CadStructurePoint2 CentroidMm
    {
        get
        {
            var x = 0.0;
            var y = 0.0;
            foreach (var vertex in Loop.VerticesMm)
            {
                x += vertex.X;
                y += vertex.Y;
            }
            var count = Math.Max(1, Loop.VerticesMm.Count);
            return new CadStructurePoint2(x / count, y / count);
        }
    }
}

/// <summary>
/// A slab as it will be created: one outer boundary, any number of holes, and the cells it was
/// merged from. Cells merge while they share an elevation and a thickness.
/// </summary>
public sealed record CadSlabRegionCandidate(
    int Id,
    CadSlabLoop OuterLoop,
    IReadOnlyList<CadSlabLoop> Holes,
    IReadOnlyList<int> CellIds,
    IReadOnlyList<int> SourceSegmentIds,
    double? DetectedThicknessMm,
    double? DetectedElevationMm,
    double EffectiveThicknessMm,
    double EffectiveOffsetMm,
    CadSlabRegionStatus Status,
    string MatchedText)
{
    public bool IsManualOverride { get; init; }
    public int AbsorbedStripCount { get; init; }
    public bool IsLowered { get; init; }

    public double AreaMm2 => OuterLoop.AreaMm2 - Holes.Sum(hole => hole.AreaMm2);
    public double AreaM2 => AreaMm2 / 1_000_000.0;

    public bool CanCreate => Status is CadSlabRegionStatus.Ready
                                 or CadSlabRegionStatus.MissingThickness
                                 or CadSlabRegionStatus.MissingElevation;
}

public sealed record CadSlabAnalysis(
    CadStructurePoint2 OriginMm,
    CadStructurePoint2 SourceAnchorMm,
    IReadOnlyList<CadSlabRegionCandidate> Regions,
    IReadOnlyList<CadSlabCell> Cells,
    int ShortLinesIgnored,
    int UnclosedVertexCount,
    int HatchesWithoutRegion,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// A hatched area from the drawing. Hatches mark a lowered slab rather than describe its boundary,
/// so they classify cells and never contribute edges.
/// </summary>
public sealed record CadHatchRegion(int Id, IReadOnlyList<CadStructurePoint2> BoundaryMm);

public sealed record CadSlabAnalysisOptions(
    // Line ends almost never meet exactly in a drawing, and a boundary that does not close leaves
    // no region at all, so ends within this distance are treated as one vertex.
    double VertexSnapToleranceMm = 20.0,
    double MinimumLineLengthMm = 200.0,
    // Below this a region is a duct or a technical shaft rather than a slab worth pouring.
    double MinimumRegionAreaM2 = 1.0,
    // A strip narrower than this between two cells is a beam drawn by its two faces. Wider than
    // this and it is a corridor or a room, which stays a region of its own.
    double MaximumBeamStripWidthMm = 500.0,
    // A bare number is only a thickness inside this range: a plan is full of numbers that are not
    // thicknesses -- grid spacings, dimensions and grid names.
    double MinimumThicknessMm = 50.0,
    double MaximumThicknessMm = 500.0,
    double DefaultThicknessMm = 100.0,
    double DefaultOffsetMm = 0.0,
    double LoweredDefaultOffsetMm = -50.0,
    double TextSearchDistanceMm = 2000.0,
    bool OverrideThickness = false,
    bool OverrideElevation = false);
