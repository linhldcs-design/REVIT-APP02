using Autodesk.Revit.DB;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services;

/// <summary>
///     Đọc danh sách nhịp (SpanInfo) từ các dầm đã chọn: đọc geometry, tự dò cột chia nhịp. Dùng chung
///     cho command (pick ngay khi bấm Ribbon) và handler (pick trong dialog).
/// </summary>
public static class BeamSpanReader
{
    public static IReadOnlyList<SpanInfo> ReadSpans(Document document, IReadOnlyList<FamilyInstance> beams,
        IReadOnlyList<Point3>? extraSupportPoints = null)
    {
        if (beams.Count == 0) return [];

        var reader = new BeamGeometryReader();
        var segments = new List<BeamSegment>();
        foreach (var beam in beams)
            if (reader.TryRead(beam, out var seg, out _))
                segments.Add(seg);

        if (segments.Count == 0) return [];

        var detector = new ColumnDetector(document);
        var innerHits = detector.FindInternalSupports(segments);
        var allHits = detector.FindAllColumnHits(segments)
            .Concat(detector.FindCrossBeamHits(segments))
            .ToList();
        // Gối tự dò + gối người dùng thêm thủ công (mục 9) → chia nhịp gồm cả 2.
        var supportPoints = innerHits.Select(h => h.Location)
            .Concat(extraSupportPoints ?? [])
            .ToList();

        var run = SpanModelBuilder.Build(segments, supportPoints);
        run = ColumnDetector.EnrichSupportsWithColumnWidth(run, allHits);

        return run.Spans
            .Select(s =>
            {
                var leftHalf = s.Index < run.Supports.Count ? run.Supports[s.Index].HalfWidthFeet * 304.8 : 200.0;
                var rightHalf = s.Index + 1 < run.Supports.Count ? run.Supports[s.Index + 1].HalfWidthFeet * 304.8 : 200.0;

                // Hình học thật của nhịp đi kèm luôn, để bản xem trước dựng đúng tiết diện, cao độ và
                // vị trí của dầm thay vì phải đoán.
                var host = FindSegmentFor(s, segments);
                return new SpanInfo(s.Index, s.LengthFeet * 304.8, leftHalf, rightHalf)
                {
                    SectionWidthMm = host?.Section.WidthMm ?? 0,
                    SectionHeightMm = host is null ? 0 : (host.TopElevationFeet - host.BottomElevationFeet) * 304.8,
                    TopElevationMm = (host?.TopElevationFeet ?? 0) * 304.8,
                    StartXMm = s.Start.X * 304.8,
                    StartYMm = s.Start.Y * 304.8,
                    EndXMm = s.End.X * 304.8,
                    EndYMm = s.End.Y * 304.8,
                    LateralOffsetMm = (host?.LateralOffsetFeet ?? 0) * 304.8
                };
            })
            .ToList();
    }

    /// <summary>Dầm vật lý chứa nhịp này, tìm bằng cách chiếu điểm giữa nhịp lên từng trục dầm.</summary>
    private static BeamSegment? FindSegmentFor(Span span, IReadOnlyList<BeamSegment> segments)
    {
        var mid = new Point3(
            (span.Start.X + span.End.X) / 2,
            (span.Start.Y + span.End.Y) / 2,
            (span.Start.Z + span.End.Z) / 2);

        foreach (var segment in segments)
        {
            var ax = segment.End.X - segment.Start.X;
            var ay = segment.End.Y - segment.Start.Y;
            var az = segment.End.Z - segment.Start.Z;
            var lengthSquared = ax * ax + ay * ay + az * az;
            if (lengthSquared < 1e-9) continue;

            var t = ((mid.X - segment.Start.X) * ax +
                     (mid.Y - segment.Start.Y) * ay +
                     (mid.Z - segment.Start.Z) * az) / lengthSquared;
            if (t is >= -0.001 and <= 1.001) return segment;
        }

        return segments.Count > 0 ? segments[0] : null;
    }
}
