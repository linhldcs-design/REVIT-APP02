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
    /// Which hatch style covers this cell, empty when none does. A plan draws each drop with its
    /// own pattern, so cells hatched alike belong to the same slab and cells hatched differently
    /// do not, however many styles the drawing uses.
    /// </summary>
    public string HatchStyleKey { get; init; } = string.Empty;
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

    /// <summary>
    /// Hatch styles the scan found, so the user can set a drop for each one. However many the
    /// drawing uses, each becomes its own slab.
    /// </summary>
    public IReadOnlyList<string> HatchStyles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A hatched area from the drawing. Hatches mark a lowered slab rather than describe its boundary,
/// so they classify cells and never contribute edges. A plan uses a different pattern or spacing
/// for each drop, so the style is carried through and cells hatched alike are grouped together.
/// </summary>
public sealed record CadHatchRegion(int Id, IReadOnlyList<CadStructurePoint2> BoundaryMm)
{
    public string PatternName { get; init; } = string.Empty;
    public double PatternScale { get; init; }
    public double PatternAngleDegrees { get; init; }

    /// <summary>
    /// Identity of the hatch style, used to tell one drop from another. Scale and angle are
    /// rounded so that two areas hatched with the same settings compare equal.
    /// </summary>
    public string StyleKey =>
        $"{PatternName}|{Math.Round(PatternScale, 3)}|{Math.Round(PatternAngleDegrees, 1)}";
}

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
    // Fallback drop for a hatched bay whose label is missing. A plan may use several patterns for
    // several drops, so the per-style values below take precedence when the user fills them in.
    double LoweredDefaultOffsetMm = -50.0,
    double TextSearchDistanceMm = 2000.0,
    bool OverrideThickness = false,
    bool OverrideElevation = false)
{
    /// <summary>
    /// Drop per hatch style, keyed by <see cref="CadHatchRegion.StyleKey"/>. The analyzer reports
    /// the styles it found so the user can give each one its own level; a style with no entry
    /// falls back to <see cref="LoweredDefaultOffsetMm"/>.
    /// </summary>
    public IReadOnlyDictionary<string, double> HatchOffsetsMm { get; init; } =
        new Dictionary<string, double>();
}
