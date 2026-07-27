using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Services.BeamDrawing;
using RevitAPP.Helpers;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

public sealed class LongitudinalAnnotator
{
    private readonly ProjectResourceProvider _resources = new();
    private readonly RebarTagPlacer _tags = new();
    private readonly DimensionPlacer _dimensions = new();
    private readonly LongitudinalDimensionPlacer _longitudinalDimensions = new();
    private readonly SpotElevationPlacer _spots = new();
    private readonly BeamGeometryReader _geometryReader = new();

    public void Annotate(Document document, IReadOnlyList<FamilyInstance> beams,
        IReadOnlyList<ViewSection> views, LongitudinalDrawingSetting setting,
        BeamLongitudinalDrawingResult result, LongitudinalDrawingReviewResult review,
        LongitudinalDrawingReviewResult tagStationReview)
    {
        var warnings = new List<string>();
        var longitudinalTag = _resources.ResolveRebarTagType(document, setting.LongitudinalRebarTagTypeName, warnings);
        var stirrupTag = _resources.ResolveRebarTagType(document, setting.StirrupTagTypeName, warnings);
        var dimensionType = _resources.ResolveDimType(document, setting.DimensionTypeName, warnings);
        var spotType = _resources.ResolveSpotType(document, setting.SpotElevationTypeName, warnings);
        var detailSymbolId = _resources.ResolveBreakLineSymbol(document, setting.DetailComponentTypeName, warnings);
        var hostIds = beams.Select(beam => beam.Id).ToHashSet();
        var rebars = new FilteredElementCollector(document).OfClass(typeof(Rebar)).Cast<Rebar>()
            .Where(rebar => hostIds.Contains(rebar.GetHostId())).ToList();

        // View ngang do LongitudinalCrossSectionAnnotator độc lập xử lý.
        // Annotator này chỉ sở hữu view dọc.
        for (var viewIndex = 0; viewIndex < Math.Min(views.Count, 1); viewIndex++)
        {
            var view = views[viewIndex];
            var visible = rebars.Where(rebar => IsVisibleIn(rebar, view)).ToList();
            var longitudinalFlags = visible.Select(rebar => IsLongitudinal(rebar, beams)).ToList();
            var taggable = visible.Select((rebar, index) => new
                { Rebar = rebar, IsLongitudinal = longitudinalFlags[index] })
                .Where(item => viewIndex != 0 || item.IsLongitudinal || !HasFiftyMillimeterSpacing(item.Rebar))
                .ToList();
            visible = taggable.Select(item => item.Rebar).ToList();
            longitudinalFlags = taggable.Select(item => item.IsLongitudinal).ToList();
            if (viewIndex == 0)
                (visible, longitudinalFlags) = ExpandLongitudinalTagRequests(
                    view, visible, longitudinalFlags, tagStationReview);
            var tagTypes = longitudinalFlags.Select(isLongitudinal => isLongitudinal ? longitudinalTag : stirrupTag).ToList();
            var tagHeads = viewIndex == 0
                ? BuildLongitudinalTagHeads(view, visible, longitudinalFlags, tagStationReview)
                : null;
            _tags.TagRebars(document, view, visible, tagTypes, warnings, spacingFactor: 5,
                tagHeadLocals: tagHeads, sharedStemLeaders: viewIndex == 0,
                sharedStemFlags: longitudinalFlags);
        }

        var longitudinalView = views[0];
        foreach (var beam in beams)
        {
            if (!_geometryReader.TryRead(document, beam, out var geometry, out var error))
            {
                warnings.Add(error);
                continue;
            }
            var pair = new ViewBeamPair(longitudinalView, beam, geometry, IsCross: false);
            _spots.Place(document, longitudinalView, pair, spotType, setting.AnnotationOffsetMm, warnings);
        }
        _longitudinalDimensions.Place(document, longitudinalView, beams, rebars, review.Chain,
            dimensionType, setting.AnnotationOffsetMm, warnings);
        PlaceSupportDetails(document, longitudinalView, review, detailSymbolId, warnings);

        foreach (var warning in warnings)
            result.AddWarning(new BeamLongitudinalDrawingWarning("ANNOTATION", warning));
    }

