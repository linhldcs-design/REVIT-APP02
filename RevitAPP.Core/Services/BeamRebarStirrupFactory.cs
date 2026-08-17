using RevitAPP.Core.Models;

namespace RevitAPP.Core.Services;

/// <summary>Một vùng rải đai: đoạn [FromMm, ToMm] dọc nhịp với bước riêng.</summary>
public readonly record struct BeamStirrupZone(double FromMm, double ToMm, double SpacingMm, string Zone)
{
    public double LengthMm => ToMm - FromMm;
}

/// <summary>
/// Vùng quanh một dầm phụ mà đai chính phải tránh, kèm hai cụm đai tăng cường hai bên.
/// </summary>
public readonly record struct BeamSecondaryStirrupRange(
    double StationMm,
    double LeftClusterStartMm,
    double LeftClusterEndMm,
    double RightClusterStartMm,
    double RightClusterEndMm);

/// <summary>
/// Phân vùng và định vị cốt đai, độc lập Revit. Số đai sinh ra ở đây bằng đúng số đai builder tạo
/// trong mô hình, nên bản xem trước phản ánh đúng khối lượng thép.
/// </summary>
public static class BeamRebarStirrupFactory
{
    /// <summary>Bước rải cụm đai tăng cường quanh dầm phụ (mm).</summary>
    public const double SecondaryTieSpacingMm = 50;

    /// <summary>Số khoảng của mỗi cụm đai tăng cường.</summary>
    private const int SecondaryClusterIntervals = 3;

    /// <summary>
    /// Trần số thanh sinh ra. Bước đai nhập quá nhỏ có thể sinh hàng triệu thanh và treo giao diện,
    /// nên chặn trước khi dựng thay vì để cạn bộ nhớ.
    /// </summary>
    public const int MaxPaths = 20_000;

    /// <summary>
    /// Ba vùng đai theo TCVN: dày hai đầu, thưa ở giữa. Hai vùng dày cộng lại vượt chiều dài nhịp thì
    /// co lại theo tỉ lệ để không chồng nhau.
    /// </summary>
    public static IReadOnlyList<BeamStirrupZone> Zones(
        double lengthMm,
        double spacingEndMm,
        double spacingMidMm,
        bool twoEnds,
        double endZoneLengthMm = 0,
        double endZoneStartMm = 0,
        double endZoneEndMm = 0)
    {
        if (lengthMm <= 1e-6) return [];
        if (!twoEnds) return [new BeamStirrupZone(0, lengthMm, spacingEndMm, "Uniform")];

        var fallback = endZoneLengthMm > 0 ? endZoneLengthMm : lengthMm / 4;
        var startZone = endZoneStartMm > 0 ? endZoneStartMm : fallback;
        var endZone = endZoneEndMm > 0 ? endZoneEndMm : fallback;

        startZone = Math.Min(startZone, lengthMm / 2);
        endZone = Math.Min(endZone, lengthMm / 2);
        if (startZone + endZone > lengthMm)
        {
            var scale = lengthMm / (startZone + endZone);
            startZone *= scale;
            endZone *= scale;
        }

        return
        [
            new BeamStirrupZone(0, startZone, spacingEndMm, "End1"),
            new BeamStirrupZone(startZone, lengthMm - endZone, spacingMidMm, "Mid"),
            new BeamStirrupZone(lengthMm - endZone, lengthMm, spacingEndMm, "End2")
        ];
    }

    /// <summary>
    /// Vùng chặn quanh mỗi dầm phụ. Dầm phụ nằm quá sát hai đầu nhịp bị bỏ qua vì cụm đai tăng cường
    /// sẽ tràn ra ngoài nhịp.
    /// </summary>
    public static IReadOnlyList<BeamSecondaryStirrupRange> SecondaryRanges(
        IReadOnlyList<(double StationMm, double HalfWidthMm)> secondaries,
        double spanLengthMm,
        double stirrupDiameterMm)
    {
        if (secondaries.Count == 0) return [];

        var clearance = SecondaryTieSpacingMm + stirrupDiameterMm / 2;
        var clusterLength = SecondaryClusterIntervals * SecondaryTieSpacingMm;
        var ranges = new List<BeamSecondaryStirrupRange>();

        foreach (var (station, halfWidth) in secondaries)
        {
            if (station <= 0 || station >= spanLengthMm) continue;

            var leftEnd = station - Math.Max(0, halfWidth) - clearance;
            var leftStart = leftEnd - clusterLength;
            var rightStart = station + Math.Max(0, halfWidth) + clearance;
            var rightEnd = rightStart + clusterLength;
            if (leftStart <= 0 || rightEnd >= spanLengthMm) continue;

            ranges.Add(new BeamSecondaryStirrupRange(station, leftStart, leftEnd, rightStart, rightEnd));
        }

        return ranges.OrderBy(r => r.LeftClusterStartMm).ToList();
    }

    /// <summary>Phần còn lại của một vùng sau khi cắt bỏ các đoạn bị dầm phụ chiếm.</summary>
    public static IReadOnlyList<(double FromMm, double ToMm)> SubtractBlocked(
        BeamStirrupZone zone, IReadOnlyList<BeamSecondaryStirrupRange> blocked)
    {
        var segments = new List<(double, double)>();
        var cursor = zone.FromMm;

        foreach (var range in blocked.OrderBy(b => b.LeftClusterStartMm))
        {
            var blockStart = range.LeftClusterStartMm;
            var blockEnd = range.RightClusterEndMm;
            if (blockEnd <= cursor || blockStart >= zone.ToMm) continue;

            var segmentEnd = Math.Min(blockStart, zone.ToMm);
            if (segmentEnd > cursor) segments.Add((cursor, segmentEnd));
            cursor = Math.Max(cursor, blockEnd);
            if (cursor >= zone.ToMm) break;
        }

        if (cursor < zone.ToMm) segments.Add((cursor, zone.ToMm));
        return segments;
    }

