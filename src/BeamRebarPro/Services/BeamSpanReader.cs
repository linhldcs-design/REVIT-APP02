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
                return new SpanInfo(s.Index, s.LengthFeet * 304.8, leftHalf, rightHalf);
            })
            .ToList();
    }
}
