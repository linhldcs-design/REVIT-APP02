using RevitAPP.Core.Models.CadGrid;

namespace RevitAPP.Core.Services;

/// <summary>One CAD line placed relative to the user's origin pick, in millimetres.</summary>
public sealed record CadGridDirectLine(
    int Id,
    CadGridPoint2 Start,
    CadGridPoint2 End);

public sealed record CadGridDirectResult(
    IReadOnlyList<CadGridDirectLine> Lines,
    IReadOnlyList<int> SkippedIds,
    string? Error)
{
    public bool IsValid => Lines.Count > 0 && string.IsNullOrWhiteSpace(Error);
}

/// <summary>
/// Places every selected CAD line as a grid without requiring a two-family network, so
/// diagonals and one-off axes come through as-is. Coordinates are made relative to the
/// drawing's lower-left corner, which the caller then anchors at a picked Revit point.
/// </summary>
public static class CadGridDirectPlacer
{
    /// <summary>Lines shorter than this are drafting noise rather than grid axes.</summary>
    public const double MinimumLengthMm = 1.0;

    public static CadGridDirectResult Place(CadGridTransferPackage package)
    {
        try
        {
            CadGridTransferStore.Validate(package, TimeSpan.MaxValue);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException)
        {
            return new CadGridDirectResult(
                Array.Empty<CadGridDirectLine>(),
                Array.Empty<int>(),
                exception.Message);
        }

        var scale = CadGridUnitConverter.MillimetresPerDrawingUnit(package.InsUnits);
        var scaled = package.Lines
            .Select(line => new CadGridDirectLine(
                line.Id,
                new CadGridPoint2(line.StartX * scale, line.StartY * scale),
                new CadGridPoint2(line.EndX * scale, line.EndY * scale)))
            .ToArray();

        var usable = scaled
            .Where(line => line.Start.DistanceTo(line.End) >= MinimumLengthMm)
            .ToArray();
        var skipped = scaled
            .Where(line => line.Start.DistanceTo(line.End) < MinimumLengthMm)
            .Select(line => line.Id)
            .ToArray();

        if (usable.Length == 0)
            return new CadGridDirectResult(
                Array.Empty<CadGridDirectLine>(),
                skipped,
                "Không có line CAD nào đủ dài để tạo Grid.");

        // Shift so the drawing's lower-left corner sits at (0,0): the picked Revit point
        // then becomes that corner, and grid positions stay independent of WCS origin.
        var minX = usable.Min(line => Math.Min(line.Start.Xmm, line.End.Xmm));
        var minY = usable.Min(line => Math.Min(line.Start.Ymm, line.End.Ymm));
        var offset = new CadGridPoint2(minX, minY);

        var relative = usable
            .Select(line => new CadGridDirectLine(
                line.Id,
                line.Start - offset,
                line.End - offset))
            .ToArray();

        return new CadGridDirectResult(relative, skipped, null);
    }
}
