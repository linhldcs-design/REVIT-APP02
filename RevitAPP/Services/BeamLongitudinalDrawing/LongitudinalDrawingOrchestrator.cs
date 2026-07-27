using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Helpers;
using RevitAPP.Services.BeamDrawing;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

public sealed class LongitudinalDrawingOrchestrator
{
    private readonly ProjectResourceProvider _resources = new();
    private readonly LongitudinalSectionBoxCalculator _boxes = new();
    private readonly LongitudinalViewBuilder _longitudinalBuilder = new();
    private readonly StationCrossViewBuilder _crossBuilder = new();
    private readonly LongitudinalAnnotator _annotator = new();
    private readonly LongitudinalCrossSectionAnnotator _crossAnnotator = new();
    private readonly LongitudinalSheetBuilder _sheetBuilder = new();

    public BeamLongitudinalDrawingResult Generate(Document document,
        IReadOnlyList<FamilyInstance> beams, LongitudinalDrawingReviewResult review)
    {
        ValidateSnapshot(beams, review);
        var result = new BeamLongitudinalDrawingResult();
        var warnings = new List<string>();
        var setting = review.Setting;
        var orderedStations = new List<SectionStation>();
        var effectiveReview = review;
        var longType = _resources.ResolveSectionType(document, setting.LongitudinalSectionTypeName, warnings);
        var crossType = _resources.ResolveSectionType(document, setting.CrossSectionTypeName, warnings);
        var longitudinalTemplate = _resources.ResolveViewTemplate(document, setting.ViewTemplateName, warnings);
        var crossTemplate = _resources.ResolveViewTemplate(document, setting.CrossViewTemplateName, warnings);
        foreach (var warning in warnings) result.AddWarning(new BeamLongitudinalDrawingWarning("RESOURCE", warning));

        using var group = new TransactionGroup(document, "Triển khai mặt cắt dọc dầm");
        group.Start();
        try
        {
            var views = new List<ViewSection>();
            using (var transaction = new Transaction(document, "Tạo mặt cắt dọc và mặt cắt station"))
            {
                transaction.Start();
                var longView = _longitudinalBuilder.Create(document, longType,
                    _boxes.CreateLongitudinal(review.Chain, review.IsReversed), setting.Scale, longitudinalTemplate,
                    $"CHI TIẾT DẦM {BeamMark(beams[0])}");
                views.Add(longView); result.AddLongitudinalView(longView.Id.ToValue());
                SetRebarVisible(document, beams, longView, result);
                var chainAxis = new XYZ(
                    review.Chain.End.X - review.Chain.Start.X,
                    review.Chain.End.Y - review.Chain.Start.Y,
                    review.Chain.End.Z - review.Chain.Start.Z).Normalize();
                var increasingIsViewRight = chainAxis.DotProduct(longView.RightDirection) >= 0;
                var adjustedStations = review.Stations.Select(station => station with
                {
                    ChainDistanceFeet = AdjustSupportStation(document, review.Chain, station,
                        increasingIsViewRight, setting.Scale)
                }).ToList();
                orderedStations = review.IsReversed
                    ? adjustedStations.AsEnumerable().Reverse().ToList()
                    : adjustedStations;
                effectiveReview = review with { Stations = orderedStations };
                for (var index = 0; index < orderedStations.Count; index++)
                {
                    var station = orderedStations[index];
                    var stationBeam = BeamAtStation(beams, review.Chain, station.ChainDistanceFeet);
                    var cross = _crossBuilder.Create(document, crossType,
                        _boxes.CreateCross(review.Chain, station.ChainDistanceFeet, review.IsReversed, stationBeam),
                        setting.Scale, crossTemplate, $"MCN-DAM-{review.Chain.Spans[0].SourceId}-{index + 1:00}");
                    views.Add(cross); result.AddCrossSectionView(cross.Id.ToValue());
                    SetRebarVisible(document, beams, cross, result);
                }
                transaction.Commit();
            }

            using (var annotation = new Transaction(document, "Gắn tag thép mặt cắt dầm"))
            {
                annotation.Start();
                _annotator.Annotate(document, beams, views, setting, result, effectiveReview, review);
                AnnotateCrossViewsLikeBeamDrawing(document, beams, views, effectiveReview, setting, result);
                annotation.Commit();
            }

            using (var sheet = new Transaction(document, "Tạo sheet mặt cắt dầm"))
            {
                sheet.Start();
                _sheetBuilder.CreateAndPlace(document, views, setting, result);
                sheet.Commit();
            }
            group.Assimilate();
            return result;
        }
        catch
        {
            group.RollBack();
            throw;
        }
    }

