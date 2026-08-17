using RevitAPP.Core.Models;

namespace RevitAPP.Core.Services;

/// <summary>Một lớp thép dọc cần dựng trên nhịp.</summary>
public sealed record BeamLongitudinalRequest(
    BeamRebarPathKind Kind,
    int Count,
    double DiameterMm,
    double StartT,
    double EndT,
    int Layer = 1,
    double StartBendMm = 0,
    double EndBendMm = 0,
    BarBendDirection BendDirection = BarBendDirection.None,
    string? PositionInSection = null,
    int MainBarCount = 0,
    bool SpreadAcrossFullWidth = true);

/// <summary>Cấu hình đai của một nhịp, đã quy về mm.</summary>
public sealed record BeamStirrupRequest(
    double DiameterMm,
    double SpacingEndMm,
    double SpacingMidMm,
    bool TwoEnds,
    double EndZoneLengthMm = 0,
    double EndZoneStartMm = 0,
    double EndZoneEndMm = 0,
    IReadOnlyList<(double StationMm, double HalfWidthMm)>? SecondaryBeams = null,
    /// <summary>Đai phụ ôm các thanh chủ giữa, rải cùng vùng và cùng bước với đai chính.</summary>
    IReadOnlyList<BeamAdditionalStirrupRequest>? AdditionalStirrups = null,
    /// <summary>Số thanh chủ và đường kính, để đặt đai phụ ôm đúng vị trí thanh.</summary>
    int MainBarCount = 0,
    double MainBarDiameterMm = 0,
    /// <summary>
    /// Đặt false khi chỉ cần đường kính đai để đặt thanh chủ lùi vào trong đai, mà không sinh đai —
    /// dùng cho khung chạy suốt tuyến vì đai đã thuộc về từng nhịp.
    /// </summary>
    bool GenerateStirrups = true);

/// <summary>Một loại đai phụ: khung kín ôm dải thanh chủ, hoặc cây móc C quặp một cột thanh.</summary>
public sealed record BeamAdditionalStirrupRequest(
    double DiameterMm,
    bool IsClosed,
    int StartBar,
    int EndBar);

/// <summary>Toàn bộ yêu cầu dựng thép cho một nhịp.</summary>
public sealed record BeamSpanRequest(
    PureSpanFrame Frame,
    BeamCoverMm Cover,
    IReadOnlyList<BeamLongitudinalRequest> Longitudinal,
    BeamStirrupRequest? Stirrup = null);

/// <summary>
/// Ghép các phép tính hình học thành bản mô tả thép hoàn chỉnh của một tuyến dầm.
/// Đây là điểm vào duy nhất mà bản xem trước sử dụng.
/// </summary>
public static class BeamRebarPlanBuilder
{
    /// <summary>
    /// Ghép mọi yêu cầu thành bản mô tả hoàn chỉnh.
    /// <paramref name="contextSpans"/> là các nhịp tạo nên khối bê tông; khi bỏ trống thì lấy theo
    /// <paramref name="spans"/>. Tách ra vì thép chủ được dựng trên một khung chạy suốt tuyến, và
    /// khung đó không phải một nhịp bê tông riêng.
    /// </summary>
    public static BeamRebarGeometryPlan Build(
        IReadOnlyList<BeamSpanRequest> spans,
        IReadOnlyList<BeamSupportMarker>? supports = null,
        IReadOnlyList<BeamSupportMarker>? crossBeams = null,
        IReadOnlyList<PureSpanFrame>? contextSpans = null)
    {
        if (spans.Count == 0) return BeamRebarGeometryPlan.Empty;

        var paths = new List<BeamRebarPath>();
        for (var spanIndex = 0; spanIndex < spans.Count; spanIndex++)
        {
            var span = spans[spanIndex];
            paths.AddRange(LongitudinalPaths(span, spanIndex));
            if (span.Stirrup is { GenerateStirrups: true })
                paths.AddRange(StirrupPaths(span, span.Stirrup, spanIndex));
        }

        BeamRebarStirrupFactory.GuardPathBudget(paths.Count);

        var frames = contextSpans ?? spans.Select(s => s.Frame).ToList();
        var context = BeamRebarContextFactory.Build(frames, supports, crossBeams);

        var supportStations = supports is { Count: > 0 }
            ? supports.Select(s => s.StationMm).ToList()
            : CumulativeStations(frames);

        return new BeamRebarGeometryPlan(context, paths, supportStations, frames.Sum(f => f.LengthMm));
    }

    /// <summary>Khoảng cách dồn của từng ranh giới nhịp, tính từ đầu tuyến.</summary>
    private static IReadOnlyList<double> CumulativeStations(IReadOnlyList<PureSpanFrame> frames)
    {
        var stations = new List<double> { 0 };
        var offset = 0d;
        foreach (var frame in frames)
        {
            offset += frame.LengthMm;
            stations.Add(offset);
        }
        return stations;
    }