    /// <summary>
    /// Vị trí mọi đai chính dọc nhịp, đã trừ vùng dầm phụ và đã nhân bản theo cách Revit rải.
    /// </summary>
    public static IReadOnlyList<(double StationMm, string Zone)> MainStirrupStations(
        IReadOnlyList<BeamStirrupZone> zones, IReadOnlyList<BeamSecondaryStirrupRange> blocked)
    {
        var stations = new List<(double, string)>();
        foreach (var zone in zones)
        {
            if (zone.SpacingMm <= 0) continue;
            foreach (var (from, to) in SubtractBlocked(zone, blocked))
            {
                foreach (var offset in RebarLayoutMath.MaximumSpacingStations(to - from, zone.SpacingMm))
                    stations.Add((from + offset, zone.Zone));
            }
        }
        return stations;
    }

    /// <summary>Vị trí đai trong hai cụm tăng cường hai bên mỗi dầm phụ.</summary>
    public static IReadOnlyList<double> SecondaryClusterStations(BeamSecondaryStirrupRange range)
    {
        var stations = new List<double>();
        foreach (var (from, to) in new[]
                 {
                     (range.LeftClusterStartMm, range.LeftClusterEndMm),
                     (range.RightClusterStartMm, range.RightClusterEndMm)
                 })
        {
            foreach (var offset in RebarLayoutMath.MaximumSpacingStations(to - from, SecondaryTieSpacingMm))
                stations.Add(from + offset);
        }
        return stations;
    }

    /// <summary>
    /// Bốn góc khung đai chữ nhật kín tại vị trí dọc <paramref name="stationMm"/>, đo từ mép bê tông
    /// trừ lớp bảo vệ.
    /// </summary>
    public static IReadOnlyList<GeometryPoint3D> ClosedProfile(
        PureSpanFrame frame, BeamCoverMm cover, double diameterMm, double stationMm)
    {
        var halfWidth = frame.WidthMm / 2 - cover.SideMm - diameterMm / 2;
        var (top, bottom) = VerticalExtent(frame, cover, diameterMm);
        return Rectangle(frame, stationMm, -halfWidth, halfWidth, top, bottom);
    }

    /// <summary>Khung đai phụ hẹp ôm một dải thanh chủ giữa, cao bằng đai chính.</summary>
    public static IReadOnlyList<GeometryPoint3D> NarrowProfile(
        PureSpanFrame frame, BeamCoverMm cover, double diameterMm, double stationMm,
        double leftLateralMm, double rightLateralMm)
    {
        var (top, bottom) = VerticalExtent(frame, cover, diameterMm);
        return Rectangle(frame, stationMm, leftLateralMm, rightLateralMm, top, bottom);
    }

    /// <summary>
    /// Thép phụ móc C: một cây thẳng đứng tại vị trí một cột thanh, hai đầu quặp ôm thanh chủ.
    /// </summary>
    public static IReadOnlyList<GeometryPoint3D> CHookProfile(
        PureSpanFrame frame, BeamCoverMm cover, double diameterMm, double stationMm, double lateralMm)
    {
        var (top, bottom) = VerticalExtent(frame, cover, diameterMm);
        return
        [
            frame.PointAtStation(stationMm, lateralMm, top),
            frame.PointAtStation(stationMm, lateralMm, bottom)
        ];
    }

    /// <summary>Vị trí ngang thanh chủ thứ <paramref name="barIndex"/> (0-based) trong tiết diện.</summary>
    public static double MainBarLateralMm(int barIndex, int mainBarCount, double usableHalfMm) =>
        mainBarCount <= 1 ? 0 : -usableHalfMm + barIndex * (2 * usableHalfMm / (mainBarCount - 1));

    /// <summary>
    /// Nửa bề rộng khả dụng cho đai phụ. Khớp cách đặt thép chủ: thanh chủ nằm trong đai nên lùi thêm
    /// một đường kính đai.
    /// </summary>
    public static double MainBarUsableHalfMm(
        PureSpanFrame frame, BeamCoverMm cover, double stirrupDiameterMm, double mainBarDiameterMm) =>
        Math.Max(0, frame.WidthMm / 2 - cover.SideMm - stirrupDiameterMm - mainBarDiameterMm / 2);

    /// <summary>
    /// Chặn cấu hình sinh ra quá nhiều thanh trước khi dựng, để giao diện không treo.
    /// </summary>
    public static void GuardPathBudget(int estimatedPaths)
    {
        if (estimatedPaths > MaxPaths)
            throw new ArgumentException(
                $"Cấu hình sinh {estimatedPaths:N0} thanh thép, vượt giới hạn {MaxPaths:N0}. " +
                "Hãy tăng bước đai hoặc thu hẹp phạm vi dầm.");
    }

    /// <summary>Cao độ tâm đai ở mặt trên và mặt dưới, đo xuống từ mặt trên dầm.</summary>
    private static (double Top, double Bottom) VerticalExtent(
        PureSpanFrame frame, BeamCoverMm cover, double diameterMm) =>
        (-(cover.TopMm + diameterMm / 2), -(frame.HeightMm - cover.BottomMm - diameterMm / 2));

    private static IReadOnlyList<GeometryPoint3D> Rectangle(
        PureSpanFrame frame, double stationMm, double left, double right, double top, double bottom) =>
    [
        frame.PointAtStation(stationMm, left, top),
        frame.PointAtStation(stationMm, right, top),
        frame.PointAtStation(stationMm, right, bottom),
        frame.PointAtStation(stationMm, left, bottom)
    ];
}
