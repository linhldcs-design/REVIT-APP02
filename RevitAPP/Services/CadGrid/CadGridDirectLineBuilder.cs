using Autodesk.Revit.DB;
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
    {
        var planned = new List<CadGridPlannedGrid>(axes.Count);
        foreach (var axis in axes)
        {
            var start = new XYZ(
                origin.X + axis.Start.Xmm / MillimetresPerFoot,
                origin.Y + axis.Start.Ymm / MillimetresPerFoot,
                origin.Z);
            var end = new XYZ(
                origin.X + axis.End.Xmm / MillimetresPerFoot,
                origin.Y + axis.End.Ymm / MillimetresPerFoot,
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