    private static IEnumerable<BeamRebarPath> LongitudinalPaths(BeamSpanRequest span, int spanIndex)
    {
        foreach (var request in span.Longitudinal)
        {
            if (request.Count <= 0) continue;

            var atTop = request.Kind is BeamRebarPathKind.MainTop or BeamRebarPathKind.AdditionalTop;
            var layerOffset = request.Layer >= 2 ? Layer2OffsetMm : 0;
            var stirrupDiameter = span.Stirrup?.DiameterMm ?? 0;

            var (vertical, usableHalf) = BeamRebarLongitudinalFactory.Vertical(
                span.Frame, span.Cover, request.DiameterMm, stirrupDiameter, atTop, layerOffset);

            var (startT, endT) = BeamRebarLongitudinalFactory.ClampSegmentInsideHost(
                span.Frame, span.Cover, request.StartT, request.EndT);
            if (endT <= startT) continue;

            var maxBend = BeamRebarLongitudinalFactory.MaxBendLengthMm(
                span.Frame, span.Cover, request.DiameterMm);

            var laterals = BeamRebarLongitudinalFactory.LateralOffsetsMm(
                request.Count, usableHalf, request.PositionInSection,
                request.MainBarCount, request.SpreadAcrossFullWidth);

            foreach (var lateral in laterals)
            {
                var points = BeamRebarLongitudinalFactory.BuildPolyline(
                    span.Frame, startT, endT, lateral, vertical,
                    request.BendDirection, request.StartBendMm, request.EndBendMm, maxBend);

                yield return new BeamRebarPath(
                    spanIndex, request.Kind, request.DiameterMm, points, request.Layer);
            }
        }
    }

    private static IEnumerable<BeamRebarPath> StirrupPaths(
        BeamSpanRequest span, BeamStirrupRequest stirrup, int spanIndex)
    {
        var length = span.Frame.LengthMm;
        var zones = BeamRebarStirrupFactory.Zones(
            length, stirrup.SpacingEndMm, stirrup.SpacingMidMm, stirrup.TwoEnds,
            stirrup.EndZoneLengthMm, stirrup.EndZoneStartMm, stirrup.EndZoneEndMm);

        var blocked = BeamRebarStirrupFactory.SecondaryRanges(
            stirrup.SecondaryBeams ?? [], length, stirrup.DiameterMm);

        foreach (var (station, zone) in BeamRebarStirrupFactory.MainStirrupStations(zones, blocked))
        {
            yield return new BeamRebarPath(
                spanIndex, BeamRebarPathKind.Stirrup, stirrup.DiameterMm,
                BeamRebarStirrupFactory.ClosedProfile(span.Frame, span.Cover, stirrup.DiameterMm, station),
                Zone: zone, IsClosedLoop: true);
        }

        foreach (var range in blocked)
        {
            foreach (var station in BeamRebarStirrupFactory.SecondaryClusterStations(range))
            {
                yield return new BeamRebarPath(
                    spanIndex, BeamRebarPathKind.StirrupSecondary, stirrup.DiameterMm,
                    BeamRebarStirrupFactory.ClosedProfile(span.Frame, span.Cover, stirrup.DiameterMm, station),
                    Zone: "Secondary", IsClosedLoop: true);
            }
        }

        foreach (var path in AdditionalStirrupPaths(span, stirrup, zones, blocked, spanIndex))
            yield return path;
    }

    /// <summary>
    /// Đai phụ ôm các thanh chủ giữa, rải cùng vùng và cùng bước với đai chính.
    /// Dưới ba thanh chủ thì không có thanh giữa nào để ôm nên không tạo.
    /// </summary>
    private static IEnumerable<BeamRebarPath> AdditionalStirrupPaths(
        BeamSpanRequest span, BeamStirrupRequest stirrup,
        IReadOnlyList<BeamStirrupZone> zones,
        IReadOnlyList<BeamSecondaryStirrupRange> blocked,
        int spanIndex)
    {
        var additions = stirrup.AdditionalStirrups;
        if (additions is not { Count: > 0 } || stirrup.MainBarCount < 3) yield break;

        var mainBarDiameter = stirrup.MainBarDiameterMm > 0 ? stirrup.MainBarDiameterMm : stirrup.DiameterMm;
        var usableHalf = BeamRebarStirrupFactory.MainBarUsableHalfMm(
            span.Frame, span.Cover, stirrup.DiameterMm, mainBarDiameter);

        foreach (var addition in additions)
        {
            var startIndex = MathCompat.Clamp(addition.StartBar - 1, 0, stirrup.MainBarCount - 1);
            var endIndex = addition.IsClosed
                ? MathCompat.Clamp(addition.EndBar - 1, startIndex, stirrup.MainBarCount - 1)
                : startIndex;

            var left = BeamRebarStirrupFactory.MainBarLateralMm(startIndex, stirrup.MainBarCount, usableHalf);
            var right = BeamRebarStirrupFactory.MainBarLateralMm(endIndex, stirrup.MainBarCount, usableHalf);
            if (right - left < 1e-6) right = left + mainBarDiameter;

            // Khung kín phải bao NGOÀI thanh chủ, không đè lên tim thanh.
            if (addition.IsClosed)
            {
                var grow = mainBarDiameter / 2 + addition.DiameterMm / 2;
                left -= grow;
                right += grow;
            }

            var kind = addition.IsClosed
                ? BeamRebarPathKind.AdditionalStirrupClosed
                : BeamRebarPathKind.AdditionalStirrupCHook;

            foreach (var (station, zone) in BeamRebarStirrupFactory.MainStirrupStations(zones, blocked))
            {
                var points = addition.IsClosed
                    ? BeamRebarStirrupFactory.NarrowProfile(
                        span.Frame, span.Cover, addition.DiameterMm, station, left, right)
                    : BeamRebarStirrupFactory.CHookProfile(
                        span.Frame, span.Cover, addition.DiameterMm, station, (left + right) / 2);

                yield return new BeamRebarPath(
                    spanIndex, kind, addition.DiameterMm, points,
                    Zone: zone, IsClosedLoop: addition.IsClosed);
            }
        }
    }

    /// <summary>Khoảng cách giữa hai lớp thép gia cường (mm).</summary>
    private const double Layer2OffsetMm = 30;
}