    private static FamilyInstance? BeamAtStation(
        IReadOnlyList<FamilyInstance> beams, BeamChainModel chain, double distanceFeet)
    {
        var cumulative = 0d;
        var span = chain.Spans[^1];
        foreach (var candidate in chain.Spans)
        {
            if (distanceFeet <= cumulative + candidate.LengthFeet + 1e-9)
            {
                span = candidate;
                break;
            }
            cumulative += candidate.LengthFeet;
        }
        return beams.FirstOrDefault(beam => beam.Id.ToValue() == span.HostId);
    }

    private static void ValidateSnapshot(IReadOnlyList<FamilyInstance> beams, LongitudinalDrawingReviewResult review)
    {
        var selected = beams.Select(beam => beam.Id.ToValue()).OrderBy(id => id);
        var confirmed = review.Chain.Spans.Select(span => span.HostId).Distinct().OrderBy(id => id);
        if (!selected.SequenceEqual(confirmed))
            throw new InvalidOperationException("Selection đã thay đổi sau khi xác nhận preview. Hãy review lại.");
    }

    private static void SetRebarVisible(Document document, IReadOnlyList<FamilyInstance> beams,
        View view, BeamLongitudinalDrawingResult result)
    {
        var hostIds = beams.Select(beam => beam.Id).ToHashSet();
        foreach (var rebar in new FilteredElementCollector(document).OfClass(typeof(Rebar)).Cast<Rebar>()
                     .Where(rebar => hostIds.Contains(rebar.GetHostId())))
        {
            try { rebar.SetUnobscuredInView(view, true); }
            catch (Exception exception)
            {
                result.AddWarning(new BeamLongitudinalDrawingWarning("REBAR_VISIBILITY", exception.Message,
                    view.Id.ToValue(), rebar.Id.ToValue()));
            }
        }
    }

    private static string BeamMark(FamilyInstance beam)
    {
        var mark = beam.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        return string.IsNullOrWhiteSpace(mark) ? beam.Id.ToValue().ToString() : mark.Trim();
    }

    private void AnnotateCrossViewsLikeBeamDrawing(Document document,
        IReadOnlyList<FamilyInstance> beams, IReadOnlyList<ViewSection> views,
        LongitudinalDrawingReviewResult review, LongitudinalDrawingSetting setting,
        BeamLongitudinalDrawingResult result)
    {
        var geometryReader = new BeamGeometryReader();
        var contexts = new List<LongitudinalCrossViewContext>();
        for (var index = 0; index < review.Stations.Count && index + 1 < views.Count; index++)
        {
            var station = review.Stations[index];
            var spanIndex = ResolveSpanIndexAtStation(review.Chain, station);
            var span = review.Chain.Spans[spanIndex];
            var beam = beams.FirstOrDefault(item => item.Id.ToValue() == span.HostId);
            if (beam == null)
            {
                result.AddWarning(new BeamLongitudinalDrawingWarning(
                    "CROSS_ANNOTATION", "Không tìm thấy dầm nguồn.", views[index + 1].Id.ToValue()));
                continue;
            }
            if (!geometryReader.TryRead(document, beam, out var geometry, out var error))
            {
                result.AddWarning(new BeamLongitudinalDrawingWarning(
                    "CROSS_ANNOTATION", error, views[index + 1].Id.ToValue()));
                continue;
            }

            var spanStart = review.Chain.Spans.Take(spanIndex).Sum(item => item.LengthFeet);
            var t = Math.Clamp((station.ChainDistanceFeet - spanStart) / span.LengthFeet, 0, 1);
            var geometryStart = new Point3(geometry.Start.X, geometry.Start.Y, geometry.Start.Z);
            if (span.Start.DistanceTo(geometryStart) > span.End.DistanceTo(geometryStart)) t = 1 - t;
            contexts.Add(new LongitudinalCrossViewContext(views[index + 1], beam, geometry, t,
                station.Kind != SectionStationKind.MidSpan));
        }

        if (contexts.Count == 0) return;
        _crossAnnotator.Annotate(document, contexts, setting, result);
    }

