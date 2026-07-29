namespace RevitAPP.Core.Chat.BeamLongitudinalDrawing;

public sealed record LongitudinalCropRange(double Minimum, double Maximum);

public static class LongitudinalGridStationPlanner
{
    public static double SelectNearestMidpoint(double totalLength, IEnumerable<double> stations,
        double endpointTolerance)
    {
        if (totalLength <= 0) throw new ArgumentOutOfRangeException(nameof(totalLength));
        if (endpointTolerance < 0) throw new ArgumentOutOfRangeException(nameof(endpointTolerance));

        var candidates = stations
            .Where(value => value > endpointTolerance && value < totalLength - endpointTolerance)
            .OrderBy(value => Math.Abs(value - totalLength * 0.5))
            .ThenBy(value => value)
            .ToList();
        return candidates.Count > 0
            ? candidates[0]
            : throw new InvalidOperationException("Không có lưới nội bộ hợp lệ để chia dầm.");
    }
}

public static class LongitudinalDependentCropPlanner
{
    public static (LongitudinalCropRange First, LongitudinalCropRange Second) Plan(
        double cropMinimum,
        double cropMaximum,
        double splitCoordinate,
        double requestedOverlap)
    {
        if (cropMaximum <= cropMinimum) throw new ArgumentException("Crop box không hợp lệ.");
        if (splitCoordinate <= cropMinimum || splitCoordinate >= cropMaximum)
            throw new ArgumentOutOfRangeException(nameof(splitCoordinate));
        if (requestedOverlap < 0) throw new ArgumentOutOfRangeException(nameof(requestedOverlap));

        var requestedExtension = requestedOverlap * 0.5;
        var firstExtension = Math.Min(requestedExtension, cropMaximum - splitCoordinate);
        var secondExtension = Math.Min(requestedExtension, splitCoordinate - cropMinimum);
        return (
            new LongitudinalCropRange(cropMinimum, splitCoordinate + firstExtension),
            new LongitudinalCropRange(splitCoordinate - secondExtension, cropMaximum));
    }
}

public sealed record LongitudinalBreakLinePositions(double First, double Second);

public static class LongitudinalBreakLinePlanner
{
    public static LongitudinalBreakLinePositions Plan(
        double splitCoordinate,
        LongitudinalCropRange first,
        LongitudinalCropRange second,
        double desiredDistance,
        double cropPastLine)
    {
        if (desiredDistance < 0) throw new ArgumentOutOfRangeException(nameof(desiredDistance));
        if (cropPastLine < 0) throw new ArgumentOutOfRangeException(nameof(cropPastLine));
        if (first.Minimum >= splitCoordinate || first.Maximum <= splitCoordinate ||
            second.Minimum >= splitCoordinate || second.Maximum <= splitCoordinate)
            throw new ArgumentException("Phạm vi crop phải chứa tọa độ lưới chia.");

        var firstAvailable = first.Maximum - splitCoordinate;
        var secondAvailable = splitCoordinate - second.Minimum;
        var firstInset = Math.Min(cropPastLine, firstAvailable * 0.5);
        var secondInset = Math.Min(cropPastLine, secondAvailable * 0.5);
        return new LongitudinalBreakLinePositions(
            splitCoordinate + Math.Min(desiredDistance, firstAvailable - firstInset),
            splitCoordinate - Math.Min(desiredDistance, secondAvailable - secondInset));
    }
}

public sealed record LongitudinalViewportTitleOffset(double X, double Y);

public static class LongitudinalVerticalMarginPlanner
{
    public static double Select(
        double sheetHeight,
        double requiredHeight,
        double preferredMargin,
        double minimumMargin)
    {
        if (sheetHeight <= 0) throw new ArgumentOutOfRangeException(nameof(sheetHeight));
        if (requiredHeight < 0) throw new ArgumentOutOfRangeException(nameof(requiredHeight));
        if (minimumMargin < 0 || preferredMargin < minimumMargin)
            throw new ArgumentOutOfRangeException(nameof(preferredMargin));

        var maximumAvailable = sheetHeight - minimumMargin * 2;
        if (requiredHeight > maximumAvailable + 1e-9)
            throw new InvalidOperationException("Nội dung vượt chiều cao khả dụng của sheet.");

        return Math.Min(preferredMargin, (sheetHeight - requiredHeight) * 0.5);
    }
}

public static class LongitudinalViewportTitleOffsetPlanner
{
    public static LongitudinalViewportTitleOffset CenterBelowView(
        double boxMinX, double boxMaxX, double boxMinY,
        double labelMinX, double labelMaxX, double labelMaxY,
        double currentX, double currentY, double gap)
    {
        if (gap < 0) throw new ArgumentOutOfRangeException(nameof(gap));
        var deltaX = (boxMinX + boxMaxX - labelMinX - labelMaxX) * 0.5;
        var deltaY = boxMinY - gap - labelMaxY;
        return new LongitudinalViewportTitleOffset(currentX + deltaX, currentY + deltaY);
    }
}