    private static void PlaceSupportDetails(Document document, View view, LongitudinalDrawingReviewResult review,
        ElementId? symbolId, List<string> warnings)
    {
        if (symbolId == null || symbolId == ElementId.InvalidElementId) return;
        if (document.GetElement(symbolId) is not FamilySymbol symbol) return;
        try
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
                document.Regenerate();
            }
        }
        catch { }
        var paperFoot = Math.Max(view.Scale, 1) / 304.8;
        var breakOffset = 2 * paperFoot;
        var transform = view.CropBox.Transform;
        var inverse = transform.Inverse;
        var beamLocalYs = new List<double>();
        foreach (var hostId in review.Chain.Spans.Select(span => span.HostId).Where(id => id > 0).Distinct())
        {
            try
            {
                var beam = document.GetElement(ElementIdHelper.Create(hostId));
                var box = beam?.get_BoundingBox(view) ?? beam?.get_BoundingBox(null);
                if (box == null) continue;
                foreach (var corner in new[]
                         {
                             new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                             new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                             new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                             new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                         })
                    beamLocalYs.Add(inverse.OfPoint(corner).Y);
            }
            catch { }
        }
        var columnRanges = new List<(double Min, double Max)>();
        foreach (var column in new FilteredElementCollector(document, view.Id)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType())
        {
            try
            {
                var box = column.get_BoundingBox(view) ?? column.get_BoundingBox(null);
                if (box == null) continue;
                var corners = new[]
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                };
                var localXs = corners.Select(item => inverse.OfPoint(item).X).ToList();
                columnRanges.Add((localXs.Min(), localXs.Max()));
            }
            catch { }
        }
        var supports = review.Chain.Spans
            .SelectMany(span => new[]
            {
                (Point: span.Start, HalfHeight: span.HeightFeet * 0.5),
                (Point: span.End, HalfHeight: span.HeightFeet * 0.5)
            })
            .GroupBy(item => (Math.Round(item.Point.X, 5), Math.Round(item.Point.Y, 5),
                Math.Round(item.Point.Z, 5)))
            .Select(group => (Point: group.First().Point, HalfHeight: group.Max(item => item.HalfHeight)))
            .ToList();
        var placedBreaks = new List<FamilyInstance>();
        void PlaceBreak(Line line, XYZ fallbackPoint)
        {
            FamilyInstance instance;
            try { instance = document.Create.NewFamilyInstance(line, symbol, view); }
            catch { instance = document.Create.NewFamilyInstance(fallbackPoint, symbol, view); }
            try
            {
                var length = instance.LookupParameter("Length");
                if (length is { IsReadOnly: false, StorageType: StorageType.Double })
                    length.Set(line.Length);
            }
            catch { }
            placedBreaks.Add(instance);
        }
        foreach (var support in supports)
        {
            try
            {
                var point = support.Point;
                var supportWorld = new XYZ(point.X, point.Y, point.Z);
                var supportLocal = inverse.OfPoint(supportWorld);
                var supportProjection = supportLocal.X;
                var range = columnRanges.OrderBy(item =>
                        Math.Abs((item.Min + item.Max) * 0.5 - supportProjection))
                    .FirstOrDefault();
                var minProjection = range.Max > range.Min ? range.Min : supportProjection - 150.0 / 304.8;
                var maxProjection = range.Max > range.Min ? range.Max : supportProjection + 150.0 / 304.8;
                var fallbackCenterY = supportLocal.Y;
                var topLocalY = (beamLocalYs.Count > 0 ? beamLocalYs.Max() : fallbackCenterY + support.HalfHeight)
                                + breakOffset;
                var bottomLocalY = (beamLocalYs.Count > 0 ? beamLocalYs.Min() : fallbackCenterY - support.HalfHeight)
                                   - breakOffset;
                var topLeft = transform.OfPoint(new XYZ(minProjection, topLocalY, 0));
                var topRight = transform.OfPoint(new XYZ(maxProjection, topLocalY, 0));
                var bottomLeft = transform.OfPoint(new XYZ(minProjection, bottomLocalY, 0));
                var bottomRight = transform.OfPoint(new XYZ(maxProjection, bottomLocalY, 0));
                // Reverse both curve directions so the upper break opens downward and the lower break
                // opens upward, toward the cut column as in the drafting standard.
                var topLine = Line.CreateBound(topRight, topLeft);
                var bottomLine = Line.CreateBound(bottomLeft, bottomRight);
                PlaceBreak(topLine, (topLeft + topRight) * 0.5);
                PlaceBreak(bottomLine, (bottomLeft + bottomRight) * 0.5);
            }
            catch (Exception exception)
            {
                warnings.Add($"Không đặt được Detail Component tại support: {exception.Message}");
            }
        }
        if (placedBreaks.Count > 0)
        {
            try
            {
                document.Regenerate();
                var localYs = placedBreaks.Select(item => item.get_BoundingBox(view))
                    .Where(box => box != null)
                    .SelectMany(box => new[]
                    {
                        inverse.OfPoint(box!.Min).Y,
                        inverse.OfPoint(box.Max).Y
                    }).ToList();
                if (localYs.Count > 0)
                {
                    var crop = view.CropBox;
                    crop.Min = new XYZ(crop.Min.X, localYs.Min() - 0.5 * paperFoot, crop.Min.Z);
                    crop.Max = new XYZ(crop.Max.X, localYs.Max() + 0.5 * paperFoot, crop.Max.Z);
                    view.CropBox = crop;
                    view.CropBoxVisible = false;
                }
            }
            catch (Exception exception)
            {
                warnings.Add($"Khong thu gon duoc vung nhin theo Detail Component: {exception.Message}");
            }
        }
    }

    private static bool IsVisibleIn(Rebar rebar, View view)
    {
        try { return rebar.get_BoundingBox(view) != null; }
        catch { return false; }
    }

    private static bool HasFiftyMillimeterSpacing(Rebar rebar)
    {
        try
        {
            var spacing = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING)?.AsDouble();
            return spacing is > 0 && Math.Abs(spacing.Value * 304.8 - 50.0) <= 1.0;
        }
        catch { return false; }
    }

    private static XYZ PointAt(BeamChainModel chain, double distance)
    {
        var cumulative = 0d;
        foreach (var span in chain.Spans)
        {
            if (distance <= cumulative + span.LengthFeet + 1e-9)
            {
                var t = Math.Clamp((distance - cumulative) / span.LengthFeet, 0, 1);
                return new XYZ(span.Start.X + (span.End.X - span.Start.X) * t,
                    span.Start.Y + (span.End.Y - span.Start.Y) * t,
                    span.Start.Z + (span.End.Z - span.Start.Z) * t);
            }
            cumulative += span.LengthFeet;
        }
        var end = chain.End;
        return new XYZ(end.X, end.Y, end.Z);
    }

    private static bool IsLongitudinal(Rebar rebar, IReadOnlyList<FamilyInstance> beams)
    {
        var host = beams.FirstOrDefault(beam => beam.Id == rebar.GetHostId());
        if (host?.Location is not LocationCurve { Curve: Line axisLine }) return true;
        try
        {
            var axis = (axisLine.GetEndPoint(1) - axisLine.GetEndPoint(0)).Normalize();
            return rebar.GetCenterlineCurves(false, false, false,
                    MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .Any(curve => Math.Abs((curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize().DotProduct(axis)) > 0.7);
        }
        catch { return true; }
    }

    /// <summary>
    /// Distributes longitudinal-view tags around their actual rebar zone instead of collecting every tag
    /// in one column at the right crop edge. Upper bars go above the beam, lower bars and stirrups go below;
    /// nearby heads use alternating lanes to keep tag text and leaders readable.
    /// </summary>
    private static IReadOnlyList<(double X, double Y)>? BuildLongitudinalTagHeads(View view,
        IReadOnlyList<Rebar> rebars, IReadOnlyList<bool> longitudinalFlags,
        LongitudinalDrawingReviewResult review)
    {
        var crop = view.CropBox;
        if (!view.CropBoxActive || crop == null || rebars.Count == 0) return null;

        var inverse = crop.Transform.Inverse;
        var items = rebars.Select((rebar, index) =>
        {
            try
            {
                var box = rebar.get_BoundingBox(view);
                if (box == null) return (Index: index, Valid: false, MinX: 0d, MaxX: 0d, MinY: 0d, MaxY: 0d);
                var corners = new[]
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                }.Select(inverse.OfPoint).ToList();
                return (Index: index, Valid: true, MinX: corners.Min(p => p.X), MaxX: corners.Max(p => p.X),
                    MinY: corners.Min(p => p.Y), MaxY: corners.Max(p => p.Y));
            }
            catch { return (Index: index, Valid: false, MinX: 0d, MaxX: 0d, MinY: 0d, MaxY: 0d); }
        }).ToList();

        var validItems = items.Where(item => item.Valid).ToList();
        if (validItems.Count == 0) return null;

        var geometryTop = validItems.Max(item => item.MaxY);
        var geometryBottom = validItems.Min(item => item.MinY);
        var beamMidY = (geometryTop + geometryBottom) * 0.5;
        var paperFoot = Math.Max(view.Scale, 1) / 304.8;
        var edgeOffset = 6 * paperFoot;
        var laneSpacing = 7 * paperFoot;
        var collisionDistance = 24 * paperFoot;
        var stationTolerance = 15 * paperFoot;
        var xMargin = 5 * paperFoot;
        // The shared stem sits 20 mm (paper) to the left of the tag head. Use a larger support inset so
        // the stem itself, not only the text head, clears the column at the left end support.
        var supportTagInset = 58 * paperFoot;
        var topBase = geometryTop + edgeOffset;
        var bottomBase = geometryBottom - edgeOffset;
        // Each internal column owns two support sides: the right end of the span on its left and the
        // left end of the span on its right. Keeping both sides prevents their tags collapsing at the
        // shared column centre.
        var supportSides = BuildSupportSides(review, view, inverse);
        var midSpanXs = review.Stations
            .Where(station => station.Kind == SectionStationKind.MidSpan)
            .Select(station => inverse.OfPoint(PointAt(review.Chain, station.ChainDistanceFeet)).X)
            .DistinctBy(x => Math.Round(x, 4)).ToList();
        var result = new (double X, double Y)[rebars.Count];
        var topLongitudinalHeads = new List<double>();
        var bottomLongitudinalHeads = new List<double>();
        var topStirrupHeads = new List<double>();
        var occurrenceByRebarId = new Dictionary<long, int>();
        var sideExtent = 15 * paperFoot;
        var supportTagXs = supportSides.Select(support =>
        {
            // Prefer the shortest upper bar crossing this support (normally the support reinforcement).
            // Put the common vertical stem at the center of that bar's visible zone and the tag head to its right.
            var reinforcement = items
                .Where(item => item.Valid && item.Index < longitudinalFlags.Count && longitudinalFlags[item.Index])
                .Where(item => support.RawX >= item.MinX - stationTolerance &&
                               support.RawX <= item.MaxX + stationTolerance)
                .Where(item => ExtendsIntoSupportSide(item.MinX, item.MaxX,
                    support.RawX, support.Direction, sideExtent))
                .Where(item => LongitudinalLayerY(rebars[item.Index], inverse,
                    (item.MinY + item.MaxY) * 0.5) >= beamMidY)
                .OrderBy(item => item.MaxX - item.MinX)
                .FirstOrDefault();
            if (reinforcement.Valid)
            {
                var zoneMin = support.Direction > 0
                    ? Math.Max(support.RawX, reinforcement.MinX)
                    : reinforcement.MinX;
                var zoneMax = support.Direction > 0
                    ? reinforcement.MaxX
                    : Math.Min(support.RawX, reinforcement.MaxX);
                var calculatedStemX = support.Direction > 0
                    ? zoneMin + (zoneMax - zoneMin) * 0.30
                    : zoneMax - (zoneMax - zoneMin) * 0.30;
                // Tag graphics open to the right of the stem. At a right support the stem therefore
                // needs extra clearance on the left so the text and number bubble do not touch the column.
                var stemX = support.Direction > 0
                    ? Math.Max(calculatedStemX, support.RawX + 10 * paperFoot)
                    : Math.Min(calculatedStemX, support.RawX - 45 * paperFoot);
                return (support.RawX, TagX: stemX + 20 * paperFoot, support.Direction);
            }

            // Fallback when no upper rebar geometry can be read.
            return (support.RawX, TagX: support.RawX + support.Direction * supportTagInset,
                support.Direction);
        }).OrderBy(item => item.TagX).ToList();

        // Short reinforcement bars are processed first. A longer main bar that shares an end then reuses
        // that anchor and occupies the next vertical lane, producing one compact support-tag stack.
        foreach (var item in items.OrderBy(item =>
                     item.Valid && item.Index < longitudinalFlags.Count && longitudinalFlags[item.Index]
                         ? item.MaxX - item.MinX
                         : double.MaxValue)
                 .ThenBy(item => item.Valid ? (item.MinX + item.MaxX) * 0.5 : double.MaxValue))
        {
            var isLongitudinal = item.Index < longitudinalFlags.Count && longitudinalFlags[item.Index];
            // Hooks and vertical legs can pull a top reinforcement bar's bounding-box center below the
            // beam midline. Classify its layer from the longest horizontal centerline segment instead.
            var centerY = item.Valid
                ? LongitudinalLayerY(rebars[item.Index], inverse, (item.MinY + item.MaxY) * 0.5)
                : beamMidY;
            // Stirrup-zone tags belong above the beam in a longitudinal drawing; main bars follow their layer.
            var placeAbove = !isLongitudinal || centerY >= beamMidY;
            var occupied = isLongitudinal
                ? placeAbove ? topLongitudinalHeads : bottomLongitudinalHeads
                : topStirrupHeads;
            var fallbackX = (crop.Min.X + crop.Max.X) * 0.5;
            IReadOnlyList<double> candidates;
            var preserveSemanticX = false;
            if (item.Valid && isLongitudinal)
            {
                // Support clusters contain upper main + upper reinforcement only. Lower bars are anchored
                // exclusively at midspan stations, so their tags cannot drift into a support cluster.
                var semanticStations = placeAbove
                    ? supportTagXs
                    : midSpanXs.Select(x => (RawX: x, TagX: x, Direction: 0d)).ToList();
                var withinBar = semanticStations
                    .Where(station => station.RawX >= item.MinX - stationTolerance &&
                                      station.RawX <= item.MaxX + stationTolerance)
                    .Where(station => station.Direction == 0 || ExtendsIntoSupportSide(item.MinX,
                        item.MaxX, station.RawX, station.Direction, sideExtent))
                    .OrderBy(station => station.TagX)
                    .Select(station => station.TagX)
                    .ToList();
                if (withinBar.Count > 0)
                {
                    preserveSemanticX = true;
                    var rebarId = rebars[item.Index].Id.ToValue();
                    occurrenceByRebarId.TryGetValue(rebarId, out var occurrence);
                    occurrenceByRebarId[rebarId] = occurrence + 1;
                    candidates = new[] { withinBar[Math.Min(occurrence, withinBar.Count - 1)] };
                }
                else
                {
                    candidates = new[] { item.MinX + xMargin, item.MaxX - xMargin, (item.MinX + item.MaxX) * 0.5 };
                }
            }
            else
            {
                candidates = new[] { item.Valid ? (item.MinX + item.MaxX) * 0.5 : fallbackX };
            }
            var clampedCandidates = candidates
                .Select(candidate => Math.Clamp(candidate, crop.Min.X + xMargin, crop.Max.X - xMargin))
                .ToList();
            var reusableAnchor = isLongitudinal && !preserveSemanticX
                ? occupied
                    .SelectMany(anchor => clampedCandidates.Select(candidate => new
                    {
                        Anchor = anchor,
                        Distance = Math.Abs(anchor - candidate)
                    }))
                    .Where(pair => pair.Distance < collisionDistance)
                    .OrderBy(pair => pair.Distance)
                    .Select(pair => (double?)pair.Anchor)
                    .FirstOrDefault()
                : null;
            var x = preserveSemanticX
                ? clampedCandidates[0]
                : reusableAnchor ?? clampedCandidates
                    .OrderBy(candidate => occupied.Count(previousX => Math.Abs(previousX - candidate) < collisionDistance))
                    .ThenBy(candidate => Math.Abs(candidate - fallbackX))
                    .First();
            var laneDistance = preserveSemanticX ? 3 * paperFoot : collisionDistance;
            var lane = occupied.Count(previousX => Math.Abs(previousX - x) < laneDistance);
            if (!preserveSemanticX && lane > 2)
            {
                x += (lane - 2) * collisionDistance;
                lane = occupied.Count(previousX => Math.Abs(previousX - x) < collisionDistance);
            }
            occupied.Add(x);
            // Stirrup tags have no leader and occupy the top annotation row immediately below the
            // upper dimension. Main/additional longitudinal tags remain in the lower stacked rows.
            var y = placeAbove
                ? isLongitudinal
                    ? topBase + lane * laneSpacing
                    : topBase + 2 * laneSpacing
                : bottomBase - lane * laneSpacing;
            result[item.Index] = (x, y);
        }

        return result;
    }

    private static (List<Rebar> Rebars, List<bool> LongitudinalFlags) ExpandLongitudinalTagRequests(
        View view, IReadOnlyList<Rebar> rebars, IReadOnlyList<bool> longitudinalFlags,
        LongitudinalDrawingReviewResult review)
    {
        var crop = view.CropBox;
        if (crop == null) return (rebars.ToList(), longitudinalFlags.ToList());
        var inverse = crop.Transform.Inverse;
        var boxes = rebars.Select(rebar =>
        {
            try
            {
                var box = rebar.get_BoundingBox(view);
                if (box == null) return (Valid: false, MinX: 0d, MaxX: 0d, MinY: 0d, MaxY: 0d);
                var corners = new[]
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                }.Select(inverse.OfPoint).ToList();
                return (Valid: true, MinX: corners.Min(point => point.X), MaxX: corners.Max(point => point.X),
                    MinY: corners.Min(point => point.Y), MaxY: corners.Max(point => point.Y));
            }
            catch { return (Valid: false, MinX: 0d, MaxX: 0d, MinY: 0d, MaxY: 0d); }
        }).ToList();
        var valid = boxes.Where(box => box.Valid).ToList();
        if (valid.Count == 0) return (rebars.ToList(), longitudinalFlags.ToList());
        var beamMidY = (valid.Max(box => box.MaxY) + valid.Min(box => box.MinY)) * 0.5;
        var supportSides = BuildSupportSides(review, view, inverse);
        var midSpanXs = review.Stations.Where(station => station.Kind == SectionStationKind.MidSpan)
            .Select(station => inverse.OfPoint(PointAt(review.Chain, station.ChainDistanceFeet)).X).ToList();
        var tolerance = 15.0 * Math.Max(view.Scale, 1) / 304.8;
        var sideExtent = 15.0 * Math.Max(view.Scale, 1) / 304.8;
        var expandedRebars = new List<Rebar>();
        var expandedFlags = new List<bool>();
        for (var index = 0; index < rebars.Count; index++)
        {
            var isLongitudinal = index < longitudinalFlags.Count && longitudinalFlags[index];
            var copies = 1;
            if (isLongitudinal && boxes[index].Valid)
            {
                var layerY = LongitudinalLayerY(rebars[index], inverse,
                    (boxes[index].MinY + boxes[index].MaxY) * 0.5);
                copies = layerY >= beamMidY
                    ? Math.Max(1, supportSides.Count(station =>
                        station.RawX >= boxes[index].MinX - tolerance &&
                        station.RawX <= boxes[index].MaxX + tolerance &&
                        ExtendsIntoSupportSide(boxes[index].MinX, boxes[index].MaxX,
                            station.RawX, station.Direction, sideExtent)))
                    : Math.Max(1, midSpanXs.Count(x =>
                        x >= boxes[index].MinX - tolerance && x <= boxes[index].MaxX + tolerance));
            }
            for (var copy = 0; copy < copies; copy++)
            {
                expandedRebars.Add(rebars[index]);
                expandedFlags.Add(isLongitudinal);
            }
        }
        return (expandedRebars, expandedFlags);
    }

    private static List<(double RawX, double Direction)> BuildSupportSides(
        LongitudinalDrawingReviewResult review,
        View view,
        Transform inverse)
    {
        var result = new List<(double RawX, double Direction)>();
        var columnFaces = ReadColumnFaces(view.Document, view, inverse);

        (double LeftFace, double RightFace) FacesAt(double x)
        {
            var column = columnFaces
                .Where(item => x >= item.MinX - 100.0 / 304.8 && x <= item.MaxX + 100.0 / 304.8)
                .OrderBy(item => Math.Abs((item.MinX + item.MaxX) * 0.5 - x))
                .FirstOrDefault();
            return column.Valid ? (column.MinX, column.MaxX) : (x, x);
        }

        // Annotation needs two support zones at every internal column even when the section-station
        // planner reduces equivalent cross sections. Therefore derive support sides from every span,
        // not from the reduced review.Stations collection.
        foreach (var span in review.Chain.Spans)
        {
            var startX = inverse.OfPoint(new XYZ(span.Start.X, span.Start.Y, span.Start.Z)).X;
            var endX = inverse.OfPoint(new XYZ(span.End.X, span.End.Y, span.End.Z)).X;
            var startFaces = FacesAt(startX);
            var endFaces = FacesAt(endX);
            if (startX <= endX)
            {
                result.Add((startFaces.RightFace, 1d));
                result.Add((endFaces.LeftFace, -1d));
            }
            else
            {
                result.Add((startFaces.LeftFace, -1d));
                result.Add((endFaces.RightFace, 1d));
            }
        }
        return result.OrderBy(item => item.RawX).ThenBy(item => item.Direction).ToList();
    }

    private static List<(bool Valid, double MinX, double MaxX)> ReadColumnFaces(Document document,
        View view, Transform inverse)
    {
        var result = new List<(bool Valid, double MinX, double MaxX)>();
        foreach (var column in new FilteredElementCollector(document)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType())
        {
            try
            {
                var box = column.get_BoundingBox(view) ?? column.get_BoundingBox(null);
                if (box == null) continue;
                var corners = new[]
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                }.Select(inverse.OfPoint).ToList();
                result.Add((true, corners.Min(point => point.X), corners.Max(point => point.X)));
            }
            catch { }
        }
        return result;
    }

    private static bool ExtendsIntoSupportSide(double minX, double maxX, double supportX,
        double direction, double requiredExtent) => direction > 0
        ? maxX >= supportX + requiredExtent
        : minX <= supportX - requiredExtent;

    private static double LongitudinalLayerY(Rebar rebar, Transform inverse, double fallback)
    {
        try
        {
            var segment = rebar.GetCenterlineCurves(false, false, false,
                    MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .Select(curve =>
                {
                    var start = inverse.OfPoint(curve.GetEndPoint(0));
                    var end = inverse.OfPoint(curve.GetEndPoint(1));
                    return new
                    {
                        HorizontalLength = Math.Abs(end.X - start.X),
                        LayerY = (start.Y + end.Y) * 0.5
                    };
                })
                .OrderByDescending(item => item.HorizontalLength)
                .FirstOrDefault();
            return segment is { HorizontalLength: > 1e-6 } ? segment.LayerY : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
