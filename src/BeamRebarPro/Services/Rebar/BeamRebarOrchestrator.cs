using Autodesk.Revit.DB;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services.Rebar;

/// <summary>
///     Coordinates rebar creation for selected beam runs.
///     Main bars are created once along each physical beam host, so a long beam passing through several
///     internal supports keeps continuous top/bottom main bars. Additional bars, stirrups, and anti-bulge
///     bars are still created per calculated span.
/// </summary>
public sealed class BeamRebarOrchestrator
{
    private const double Layer2OffsetFeet = 30.0 / 304.8;
    private const double SupportClearanceFeet = 200.0 / 304.8;
    private const double SupportToleranceFeet = 60.0 / 304.8;
    private const double ColumnFaceMatchToleranceFeet = 100.0 / 304.8;

    public RebarCreationResult Create(Document document, IReadOnlyList<FamilyInstance> beams, QuickSettingModel model)
    {
        using var transaction = new Transaction(document, "Tao thep dam (TCVN)");
        transaction.Start();
        var result = CreateInTransaction(document, beams, model);
        transaction.Commit();
        return result;
    }

    /// <summary>
    ///     Như <see cref="Create" /> nhưng KHÔNG tự mở Transaction — dùng khi caller đã ở trong một
    ///     Transaction đang mở (vd revit-mcp send_code_to_revit đã mở sẵn transaction).
    /// </summary>
    public RebarCreationResult CreateInTransaction(
        Document document, IReadOnlyList<FamilyInstance> beams, QuickSettingModel model)
    {
        var warnings = new List<string>();
        var families = new RebarFamilyValidator(document);

        var familyErrors = families.Validate(model);
        if (familyErrors.Count > 0)
            return new RebarCreationResult(0, 0, 0, familyErrors);

        var reader = new BeamGeometryReader();
        var segments = new List<BeamSegment>();
        foreach (var beam in beams)
        {
            if (reader.TryRead(beam, out var segment, out var error))
                segments.Add(segment);
            else
                warnings.Add(error);
        }

        if (segments.Count == 0)
            return new RebarCreationResult(0, 0, 0, warnings.Count > 0 ? warnings : ["Khong doc duoc dam nao."]);

        // Cột GIỮA → chia nhịp. Cột TẤT CẢ (gồm 2 đầu mút) → gán nửa bề rộng cho mọi gối để tính thép
        // gia cường từ MÉP CỘT (kể cả nhịp biên).
        var detector = new ColumnDetector(document);
        var innerHits = detector.FindInternalSupports(segments);
        // Gối có thể là CỘT hoặc DẦM GIAO. Gộp cả 2 để gán bề rộng THẬT → đai/thép lùi đúng mép gối.
        var allHits = detector.FindAllColumnHits(segments)
            .Concat(detector.FindCrossBeamHits(segments))
            .ToList();
        var manualSupportHits = model.InternalSupports
            .Select(s => new ColumnDetector.ColumnHit(s.Location, s.HalfWidthFeet))
            .ToList();
        var supportPoints = innerHits.Select(h => h.Location)
            .Concat(model.InternalSupportPoints)
            .Concat(model.InternalSupports.Select(s => s.Location))
            .ToList();

        var run = SpanModelBuilder.Build(segments, supportPoints);
        // Bề rộng gối lấy THẬT từ cột/dầm giao (allHits). Không dùng default ước lượng → gối đầu tự do
        // (không cột/dầm giao) sẽ không bị lùi sai.
        run = ColumnDetector.EnrichSupportsWithColumnWidth(run, allHits.Concat(manualSupportHits).ToList());
        warnings.AddRange(run.Warnings);

        if (run.Spans.Count == 1 && supportPoints.Count == 0)
            warnings.Add("Không dò được cột nào cắt qua dầm — dầm được xem là 1 nhịp. " +
                         "Nếu dầm chạy qua nhiều cột, hãy chọn cột ở mục 9.");

        // Truyền đường kính đai để thép chủ + gia cường nằm BÊN TRONG đai (không đè lên đường đai).
        var longCreator = new LongitudinalBarCreator(document, families, model.Cover, model.Stirrup.Diameter.Millimeters);
        var additionalCreator = new AdditionalBarCreator(longCreator);
        var additionalTieCreator = new AdditionalTieCreator(document, families, model.Cover);
        var stirrupCreator = new StirrupCreator(document, families, model.Cover);
        var antiBulgeCreator = new AntiBulgeCreator(document, families, model.Cover);

        var longitudinalCount = 0;
        var stirrupCount = 0;
        var antiBulgeCount = 0;

        var fullBeamFrames = new List<(FamilyInstance Host, BeamSegment Segment, SpanFrame Frame)>();

        // Main bars must run through the whole physical beam host, not stop at internal supports.
        for (var i = 0; i < segments.Count; i++)
        {
            var host = beams[Math.Min(i, beams.Count - 1)];
            var segment = segments[i];
            var fullBeamSpan = new Span(i, segment.Start, segment.End, segment.Section);

            try
            {
                var fullBeamFrame = new SpanFrame(fullBeamSpan, segment.TopElevationFeet, segment.BottomElevationFeet, segment.LateralOffsetFeet);
                fullBeamFrames.Add((host, segment, fullBeamFrame));
            }
            catch (InvalidOperationException ex)
            {
                warnings.Add(ex.Message);
            }
        }

        longitudinalCount += CreateMainBars(run, fullBeamFrames, model, longCreator, warnings);

        if (model.TopAdditionalItems.Count > 0)
        {
            foreach (var config in model.TopAdditionalItems)
            {
                var layerOffset = config.Layer == 2 ? Layer2OffsetFeet : 0;
                longitudinalCount += CreateTopAdditionalAtSupports(
                    run, fullBeamFrames, config, layerOffset, model.MainTop.Count, longCreator, warnings);
            }
        }
        else
        {
            longitudinalCount += CreateTopAdditionalAtSupports(
                run, fullBeamFrames, model.TopAdditional, 0, model.MainTop.Count, longCreator, warnings);
            longitudinalCount += CreateTopAdditionalAtSupports(
                run, fullBeamFrames, model.TopAdditionalLayer2, Layer2OffsetFeet, model.MainTop.Count, longCreator, warnings);
        }

        // Span-based bars still follow the calculated spans between supports.
        for (var i = 0; i < run.Spans.Count; i++)
        {
            var span = run.Spans[i];
            var (host, segment) = FindHostForSpan(span, beams, segments);

            SpanFrame frame;
            try
            {
                frame = new SpanFrame(span, segment.TopElevationFeet, segment.BottomElevationFeet, segment.LateralOffsetFeet);
            }
            catch (InvalidOperationException ex)
            {
                warnings.Add(ex.Message);
                continue;
            }

            var resolved = QuickSettingFactory.ResolveForSpan(model, i);

            // Mép cột trái/phải của nhịp (feet, từ đầu span) để tính thép bot trên L THÔNG THỦY.
            var leftHalf = i < run.Supports.Count ? run.Supports[i].HalfWidthFeet : 0;
            var rightHalf = i + 1 < run.Supports.Count ? run.Supports[i + 1].HalfWidthFeet : 0;

            if (model.BottomAdditionalItems.Count > 0)
            {
                foreach (var config in model.BottomAdditionalItems.Where(c => c.StartPointIndex == i && c.EndPointIndex == i + 1))
                {
                    var layerOffset = config.Layer == 2 ? Layer2OffsetFeet : 0;
                    longitudinalCount += additionalCreator.Create(host, frame, config, atTop: false, layerOffset, leftHalf, rightHalf, resolved.MainBottom.Count, warnings);
                }
            }
            else
            {
                longitudinalCount += CreateBottomAdditionalBars(host, frame, resolved, additionalCreator, leftHalf, rightHalf, resolved.MainBottom.Count, warnings);
            }

            if (TryCreateStirrupFrame(span, segment, run.Supports, resolved.Stirrup, out var stirrupFrame))
            {
                // Chiếu các điểm dầm phụ lên trục nhịp (station feet từ đầu nhịp) → đai tăng cường tại đó.
                var secondaryStations = ProjectSecondaryStations(model, stirrupFrame)
                    .Concat(FindIntersectingBeamStations(document, beams, stirrupFrame, segment))
                    .GroupBy(s => Math.Round(s.StationFeet / SupportToleranceFeet))
                    .Select(g => g.OrderByDescending(s => s.HalfWidthFeet).First())
                    .ToList();
                // mainBarCount = số thanh chủ top (để đặt đai phụ ôm thanh giữa).
                stirrupCount += stirrupCreator.Create(host, stirrupFrame, resolved.Stirrup, warnings, secondaryStations, resolved.MainTop.Count, resolved.MainTop.Diameter.Millimeters);
            }

            antiBulgeCount += antiBulgeCreator.Create(host, frame, resolved.AntiBulge, leftHalf, rightHalf, warnings);

            // Đai C giữ thép gia cường LỚP 2 (≥3 cây): top + bottom. Lùi khỏi cột (leftHalf/rightHalf).
            longitudinalCount += additionalTieCreator.Create(host, frame, model.TopAdditionalLayer2, atTop: true, Layer2OffsetFeet, leftHalf, rightHalf, warnings);
            longitudinalCount += additionalTieCreator.Create(host, frame, model.BottomAdditionalLayer2, atTop: false, Layer2OffsetFeet, leftHalf, rightHalf, warnings);
        }

        return new RebarCreationResult(longitudinalCount, stirrupCount, antiBulgeCount, warnings);
    }

