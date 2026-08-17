using RevitAPP.Core.Models;

namespace RevitAPP.Core.Services;

/// <summary>Lớp bảo vệ bê tông (mm).</summary>
public readonly record struct BeamCoverMm(double TopMm, double BottomMm, double SideMm);

/// <summary>Hướng bẻ đầu thanh so với phương đứng.</summary>
public enum BarBendDirection
{
    None,
    Down,
    Up
}

/// <summary>
/// Hình học thép dọc trong tiết diện dầm: cao độ đặt thanh, vị trí ngang, và đường tim đã bẻ đầu.
/// Độc lập Revit để preview và builder cùng đọc một phép tính.
/// </summary>
public static class BeamRebarLongitudinalFactory
{
    /// <summary>
    /// Cao độ tâm thanh (âm, tính xuống từ mặt trên) và nửa bề rộng còn dùng được của tiết diện.
    /// Thép chủ nằm TRONG đai nên lùi thêm một đường kính đai so với mép bê tông.
    /// </summary>
    public static (double VerticalMm, double UsableHalfMm) Vertical(
        PureSpanFrame frame,
        BeamCoverMm cover,
        double barDiameterMm,
        double stirrupDiameterMm,
        bool atTop,
        double extraVerticalOffsetMm = 0)
    {
        var coverSide = cover.SideMm + stirrupDiameterMm;
        var coverTop = cover.TopMm + stirrupDiameterMm;
        var coverBottom = cover.BottomMm + stirrupDiameterMm;

        // Tâm thanh cách mặt cover nửa đường kính, cộng offset lớp 2. Hệ quy chiếu đi xuống từ mặt trên.
        var vertical = atTop
            ? -(coverTop + barDiameterMm / 2 + extraVerticalOffsetMm)
            : -(frame.HeightMm - coverBottom - barDiameterMm / 2 - extraVerticalOffsetMm);

        var usableHalf = Math.Max(0, frame.WidthMm / 2 - coverSide - barDiameterMm / 2);
        return (vertical, usableHalf);
    }

    /// <summary>Vị trí ngang thanh gốc: một cây đơn nằm giữa, nhiều cây bắt đầu từ mép trái.</summary>
    public static double FirstLateralMm(double usableHalfMm, int count) => count == 1 ? 0 : -usableHalfMm;

    /// <summary>
    /// Vị trí ngang của cả bó thanh, đã nhân bản theo cách Revit rải.
    /// Thép chủ và thép gia cường lớp 2 rải đều suốt bề rộng; thép gia cường lớp 1 đặt xen vào khe
    /// giữa các thanh chủ.
    /// </summary>
    public static IReadOnlyList<double> LateralOffsetsMm(
        int count,
        double usableHalfMm,
        string? positionInSection = null,
        int mainBarCount = 0,
        bool spreadAcrossFullWidth = true)
    {
        if (count <= 0) return [];

        if (!spreadAcrossFullWidth)
        {
            var gaps = GapOffsetsMm(positionInSection, mainBarCount, count, usableHalfMm);
            if (gaps.Count > 0) return gaps;
        }

        var first = FirstLateralMm(usableHalfMm, count);
        return RebarLayoutMath.FixedNumberOffsets(count, usableHalfMm * 2)
            .Select(offset => first + offset)
            .ToArray();
    }

