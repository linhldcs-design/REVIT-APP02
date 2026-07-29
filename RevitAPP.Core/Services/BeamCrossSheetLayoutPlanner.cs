namespace RevitAPP.Core.Services;

/// <summary>
///     Tìm vị trí trống cho các cụm viewport mặt cắt ngang vừa tạo.
///     Nội dung đã có trên sheet chỉ là vùng chiếm chỗ và không bao giờ xuất hiện trong kết quả di chuyển.
/// </summary>
public static class BeamCrossSheetLayoutPlanner
{
    private const double IntraBeamCenterFeet = 0.213;
    private const double BeamPitchXFeet = 0.466;
    private const double RowPitchYFeet = 0.2075;
    private const int BeamsPerRow = 3;
    private const double MinimumFootprintGapFeet = 2.0 / 304.8;

    public static IReadOnlyList<BeamCrossViewportPlacement> Plan(
        double left, double right, double top, double bottom,
        IReadOnlyList<BeamCrossViewportGroup> newGroups,
        IReadOnlyList<BeamSheetRect> occupied)
    {
        if (right <= left || top <= bottom)
            throw new ArgumentException("Vùng bố trí trên sheet không hợp lệ.");
        if (newGroups.Count == 0) return Array.Empty<BeamCrossViewportPlacement>();
        if (newGroups.Any(group => group.Viewports.Count == 0))
            throw new ArgumentException("Cụm viewport mới không được rỗng.");

        var newIds = newGroups.SelectMany(group => group.Viewports)
            .Select(viewport => viewport.ViewportId).ToList();
        if (newIds.Any(id => id <= 0) || newIds.Distinct().Count() != newIds.Count)
            throw new ArgumentException("ViewportId mới phải dương và không được trùng nhau.");
        if (newGroups.SelectMany(group => group.Viewports)
            .Any(viewport => viewport.Width <= 0 || viewport.Height <= 0))
            throw new ArgumentException("Kích thước viewport phải lớn hơn 0.");

        var reserved = occupied.ToList();
        var placements = new List<BeamCrossViewportPlacement>(newIds.Count);
        var candidates = CandidateClusterCenters(left, right, top, bottom).ToList();

        foreach (var group in newGroups)
        {
            var placed = false;
            foreach (var candidate in candidates)
            {
                var targets = TargetRects(group, candidate.X, candidate.Y);
                if (targets.Any(rect => !rect.IsInside(left, right, top, bottom))) continue;
                if (targets.Any(rect => reserved.Any(rect.Intersects))) continue;

                var offsets = ViewportCenterOffsets(group);
                for (var index = 0; index < group.Viewports.Count; index++)
                {
                    placements.Add(new BeamCrossViewportPlacement(
                        group.Viewports[index].ViewportId, candidate.X + offsets[index], candidate.Y));
                }
                reserved.AddRange(targets);
                placed = true;
                break;
            }

            if (!placed)
                throw new InvalidOperationException(
                    "Không còn vùng trống trên sheet để đặt mặt cắt mới mà không chồng lên nội dung hiện có.");
        }

        return placements;
    }

    private static IEnumerable<(double X, double Y)> CandidateClusterCenters(
        double left, double right, double top, double bottom)
    {
        var rowWidth = (BeamsPerRow - 1) * BeamPitchXFeet + IntraBeamCenterFeet;
        var firstX = left + Math.Max(0, (right - left - rowWidth) / 2.0);
        var rowCount = Math.Max(1, (int)Math.Floor((top - bottom) / RowPitchYFeet) + 1);
        var firstY = top - RowPitchYFeet / 2.0;

        for (var row = 0; row < rowCount; row++)
        {
            var y = firstY - row * RowPitchYFeet;
            for (var col = 0; col < BeamsPerRow; col++)
                yield return (firstX + col * BeamPitchXFeet, y);
        }
    }

    private static List<BeamSheetRect> TargetRects(BeamCrossViewportGroup group, double firstX, double y)
    {
        var result = new List<BeamSheetRect>(group.Viewports.Count);
        var offsets = ViewportCenterOffsets(group);
        for (var index = 0; index < group.Viewports.Count; index++)
        {
            var viewport = group.Viewports[index];
            result.Add(BeamSheetRect.Centered(
                firstX + offsets[index] + viewport.FootprintOffsetX,
                y + viewport.FootprintOffsetY,
                viewport.Width,
                viewport.Height));
        }
        return result;
    }

    private static IReadOnlyList<double> ViewportCenterOffsets(BeamCrossViewportGroup group)
    {
        var offsets = new double[group.Viewports.Count];
        for (var index = 1; index < group.Viewports.Count; index++)
        {
            var previous = group.Viewports[index - 1];
            var current = group.Viewports[index];
            var minimumDelta =
                previous.FootprintOffsetX + previous.Width / 2.0 + MinimumFootprintGapFeet -
                current.FootprintOffsetX + current.Width / 2.0;
            offsets[index] = offsets[index - 1] + Math.Max(IntraBeamCenterFeet, minimumDelta);
        }
        return offsets;
    }
}

public sealed record BeamCrossViewportFootprint(
    long ViewportId,
    double Width,
    double Height,
    double FootprintOffsetX,
    double FootprintOffsetY);

public sealed record BeamCrossViewportGroup(IReadOnlyList<BeamCrossViewportFootprint> Viewports);

public sealed record BeamCrossViewportPlacement(long ViewportId, double BoxCenterX, double BoxCenterY);

public sealed record BeamSheetRect(double MinX, double MinY, double MaxX, double MaxY)
{
    private const double Tolerance = 1.0 / 304.8;

    public bool Intersects(BeamSheetRect other) =>
        MinX < other.MaxX + Tolerance && MaxX > other.MinX - Tolerance &&
        MinY < other.MaxY + Tolerance && MaxY > other.MinY - Tolerance;

    public bool IsInside(double left, double right, double top, double bottom) =>
        MinX >= left - Tolerance && MaxX <= right + Tolerance &&
        MinY >= bottom - Tolerance && MaxY <= top + Tolerance;

    public static BeamSheetRect Centered(double x, double y, double width, double height) =>
        new(x - width / 2.0, y - height / 2.0, x + width / 2.0, y + height / 2.0);
}