    /// <summary>Chiếu các điểm dầm phụ lên trục nhịp: trả station (feet từ đầu nhịp) cho điểm nằm trong nhịp.</summary>
    private static IReadOnlyList<SecondaryStirrupStation> ProjectSecondaryStations(QuickSettingModel model, SpanFrame frame)
    {
        if (model.SecondaryBeams.Count == 0 && model.SecondaryBeamPoints.Count == 0) return [];

        var stations = new List<SecondaryStirrupStation>();
        foreach (var beam in model.SecondaryBeams)
        {
            var p = beam.Location;
            var v = new XYZ(p.X, p.Y, p.Z) - frame.StartFeet;
            var s = v.DotProduct(frame.Along);
            if (s > 0 && s < frame.LengthFeet)
                stations.Add(new SecondaryStirrupStation(s, beam.HalfWidthFeet));
        }

        if (stations.Count > 0) return stations;

        foreach (var p in model.SecondaryBeamPoints)
        {
            var v = new XYZ(p.X, p.Y, p.Z) - frame.StartFeet;
            var s = v.DotProduct(frame.Along);
            if (s > 0 && s < frame.LengthFeet)
                stations.Add(new SecondaryStirrupStation(s, 0));
        }
        return stations;
    }

    private static IReadOnlyList<SecondaryStirrupStation> FindIntersectingBeamStations(
        Document document,
        IReadOnlyList<FamilyInstance> selectedMainBeams,
        SpanFrame frame,
        BeamSegment mainSegment)
    {
        var selectedIds = selectedMainBeams.Select(b => b.Id.ToValue()).ToHashSet();
        var mainStart = frame.StartFeet;
        var mainEnd = frame.EndFeet;
        var mainAxis = frame.Along;
        var stations = new List<SecondaryStirrupStation>();

        // Dầm giao nằm SÁT 2 đầu nhịp đã là GỐI (đai đã lùi khỏi nó qua stirrupFrame) — KHÔNG tạo thêm
        // đai tăng cường/vùng chặn cho nó, tránh xung đột 2 cơ chế (gối vs dầm phụ) làm hở đai ở mép gối.
        var edgeMarginFeet = mainSegment.TopElevationFeet - mainSegment.BottomElevationFeet; // = chiều cao dầm.

        var beams = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>();

        foreach (var beam in beams)
        {
            if (selectedIds.Contains(beam.Id.ToValue())) continue;
            if (beam.Location is not LocationCurve { Curve: Line secondaryLine }) continue;
            if (!BeamTouchesElevation(beam.get_BoundingBox(null), mainSegment)) continue;
            if (!TryIntersectLines2D(mainStart, mainEnd, secondaryLine, out var intersection)) continue;

            var station = (intersection - mainStart).DotProduct(mainAxis);
            if (station <= edgeMarginFeet || station >= frame.LengthFeet - edgeMarginFeet)
                continue;

            var halfWidth = TryGetProjectionHalfWidthOnAxis(beam, mainAxis, out var projectionMid, out var projectionHalf)
                ? projectionHalf
                : HalfWidthAlong(beam.get_BoundingBox(null), mainAxis);

            if (halfWidth <= 1e-6) continue;

            // Use the actual geometry midpoint along the main beam axis when the beam location line is justified
            // to an edge instead of the physical center.
            if (projectionMid != 0)
                station += projectionMid - intersection.DotProduct(mainAxis);

            if (station <= edgeMarginFeet || station >= frame.LengthFeet - edgeMarginFeet)
                continue;

            stations.Add(new SecondaryStirrupStation(station, halfWidth));
        }

        return stations;
    }