    /// <summary>
    /// Vị trí ngang thép gia cường đặt theo KHE giữa các thanh chủ. <paramref name="positionInSection"/>
    /// là danh sách chỉ số khe (0 = khe giữa thanh chủ 1 và 2). Không đủ khe thì chia đều trong lòng,
    /// tránh hai biên vốn đã có thép chủ.
    /// </summary>
    public static IReadOnlyList<double> GapOffsetsMm(
        string? positionInSection, int mainBarCount, int addCount, double usableHalfMm)
    {
        if (addCount <= 0) return [];
        if (mainBarCount <= 2) return EvenInteriorOffsetsMm(addCount, usableHalfMm);

        var gaps = mainBarCount - 1;
        var parts = string.IsNullOrWhiteSpace(positionInSection)
            ? Enumerable.Range(0, addCount).Select(i => i.ToString()).ToArray()
            // Tách bằng mảng ký tự: quá tải nhận thẳng ký tự chỉ có từ .NET Core, còn add-in chạy cả net48.
            : positionInSection.Split([','], StringSplitOptions.RemoveEmptyEntries);

        var offsets = new List<double>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part.Trim(), out var gapIndex)) continue;
            if (gapIndex >= gaps && addCount > gaps)
                return EvenInteriorOffsetsMm(addCount, usableHalfMm);

            gapIndex = MathCompat.Clamp(gapIndex, 0, gaps - 1);
            var step = usableHalfMm * 2 / (mainBarCount - 1);
            var lateral = -usableHalfMm + (gapIndex + 0.5) * step;
            if (!offsets.Any(o => Math.Abs(o - lateral) < 1e-9))
                offsets.Add(lateral);
        }

        return offsets.Count == addCount ? offsets : EvenInteriorOffsetsMm(addCount, usableHalfMm);
    }

    /// <summary>Chia đều <paramref name="count"/> thanh trong lòng tiết diện, không chạm hai biên.</summary>
    public static IReadOnlyList<double> EvenInteriorOffsetsMm(int count, double usableHalfMm)
    {
        if (count <= 0) return [];

        var step = usableHalfMm * 2 / (count + 1);
        var offsets = new double[count];
        for (var i = 0; i < count; i++)
            offsets[i] = -usableHalfMm + (i + 1) * step;
        return offsets;
    }

    /// <summary>Co đoạn thép vào trong host theo lớp bảo vệ đầu dầm.</summary>
    public static (double StartT, double EndT) ClampSegmentInsideHost(
        PureSpanFrame frame, BeamCoverMm cover, double startT, double endT)
    {
        var insetT = Math.Min(cover.SideMm / frame.LengthMm, 0.05);
        if (startT <= 0) startT = insetT;
        if (endT >= 1) endT = 1 - insetT;
        return (startT, endT);
    }

    /// <summary>Giới hạn chiều dài bẻ đầu để móc không xuyên khỏi mặt đối diện.</summary>
    public static double MaxBendLengthMm(PureSpanFrame frame, BeamCoverMm cover, double barDiameterMm) =>
        Math.Max(0, frame.HeightMm - cover.TopMm - cover.BottomMm - barDiameterMm);

    /// <summary>
    /// Đường tim một thanh dọc, kể cả đoạn bẻ ở hai đầu. Bẻ ở cả hai đầu bằng 0 cho thanh thẳng 2 điểm.
    /// Hướng bẻ truyền vào tường minh vì thép chủ bẻ ngược phía so với thép gia cường.
    /// </summary>
    public static IReadOnlyList<GeometryPoint3D> BuildPolyline(
        PureSpanFrame frame,
        double startT,
        double endT,
        double lateralMm,
        double verticalMm,
        BarBendDirection bendDirection,
        double startBendMm = 0,
        double endBendMm = 0,
        double maxBendMm = double.MaxValue)
    {
        var start = frame.PointAt(startT, lateralMm, verticalMm);
        var end = frame.PointAt(endT, lateralMm, verticalMm);

        if (bendDirection == BarBendDirection.None)
            return [start, end];

        var sign = bendDirection == BarBendDirection.Down ? -1d : 1d;
        var startBend = Math.Min(Math.Max(0, startBendMm), maxBendMm);
        var endBend = Math.Min(Math.Max(0, endBendMm), maxBendMm);
        if (startBend <= 1e-6 && endBend <= 1e-6)
            return [start, end];

        var points = new List<GeometryPoint3D>(4);
        if (startBend > 1e-6)
            points.Add(new GeometryPoint3D(start.Xmm, start.Ymm, start.Zmm + sign * startBend));
        points.Add(start);
        points.Add(end);
        if (endBend > 1e-6)
            points.Add(new GeometryPoint3D(end.Xmm, end.Ymm, end.Zmm + sign * endBend));
        return points;
    }
}