    private static int ResolveSpanIndexAtStation(BeamChainModel chain, SectionStation station)
    {
        var spanStart = 0.0;
        for (var index = 0; index < chain.Spans.Count; index++)
        {
            var spanEnd = spanStart + chain.Spans[index].LengthFeet;
            if (station.ChainDistanceFeet >= spanStart - 1e-6 &&
                station.ChainDistanceFeet <= spanEnd + 1e-6)
                return index;
            spanStart = spanEnd;
        }
        return Math.Clamp(station.SourceSpanIndices.FirstOrDefault(), 0, chain.Spans.Count - 1);
    }

    private static double AdjustSupportStation(Document document, BeamChainModel chain,
        SectionStation station, bool increasingIsViewRight, int viewScale)
    {
        if (station.Kind == SectionStationKind.MidSpan)
        {
            const double tagAndLeaderClearancePaperMm = 20.0;
            var shift = tagAndLeaderClearancePaperMm * Math.Max(viewScale, 1) / 304.8;
            var spanIndex = station.SourceSpanIndices.FirstOrDefault();
            if (spanIndex < 0 || spanIndex >= chain.Spans.Count)
                return station.ChainDistanceFeet + (increasingIsViewRight ? shift : -shift);

            var spanStart = chain.Spans.Take(spanIndex).Sum(span => span.LengthFeet);
            var spanEnd = spanStart + chain.Spans[spanIndex].LengthFeet;
            return increasingIsViewRight
                ? Math.Min(station.ChainDistanceFeet + shift, spanEnd - shift)
                : Math.Max(station.ChainDistanceFeet - shift, spanStart + shift);
        }
        var axis = new XYZ(chain.End.X - chain.Start.X, chain.End.Y - chain.Start.Y,
            chain.End.Z - chain.Start.Z).Normalize();
        var chainStart = new XYZ(chain.Start.X, chain.Start.Y, chain.Start.Z);
        var nominalProjection = chainStart.DotProduct(axis) + station.ChainDistanceFeet;
        var columns = new List<(double Min, double Max)>();
        foreach (var column in new FilteredElementCollector(document)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType())
        {
            try
            {
                var box = column.get_BoundingBox(null);
                if (box == null) continue;
                var corners = new[]
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                };
                columns.Add((corners.Min(point => point.DotProduct(axis)),
                    corners.Max(point => point.DotProduct(axis))));
            }
            catch { }
        }
        var support = columns.OrderBy(item => Math.Abs((item.Min + item.Max) * 0.5 - nominalProjection))
            .FirstOrDefault();
        if (support.Max <= support.Min) return station.ChainDistanceFeet;

        var towardIncreasing = station.Kind == SectionStationKind.LeftSupport;
        if (station.Kind == SectionStationKind.SharedSupport)
        {
            var spanIndex = station.SourceSpanIndices.FirstOrDefault();
            var spanStart = chain.Spans.Take(spanIndex).Sum(span => span.LengthFeet);
            var spanEnd = spanStart + chain.Spans[spanIndex].LengthFeet;
            towardIncreasing = Math.Abs(station.ChainDistanceFeet - spanStart) <=
                               Math.Abs(station.ChainDistanceFeet - spanEnd);
        }
        const double supportSectionClearanceMm = 200.0;
        var clearance = supportSectionClearanceMm / 304.8;
        var adjustedProjection = towardIncreasing ? support.Max + clearance : support.Min - clearance;
        return Math.Clamp(adjustedProjection - chainStart.DotProduct(axis), 0, chain.TotalLengthFeet);
    }

}
