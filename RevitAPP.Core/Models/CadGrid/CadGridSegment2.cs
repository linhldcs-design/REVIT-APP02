namespace RevitAPP.Core.Models.CadGrid;

/// <summary>
/// A bounded CAD line projected into the active Revit view plane.
/// </summary>
public sealed record CadGridSegment2(
    int Id,
    CadGridPoint2 Start,
    CadGridPoint2 End,
    string? LayerName = null);

public readonly record struct CadGridSelectionBox(
    double MinXmm,
    double MinYmm,
    double MaxXmm,
    double MaxYmm)
{
    public static CadGridSelectionBox FromCorners(CadGridPoint2 first, CadGridPoint2 second) =>
        new(
            Math.Min(first.Xmm, second.Xmm),
            Math.Min(first.Ymm, second.Ymm),
            Math.Max(first.Xmm, second.Xmm),
            Math.Max(first.Ymm, second.Ymm));
}
