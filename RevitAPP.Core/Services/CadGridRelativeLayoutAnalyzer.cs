using RevitAPP.Core.Models.CadGrid;

namespace RevitAPP.Core.Services;

public static class CadGridRelativeLayoutAnalyzer
{
    public static CadGridRelativeLayoutResult Analyze(CadGridTransferPackage package)
    {
        try
        {
            CadGridTransferStore.Validate(package, TimeSpan.MaxValue);
            var scale = CadGridUnitConverter.MillimetresPerDrawingUnit(package.InsUnits);
            var sourceById = package.Lines.ToDictionary(line => line.Id);
            var segments = package.Lines.Select(line => new CadGridSegment2(
                line.Id,
                new CadGridPoint2(line.StartX * scale, line.StartY * scale),
                new CadGridPoint2(line.EndX * scale, line.EndY * scale))).ToArray();

            var analysis = CadGridNetworkAnalyzer.Analyze(segments);
            if (!analysis.IsValid || analysis.Network is null)
                return CadGridRelativeLayoutResult.Invalid(
                    analysis.Error ?? "Không nhận diện được mạng lưới.");

            var corner = FindLowerLeftCorner(analysis.Network.Intersections);
            var first = BuildFamily(analysis.Network.FirstFamily, corner, sourceById, scale);
            var second = BuildFamily(analysis.Network.SecondFamily, corner, sourceById, scale);
            if (first is null || second is null)
                return CadGridRelativeLayoutResult.Invalid(
                    "Góc dưới-trái của mạng CAD không nằm ở đầu chuỗi của cả hai họ trục.");

            return CadGridRelativeLayoutResult.Valid(
                new CadGridRelativeLayout(first, second, corner.Point, analysis.Network));
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or ArgumentException
            or InvalidOperationException)
        {
            return CadGridRelativeLayoutResult.Invalid(exception.Message);
        }
    }

    private static CadGridIntersection FindLowerLeftCorner(
        IReadOnlyList<CadGridIntersection> intersections)
    {
        var minX = intersections.Min(item => item.Point.Xmm);
        var maxX = intersections.Max(item => item.Point.Xmm);
        var minY = intersections.Min(item => item.Point.Ymm);
        var maxY = intersections.Max(item => item.Point.Ymm);
        var rangeX = Math.Max(maxX - minX, 1.0);
        var rangeY = Math.Max(maxY - minY, 1.0);
        return intersections
            .OrderBy(item =>
                (item.Point.Xmm - minX) / rangeX + (item.Point.Ymm - minY) / rangeY)
            .First();
    }

    private static CadGridRelativeFamily? BuildFamily(
        CadGridNetworkFamily family,
        CadGridIntersection corner,
        IReadOnlyDictionary<int, CadGridTransferLine> sourceById,
        double scale)
    {
        var cornerId = family.OrderedSegmentIds.Contains(corner.FirstSegmentId)
            ? corner.FirstSegmentId
            : corner.SecondSegmentId;
        var index = family.OrderedSegmentIds.IndexOf(cornerId);
        if (index != 0 && index != family.OrderedSegmentIds.Count - 1) return null;

        IReadOnlyList<int> ids;
        IReadOnlyList<double> distances;
        if (index == 0)
        {
            ids = family.OrderedSegmentIds;
            distances = family.ConsecutiveDistancesMm;
        }
        else
        {
            ids = family.OrderedSegmentIds.Reverse().ToArray();
            distances = family.ConsecutiveDistancesMm.Reverse().ToArray();
        }

        var offsets = new List<double> { 0.0 };
        foreach (var distance in distances) offsets.Add(offsets[^1] + distance);

        var line = sourceById[ids[0]];
        var dx = (line.EndX - line.StartX) * scale;
        var dy = (line.EndY - line.StartY) * scale;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return new CadGridRelativeFamily(
            new CadGridPoint2(dx / length, dy / length),
            offsets,
            ids);
    }

    private static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
            if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
        return -1;
    }
}
