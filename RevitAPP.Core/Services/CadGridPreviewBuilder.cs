using RevitAPP.Core.Models.CadGrid;

namespace RevitAPP.Core.Services;

/// <summary>How a previewed CAD line relates to the detected grid network.</summary>
public enum CadGridAxisKind
{
    /// <summary>Belongs to one of the two dominant parallel families.</summary>
    Family,

    /// <summary>Runs at its own angle: a diagonal or one-off axis.</summary>
    Skew
}

public sealed record CadGridPreviewAxis(
    int Id,
    CadGridPoint2 Start,
    CadGridPoint2 End,
    CadGridAxisKind Kind,
    string SuggestedName)
{
    /// <summary>
    /// Undirected angle in [0, 180): a line and its reverse describe the same axis, so
    /// endpoint order in the DWG must not change the reported angle.
    /// </summary>
    public double AngleDegrees
    {
        get
        {
            var angle = Math.Atan2(End.Ymm - Start.Ymm, End.Xmm - Start.Xmm) * 180.0 / Math.PI;
            if (angle < 0) angle += 180.0;
            return angle >= 180.0 - 1e-9 ? 0.0 : angle;
        }
    }

    public double LengthMm => Start.DistanceTo(End);
}

public sealed record CadGridPreview(
    IReadOnlyList<CadGridPreviewAxis> Axes,
    IReadOnlyList<int> SkippedIds,
    string? Error)
{
    public bool IsValid => Axes.Count > 0 && string.IsNullOrWhiteSpace(Error);

    public double WidthMm => Axes.Count == 0
        ? 0
        : Axes.Max(axis => Math.Max(axis.Start.Xmm, axis.End.Xmm));

    public double HeightMm => Axes.Count == 0
        ? 0
        : Axes.Max(axis => Math.Max(axis.Start.Ymm, axis.End.Ymm));
}

/// <summary>
/// Turns a CAD selection into a reviewable axis list. Every line is kept — including
/// diagonals the two-family network analysis cannot describe — and each is labelled so
/// the user can decide what to create.
/// </summary>
public static class CadGridPreviewBuilder
{
    /// <summary>Lines closer than this to a family direction count as part of it.</summary>
    private const double FamilyToleranceDegrees = 1.0;

    public static CadGridPreview Build(CadGridTransferPackage package)
    {
        var placement = CadGridDirectPlacer.Place(package);
        if (!placement.IsValid)
            return new CadGridPreview(
                Array.Empty<CadGridPreviewAxis>(),
                placement.SkippedIds,
                placement.Error);

        var directions = placement.Lines
            .Select(line => new CadGridPoint2(
                line.End.Xmm - line.Start.Xmm,
                line.End.Ymm - line.Start.Ymm))
            .ToArray();

        // The two most populated directions are the grid's main families; anything else
        // is a skew axis the user may still want as a grid.
        var dominant = DominantDirections(directions);

        var members = new List<(int Index, int Family, double Position)>();
        for (var index = 0; index < placement.Lines.Count; index++)
        {
            var familyIndex = MatchFamily(directions[index], dominant);
            members.Add((index, familyIndex, SortPosition(placement.Lines[index])));
        }

        // Names must read the way the drawing does — left to right, bottom to top — so
        // each family is numbered by position rather than by order in the DWG file.
        var names = new string[placement.Lines.Count];
        foreach (var family in members.Where(item => item.Family >= 0)
                     .GroupBy(item => item.Family))
        {
            // Vertical axes take numbers and horizontal axes take letters, following the
            // usual drafting convention. Which family is larger is irrelevant.
            var isVertical = IsVertical(placement.Lines[family.First().Index]);
            var ordered = family.OrderBy(item => item.Position).ToArray();
            for (var rank = 0; rank < ordered.Length; rank++)
                names[ordered[rank].Index] = isVertical
                    ? (rank + 1).ToString()
                    : ToLetters(rank + 1);
        }

        var skewRank = 0;
        foreach (var skew in members.Where(item => item.Family < 0)
                     .OrderBy(item => item.Position))
            names[skew.Index] = "X" + ++skewRank;

        var axes = new List<CadGridPreviewAxis>(placement.Lines.Count);
        for (var index = 0; index < placement.Lines.Count; index++)
        {
            var line = placement.Lines[index];
            axes.Add(
                new CadGridPreviewAxis(
                    line.Id,
                    line.Start,
                    line.End,
                    members[index].Family >= 0 ? CadGridAxisKind.Family : CadGridAxisKind.Skew,
                    names[index]));
        }

        return new CadGridPreview(axes, placement.SkippedIds, null);
    }

    /// <summary>
    /// The two directions shared by the most lines, compared without regard to which way
    /// each line was drawn.
    /// </summary>
    private static IReadOnlyList<CadGridPoint2> DominantDirections(
        IReadOnlyList<CadGridPoint2> directions)
    {
        var groups = new List<(CadGridPoint2 Direction, int Count)>();
        foreach (var direction in directions)
        {
            var matched = false;
            for (var index = 0; index < groups.Count; index++)
            {
                if (CadGridFamilyAssigner.AngleBetweenDegrees(groups[index].Direction, direction)
                    > FamilyToleranceDegrees) continue;

                groups[index] = (groups[index].Direction, groups[index].Count + 1);
                matched = true;
                break;
            }

            if (!matched) groups.Add((direction, 1));
        }

        return groups
            .Where(group => group.Count >= 2)
            .OrderByDescending(group => group.Count)
            .Take(2)
            .Select(group => group.Direction)
            .ToArray();
    }

    /// <summary>
    /// Where an axis sits when read the way a drawing is read. A mostly-vertical line is
    /// ordered by X so numbering runs left to right; a mostly-horizontal line is ordered
    /// by Y so lettering runs bottom to top.
    /// </summary>
    private static double SortPosition(CadGridDirectLine line) =>
        IsVertical(line)
            ? (line.Start.Xmm + line.End.Xmm) / 2.0
            : (line.Start.Ymm + line.End.Ymm) / 2.0;

    /// <summary>A line closer to vertical than horizontal, however slightly skewed.</summary>
    private static bool IsVertical(CadGridDirectLine line) =>
        Math.Abs(line.End.Ymm - line.Start.Ymm)
        >= Math.Abs(line.End.Xmm - line.Start.Xmm);

    private static int MatchFamily(
        CadGridPoint2 direction,
        IReadOnlyList<CadGridPoint2> dominant)
    {
        for (var index = 0; index < dominant.Count; index++)
            if (CadGridFamilyAssigner.AngleBetweenDegrees(dominant[index], direction)
                <= FamilyToleranceDegrees)
                return index;
        return -1;
    }

    private static string ToLetters(int position)
    {
        var characters = new Stack<char>();
        while (position > 0)
        {
            var remainder = (position - 1) % 26;
            characters.Push((char)('A' + remainder));
            position = (position - 1) / 26;
        }

        return new string(characters.ToArray());
    }
}