    private static bool BeamTouchesElevation(BoundingBoxXYZ? bbox, BeamSegment mainSegment)
    {
        if (bbox is null) return true;
        const double tolerance = 100.0 / 304.8;
        return bbox.Max.Z >= mainSegment.BottomElevationFeet - tolerance
               && bbox.Min.Z <= mainSegment.TopElevationFeet + tolerance;
    }

    private static bool TryIntersectLines2D(XYZ mainStart, XYZ mainEnd, Line secondaryLine, out XYZ point)
    {
        point = XYZ.Zero;
        var p = mainStart;
        var r = mainEnd - mainStart;
        var q = secondaryLine.GetEndPoint(0);
        var s = secondaryLine.GetEndPoint(1) - q;
        var denominator = Cross2D(r, s);
        if (Math.Abs(denominator) < 1e-9) return false;

        var t = Cross2D(q - p, s) / denominator;
        var u = Cross2D(q - p, r) / denominator;
        if (t is < -0.02 or > 1.02 || u is < -0.02 or > 1.02) return false;

        point = p + r * t;
        return true;
    }

    private static double Cross2D(XYZ a, XYZ b) => a.X * b.Y - a.Y * b.X;

    private static bool TryGetProjectionHalfWidthOnAxis(
        Element element,
        XYZ mainAxis,
        out double midpointProjection,
        out double halfWidthFeet)
    {
        midpointProjection = 0;
        halfWidthFeet = 0;

        var points = new List<XYZ>();
        var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        if (geometry is not null)
            CollectGeometryPoints(geometry, points);

        if (points.Count == 0)
            CollectBoundingBoxPoints(element.get_BoundingBox(null), points);

        if (points.Count == 0) return false;

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var point in points)
        {
            var projection = point.DotProduct(mainAxis);
            min = Math.Min(min, projection);
            max = Math.Max(max, projection);
        }

