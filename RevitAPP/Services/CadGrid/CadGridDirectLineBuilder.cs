using Autodesk.Revit.DB;
using RevitAPP.Core.Models.CadStructure;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.CadGrid;

internal static class CadGridDirectLineBuilder
{
    /// <summary>Revit internal units are feet; preview coordinates are millimetres.</summary>
    private const double MillimetresPerFoot = 304.8;

    /// <summary>
    /// Anchors the reviewed axes at the picked point, keeping each line's own angle and
    /// length so diagonals stay diagonal.
    /// </summary>
    public static IReadOnlyList<CadGridPlannedGrid> Build(
        IReadOnlyList<CadGridPreviewAxis> axes,
        XYZ origin)
        => Build(axes, origin, new CadStructurePoint2(0, 0), 0);

    /// <summary>
    /// Places preview coordinates relative to the explicit CAD anchor and applies the
    /// optional plan rotation. The original overload remains unchanged for Grid V1.
    /// </summary>
    public static IReadOnlyList<CadGridPlannedGrid> Build(
        IReadOnlyList<CadGridPreviewAxis> axes,
        XYZ origin,
        CadStructurePoint2 sourceAnchorMm,
        double rotationDegrees)
    {
        var radians = rotationDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var planned = new List<CadGridPlannedGrid>(axes.Count);
        foreach (var axis in axes)
        {
            var startRelativeX = axis.Start.Xmm - sourceAnchorMm.X;
            var startRelativeY = axis.Start.Ymm - sourceAnchorMm.Y;
            var endRelativeX = axis.End.Xmm - sourceAnchorMm.X;
            var endRelativeY = axis.End.Ymm - sourceAnchorMm.Y;
            var start = new XYZ(
                origin.X + (startRelativeX * cosine - startRelativeY * sine) / MillimetresPerFoot,
                origin.Y + (startRelativeX * sine + startRelativeY * cosine) / MillimetresPerFoot,
                origin.Z);
            var end = new XYZ(
                origin.X + (endRelativeX * cosine - endRelativeY * sine) / MillimetresPerFoot,
                origin.Y + (endRelativeX * sine + endRelativeY * cosine) / MillimetresPerFoot,
                origin.Z);

            planned.Add(
                new CadGridPlannedGrid(
                    Line.CreateBound(start, end),
                    0,
                    $"CAD #{axis.Id}",
                    string.IsNullOrWhiteSpace(axis.SuggestedName) ? null : axis.SuggestedName));
        }

        return planned;
    }
}
