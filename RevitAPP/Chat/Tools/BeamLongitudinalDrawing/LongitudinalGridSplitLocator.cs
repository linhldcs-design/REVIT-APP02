using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Chat.BeamLongitudinalDrawing;

namespace RevitAPP.Chat.Tools.BeamLongitudinalDrawing;

internal sealed record LongitudinalGridSplit(XYZ Point, double StationFeet, string GridName);

internal sealed class LongitudinalGridSplitLocator
{
    public LongitudinalGridSplit Find(Document document, FamilyInstance beam)
    {
        if (beam.Location is not LocationCurve { Curve: Line beamLine })
            throw new InvalidOperationException("Chỉ hỗ trợ dầm thẳng khi chia view theo lưới.");

        var start = beamLine.GetEndPoint(0);
        var end = beamLine.GetEndPoint(1);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthXy = Math.Sqrt(dx * dx + dy * dy);
        if (lengthXy < 1e-9) throw new InvalidOperationException("Tim dầm không hợp lệ.");

        var candidates = new List<(double Station, string Name)>();
        foreach (var grid in new FilteredElementCollector(document)
                     .OfClass(typeof(Grid)).Cast<Grid>())
        {
            if (grid.Curve is not Line gridLine) continue;
            var gridOrigin = gridLine.Origin;
            var gridDirection = gridLine.Direction;
            var denominator = Cross2D(dx, dy, gridDirection.X, gridDirection.Y);
            if (Math.Abs(denominator) < 1e-10) continue;

            var ox = gridOrigin.X - start.X;
            var oy = gridOrigin.Y - start.Y;
            var beamParameter = Cross2D(ox, oy, gridDirection.X, gridDirection.Y) / denominator;
            if (beamParameter < 0 || beamParameter > 1) continue;
            var intersection = new XYZ(
                start.X + dx * beamParameter,
                start.Y + dy * beamParameter,
                start.Z + (end.Z - start.Z) * beamParameter);
            if (gridLine.IsBound)
            {
                var gridStart = gridLine.GetEndPoint(0);
                var gridEnd = gridLine.GetEndPoint(1);
                var gridLength = gridStart.DistanceTo(gridEnd);
                var alongGrid = (intersection - gridStart).DotProduct(gridDirection);
                const double extentToleranceFeet = 1.0 / 304.8;
                if (alongGrid < -extentToleranceFeet || alongGrid > gridLength + extentToleranceFeet)
                    continue;
            }
            candidates.Add((beamParameter * lengthXy, grid.Name));
        }

        var station = LongitudinalGridStationPlanner.SelectNearestMidpoint(
            lengthXy, candidates.Select(item => item.Station), 1.0 / 304.8);
        var selected = candidates
            .Where(item => Math.Abs(item.Station - station) < 1e-7)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .First();
        var ratio = station / lengthXy;
        return new LongitudinalGridSplit(
            new XYZ(start.X + dx * ratio, start.Y + dy * ratio, start.Z + (end.Z - start.Z) * ratio),
            station, selected.Name);
    }

    private static double Cross2D(double ax, double ay, double bx, double by) => ax * by - ay * bx;
}
