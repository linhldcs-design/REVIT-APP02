using RevitAPP.Core.Models.CadGrid;

namespace RevitAPP.Core.Services;

/// <summary>
/// Works out how far a grid line must reach to span the network. Kept free of Revit
/// types so the endpoint arithmetic — which decides whether an anchor grows, shrinks or
/// shifts — can be tested outside Revit.
/// </summary>
public static class CadGridSpanCalculator
{
    /// <summary>
    /// Re-cuts the segment along its own axis so it runs from the trailing end across the
    /// full <paramref name="reach"/> of the crossing family, with <paramref name="margin"/>
    /// beyond each end. <paramref name="advanceTowardEnd"/> says whether the crossing grids
    /// march toward the segment's far end.
    /// <para>
    /// The trailing end is the network corner and stays put; the leading end is placed at
    /// the full reach so every grid in the network finishes flush. A segment already longer
    /// than the reach keeps its own length rather than being cut back. The line is never
    /// moved sideways — a shifted anchor would silently relocate the whole grid.
    /// </para>
    /// </summary>
    public static (CadGridPoint2 Start, CadGridPoint2 End) Span(
        CadGridPoint2 start,
        CadGridPoint2 end,
        bool advanceTowardEnd,
        double reach,
        double margin)
    {
        var dx = end.Xmm - start.Xmm;
        var dy = end.Ymm - start.Ymm;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0) throw new ArgumentException("Đoạn thẳng có độ dài bằng 0.");

        var direction = new CadGridPoint2(dx / length, dy / length);
        // Reach past the far end, or the segment's own length when it already extends
        // beyond the network.
        var span = Math.Max(length, reach) + margin;

        return advanceTowardEnd
            ? (start - direction * margin, start + direction * span)
            : (end - direction * span, end + direction * margin);
    }
}