        if (max <= min) return false;

        midpointProjection = (min + max) / 2.0;
        halfWidthFeet = (max - min) / 2.0;
        return halfWidthFeet > 1e-6;
    }

    private static void CollectGeometryPoints(GeometryElement geometry, List<XYZ> points)
    {
        foreach (var obj in geometry)
        {
            switch (obj)
            {
                case Solid solid when solid.Volume > 1e-9:
                    foreach (Edge edge in solid.Edges)
                    {
                        foreach (var point in edge.Tessellate())
                            points.Add(point);
                    }
                    break;
                case GeometryInstance instance:
                    var transform = instance.Transform;
                    var instancePoints = new List<XYZ>();
                    CollectGeometryPoints(instance.GetInstanceGeometry(), instancePoints);
                    points.AddRange(instancePoints.Select(transform.OfPoint));
                    break;
            }
        }
    }

    private static void CollectBoundingBoxPoints(BoundingBoxXYZ? bbox, List<XYZ> points)
    {
        if (bbox is null) return;

        for (var ix = 0; ix <= 1; ix++)
        for (var iy = 0; iy <= 1; iy++)
        for (var iz = 0; iz <= 1; iz++)
        {
            points.Add(new XYZ(
                ix == 0 ? bbox.Min.X : bbox.Max.X,
                iy == 0 ? bbox.Min.Y : bbox.Max.Y,
                iz == 0 ? bbox.Min.Z : bbox.Max.Z));
        }
    }

    private static double HalfWidthAlong(BoundingBoxXYZ? bbox, XYZ axisXy)
    {
        if (bbox is null) return 0;
        var size = bbox.Max - bbox.Min;
        var projected = Math.Abs(size.X * axisXy.X) + Math.Abs(size.Y * axisXy.Y);
        return projected / 2;
    }

    private static int CreateMainBars(BeamRun run,
        IReadOnlyList<(FamilyInstance Host, BeamSegment Segment, SpanFrame Frame)> fullBeamFrames,
        QuickSettingModel model,
        LongitudinalBarCreator longCreator, List<string> warnings)
    {
        var count = 0;
        foreach (var (host, segment, frame) in fullBeamFrames)
        {
            count += CreateMainBarRange(host, segment, frame, run, model.MainTop, atTop: true, longCreator, warnings);
            count += CreateMainBarRange(host, segment, frame, run, model.MainBottom, atTop: false, longCreator, warnings);
        }
        return count;
    }

    private static int CreateMainBarRange(Element host, BeamSegment segment, SpanFrame frame, BeamRun run,
        MainBarConfig config, bool atTop, LongitudinalBarCreator longCreator, List<string> warnings)
    {
        if (run.Supports.Count == 0)
            return longCreator.Create(host, frame, config, atTop, 0, warnings);

        var startIndex = Math.Clamp(config.StartPointIndex, 0, run.Supports.Count - 1);
        var endIndex = config.EndPointIndex == int.MaxValue
            ? run.Supports.Count - 1
            : Math.Clamp(config.EndPointIndex, 0, run.Supports.Count - 1);
        if (endIndex <= startIndex)
            return 0;

        var startDistance = ProjectDistanceOnSegmentUnclamped(run.Supports[startIndex].Location, segment);
        var endDistance = ProjectDistanceOnSegmentUnclamped(run.Supports[endIndex].Location, segment);
        var rangeStart = Math.Max(0, startDistance);
        var rangeEnd = Math.Min(frame.LengthFeet, endDistance);
        if (rangeEnd - rangeStart <= SupportToleranceFeet)
            return 0;

        return longCreator.CreateRange(host, frame, config, atTop, 0,
            rangeStart / frame.LengthFeet, rangeEnd / frame.LengthFeet, warnings);
    }

    private static int CreateBottomAdditionalBars(Element host, SpanFrame frame, QuickSettingModel model,
        AdditionalBarCreator additionalCreator, double leftHalfFeet, double rightHalfFeet, int mainCount, List<string> warnings)
    {
        var count = 0;
        count += additionalCreator.Create(host, frame, model.BottomAdditional, atTop: false, 0, leftHalfFeet, rightHalfFeet, mainCount, warnings);
        count += additionalCreator.Create(host, frame, model.BottomAdditionalLayer2, atTop: false, Layer2OffsetFeet, leftHalfFeet, rightHalfFeet, mainCount, warnings);
        return count;
    }

    private static int CreateTopAdditionalAtSupports(
        BeamRun run,
        IReadOnlyList<(FamilyInstance Host, BeamSegment Segment, SpanFrame Frame)> fullBeamFrames,
        AdditionalBarConfig config,
        double layerOffsetFeet,
        int mainCount,
        LongitudinalBarCreator longCreator,
        List<string> warnings)
    {
        if (!config.Enabled || config.Count <= 0 || run.Supports.Count == 0) return 0;

        var startIndex = Math.Clamp(config.StartPointIndex, 0, run.Supports.Count - 1);
        var endIndex = config.EndPointIndex == int.MaxValue
            ? run.Supports.Count - 1
            : Math.Clamp(config.EndPointIndex, 0, run.Supports.Count - 1);
        if (endIndex < startIndex) return 0;

        var fixedEachSideFeet = config.LengthMm > 0 ? config.LengthMm / 304.8 : 0;
        var created = 0;

        for (var supportIndex = startIndex; supportIndex <= endIndex; supportIndex++)
        {
            var support = run.Supports[supportIndex];
            var leftSpan = supportIndex > 0 ? run.Spans[supportIndex - 1] : null;
            var rightSpan = supportIndex < run.Spans.Count ? run.Spans[supportIndex] : null;
            var leftExtend = ResolveAdditionalExtendFeet(config.LeftLengthMm, config.LeftRatio, fixedEachSideFeet, leftSpan?.LengthFeet ?? 0);
            var rightExtend = ResolveAdditionalExtendFeet(config.RightLengthMm, config.RightRatio, fixedEachSideFeet, rightSpan?.LengthFeet ?? 0);

            foreach (var (host, segment, frame) in fullBeamFrames)
            {
                if (!TryProjectDistanceOnSegment(support.Location, segment, out var supportDistance))
                    continue;

                var startDistance = supportDistance - support.HalfWidthFeet - (leftSpan is null ? 0 : leftExtend);
                var endDistance = supportDistance + support.HalfWidthFeet + (rightSpan is null ? 0 : rightExtend);

                startDistance = Math.Max(0, startDistance);
                endDistance = Math.Min(frame.LengthFeet, endDistance);

                if (endDistance - startDistance <= SupportToleranceFeet)
                    continue;

                var leftHookFeet = config.DLeftMm / 304.8;
                var rightHookFeet = config.DRightMm / 304.8;
                var bendStartDown = supportIndex == startIndex && IsAttachedToColumn(config.StartType) && leftHookFeet > 0;
                var bendEndDown = supportIndex == endIndex && IsAttachedToColumn(config.EndType) && rightHookFeet > 0;

                created += bendStartDown || bendEndDown
                    ? longCreator.CreateSegmentWithEndBends(
                        host, frame, config.Diameter, config.Count, atTop: true, layerOffsetFeet,
                        startDistance / frame.LengthFeet, endDistance / frame.LengthFeet,
                        bendStartDown, bendEndDown, leftHookFeet, rightHookFeet,
                        config.PositionInSection, mainCount, warnings,
                        forceFixedNumberAcrossWidth: config.Layer >= 2)
                    : longCreator.CreateSegment(
                        host, frame, config.Diameter, config.Count, atTop: true, layerOffsetFeet,
                        startDistance / frame.LengthFeet, endDistance / frame.LengthFeet,
                        config.PositionInSection, mainCount, warnings,
                        forceFixedNumberAcrossWidth: config.Layer >= 2);
            }
        }

        return created;
    }

    private static bool IsAttachedToColumn(string? type)
        => type is null || !type.Contains("through", StringComparison.OrdinalIgnoreCase);

    private static double ResolveAdditionalExtendFeet(double lengthMm, double ratio, double fixedEachSideFeet, double spanLengthFeet)
    {
        if (lengthMm > 0) return lengthMm / 304.8;
        if (ratio > 0 && spanLengthFeet > 0) return spanLengthFeet * ratio;
        if (fixedEachSideFeet > 0) return fixedEachSideFeet;
        return spanLengthFeet * 0.25;
    }

    private static bool TryCreateStirrupFrame(
        Span span,
        BeamSegment segment,
        IReadOnlyList<Support> supports,
        StirrupConfig config,
        out SpanFrame frame)
    {
        frame = null!;

        var axis = UnitAxis(span);
        if (axis is null) return false;

        var firstDistanceFeet = config.FirstDistanceFromSupportMm / 304.8;
        var start = span.Start;
        var end = span.End;

        if (TryFindSupport(start, supports, out var startSupport))
        {
            var clearance = Math.Min(startSupport.HalfWidthFeet + firstDistanceFeet, span.LengthFeet / 3);
            start = PointAt(start, axis.Value, clearance);
        }

        if (TryFindSupport(end, supports, out var endSupport))
        {
            var clearance = Math.Min(endSupport.HalfWidthFeet + firstDistanceFeet, span.LengthFeet / 3);
            end = PointAt(end, axis.Value, -clearance);
        }

        if (start.DistanceTo(end) <= SupportToleranceFeet)
            return false;

        var adjusted = new Span(span.Index, start, end, span.Section);
        frame = new SpanFrame(adjusted, segment.TopElevationFeet, segment.BottomElevationFeet, segment.LateralOffsetFeet);
        return true;
    }

    private static (FamilyInstance Host, BeamSegment Segment) FindHostForSpan(
        Span span,
        IReadOnlyList<FamilyInstance> beams,
        IReadOnlyList<BeamSegment> segments)
    {
        var midpoint = new Point3(
            (span.Start.X + span.End.X) / 2,
            (span.Start.Y + span.End.Y) / 2,
            (span.Start.Z + span.End.Z) / 2);

        for (var i = 0; i < segments.Count; i++)
        {
            if (PointProjectsInsideSegment(midpoint, segments[i]))
                return (beams[Math.Min(i, beams.Count - 1)], segments[i]);
        }

        return (beams[0], segments[0]);
    }

    private static bool PointProjectsInsideSegment(Point3 point, BeamSegment segment)
        => TryProjectDistanceOnSegment(point, segment, out _);

    private static bool TryProjectDistanceOnSegment(Point3 point, BeamSegment segment, out double distance)
    {
        distance = 0;
        var ax = segment.End.X - segment.Start.X;
        var ay = segment.End.Y - segment.Start.Y;
        var az = segment.End.Z - segment.Start.Z;
        var lenSq = ax * ax + ay * ay + az * az;
        if (lenSq < 1e-9) return false;

        var t = ((point.X - segment.Start.X) * ax +
                 (point.Y - segment.Start.Y) * ay +
                 (point.Z - segment.Start.Z) * az) / lenSq;

        if (t is < -0.001 or > 1.001)
            return false;

        distance = Math.Clamp(t, 0, 1) * Math.Sqrt(lenSq);
        return true;
    }

    private static double ProjectDistanceOnSegmentUnclamped(Point3 point, BeamSegment segment)
    {
        var ax = segment.End.X - segment.Start.X;
        var ay = segment.End.Y - segment.Start.Y;
        var az = segment.End.Z - segment.Start.Z;
        var lenSq = ax * ax + ay * ay + az * az;
        if (lenSq < 1e-9) return 0;

        var t = ((point.X - segment.Start.X) * ax +
                 (point.Y - segment.Start.Y) * ay +
                 (point.Z - segment.Start.Z) * az) / lenSq;

        return t * Math.Sqrt(lenSq);
    }

    private static (double X, double Y, double Z)? UnitAxis(Span span)
    {
        var x = span.End.X - span.Start.X;
        var y = span.End.Y - span.Start.Y;
        var z = span.End.Z - span.Start.Z;
        var len = Math.Sqrt(x * x + y * y + z * z);
        return len < 1e-9 ? null : (x / len, y / len, z / len);
    }

    private static Point3 PointAt(Point3 origin, (double X, double Y, double Z) axis, double distance)
        => new(origin.X + axis.X * distance, origin.Y + axis.Y * distance, origin.Z + axis.Z * distance);

    private static bool TryFindSupport(Point3 point, IReadOnlyList<Support> supports, out Support support)
    {
        var found = supports
            .Where(s => s.Location.DistanceTo(point) <= SupportToleranceFeet)
            .OrderBy(s => s.Location.DistanceTo(point))
            .FirstOrDefault();
        support = found!;
        return support is not null;
    }


}
