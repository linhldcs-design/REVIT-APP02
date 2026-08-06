using System.Globalization;
using System.Text.RegularExpressions;
using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.Core.Services;

/// <summary>
/// Pure geometry analyzer for straight structural beams represented by two CAD boundary rails.
/// Fragment count and gaps never define Revit beam count; section changes do.
/// </summary>
public static class CadBeamAnalyzer
{
    private const double AngleToleranceDegrees = 2.0;
    private const double RailAngleBucketDegrees = 2.0;
    private const double GridAxisSnapToleranceMm = 50.0;
    private const double EndpointSnapToleranceMm = 300.0;
    private const double TextOwnershipBandToleranceMm = 300.0;
    private const double SectionToleranceMm = 2.0;
    private const double SplitLabelAcrossToleranceMm = 150.0;
    private const double SplitLabelAlongToleranceMm = 2000.0;

    // A beam name such as DK1 or D2A: letters then digits, optionally a trailing letter.
    private static readonly Regex MarkRegex = new(
        @"^[A-Za-zĐđ]{1,4}\d{1,3}[A-Za-z]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SectionRegex = new(
        @"(?<b>\d+(?:[\.,]\d+)?)\s*[xX×*]\s*(?<h>\d+(?:[\.,]\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // MTEXT formatting codes. \p (paragraph) carries its own argument list and must be matched
    // before the single-letter toggles, otherwise \pxqc; loses only the backslash and leaves
    // "xqc;" in the text. The trailing alternative catches an argument run whose backslash was
    // already stripped upstream, which would otherwise be read as part of the beam mark.
    private static readonly Regex MTextControlRegex = new(
        @"\\[ACFHQTWp][^;]*;|\\[LlOoKk]|(?<=^|[;}])[ACFHQTWpxt][\d.,]+;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static CadBeamAnalysis Analyze(
        CadStructureTransferPackage beamPackage,
        IReadOnlyList<CadStructureSegment> gridSegments,
        CadBeamAnalysisOptions? options = null)
    {
        options ??= new CadBeamAnalysisOptions();
        var validation = Validate(beamPackage, options);
        if (validation is not null) return Invalid(validation);

        double scale;
        try
        {
            scale = CadGridUnitConverter.MillimetresPerDrawingUnit(beamPackage.InsUnits);
        }
        catch (InvalidDataException exception)
        {
            return Invalid(exception.Message);
        }

        var scaled = beamPackage.Segments
            .Where(segment => Finite(segment.Start) && Finite(segment.End))
            .Select(segment => segment with
            {
                Start = segment.Start * scale,
                End = segment.End * scale
            })
            .Where(segment => segment.Start.DistanceTo(segment.End) >= 1.0)
            .ToArray();
        if (scaled.Length == 0) return Invalid("Vùng chọn Beam không có LINE/POLYLINE hợp lệ.");

        var origin = new CadStructurePoint2(
            scaled.Min(segment => Math.Min(segment.Start.X, segment.End.X)),
            scaled.Min(segment => Math.Min(segment.Start.Y, segment.End.Y)));
        var segments = scaled.Select(segment => segment with
        {
            Start = segment.Start - origin,
            End = segment.End - origin
        }).ToArray();
        var annotations = beamPackage.Annotations
            .Where(annotation => Finite(annotation.Position))
            .Select(annotation => annotation with
            {
                Position = annotation.Position * scale - origin,
                Text = NormalizeText(annotation.Text)
            })
            .Where(annotation => !string.IsNullOrWhiteSpace(annotation.Text))
            .ToArray();
        annotations = JoinSplitLabels(annotations);
        var grids = gridSegments
            .Where(segment => Finite(segment.Start) && Finite(segment.End))
            .Select(segment => segment with
            {
                Start = segment.Start * scale - origin,
                End = segment.End * scale - origin
            })
            .Where(segment => segment.Start.DistanceTo(segment.End) >= options.MinimumLineLengthMm)
            .ToArray();

        // Boundaries are routinely trimmed at every column face, which leaves pieces far shorter
        // than a beam. Assemble rails from every segment first and apply the length gate to the
        // assembled boundary: a short piece that joins others into a long rail is part of a real
        // beam, while one that stays isolated is a tick, a dimension witness line or similar.
        var allRails = BuildRails(segments, options.GapJoinToleranceMm, options.RailOffsetToleranceMm);
        var rails = allRails
            .Where(rail => rail.End - rail.Start >= options.MinimumLineLengthMm)
            .ToArray();
        var shortLines = allRails
            .Where(rail => rail.End - rail.Start < options.MinimumLineLengthMm)
            .Sum(rail => rail.SourceIds.Count);
        var raw = PairRails(rails, grids, annotations, options);
        var merged = MergeRuns(raw, options.MaximumRunGapMm)
            .OrderBy(candidate => candidate.StartMm.X)
            .ThenBy(candidate => candidate.StartMm.Y)
            .Select((candidate, index) => candidate with { Id = index + 1 })
            .ToArray();

        var warnings = new List<string>();
        if (shortLines > 0) warnings.Add($"Đã bỏ qua {shortLines} line ngắn hơn {options.MinimumLineLengthMm:0} mm.");
        if (annotations.Length == 0) warnings.Add("Vùng chọn Beam không có TEXT/MTEXT hợp lệ.");
        if (merged.Length == 0) warnings.Add("Không nhận dạng được cặp biên dầm phù hợp.");

        // Boundaries that never found a partner are the usual reason a line stays grey in the
        // preview, so report them with the settings that decide the outcome. Without this the
        // rejection is silent and the user cannot tell which value to change.
        var pairedIds = merged.SelectMany(candidate => candidate.SourceSegmentIds).ToHashSet();
        var unpaired = rails.Count(rail => !rail.SourceIds.Any(pairedIds.Contains));
        if (unpaired > 0)
            warnings.Add($"{unpaired} đường biên không ghép được thành dầm. "
                + $"Kiểm tra Min Line ({options.MinimumLineLengthMm:0}), "
                + $"Gap Join ({options.GapJoinToleranceMm:0}), "
                + $"Text Search ({options.TextSearchDistanceMm:0}) và bề rộng "
                + $"{options.MinimumWidthMm:0}–{options.MaximumWidthMm:0} mm.");
        var anchor = beamPackage.SourceAnchor * scale - origin;
        return new CadBeamAnalysis(origin, anchor, merged, shortLines, warnings, null);
    }

    private static IReadOnlyList<Rail> BuildRails(
        IReadOnlyList<CadStructureSegment> segments,
        double gapToleranceMm,
        double railOffsetToleranceMm)
    {
        var pieces = segments.Select(segment =>
        {
            var vector = segment.End - segment.Start;
            var length = Length(vector);
            var direction = CanonicalDirection(vector * (1.0 / length));
            var normal = new CadStructurePoint2(-direction.Y, direction.X);
            var offset = Dot(segment.Start, normal);
            var start = Dot(segment.Start, direction);
            var end = Dot(segment.End, direction);
            if (start > end) (start, end) = (end, start);
            return new Piece(segment, direction, normal, offset, start, end);
        }).ToArray();

        // Cluster by actual distance rather than by rounding the offset into fixed cells: two
        // pieces of one drawn boundary must stay together even when their offsets fall either
        // side of a cell edge, which would otherwise split a beam over a millimetre of drift.
        var groups = pieces
            .GroupBy(piece => (int)Math.Round(Angle(piece.Direction) / RailAngleBucketDegrees))
            .SelectMany(family =>
            {
                var clusters = new List<List<Piece>>();
                foreach (var piece in family.OrderBy(item => item.Offset))
                {
                    var current = clusters.Count == 0 ? null : clusters[^1];
                    if (current is not null
                        && piece.Offset - current[^1].Offset <= railOffsetToleranceMm)
                        current.Add(piece);
                    else
                        clusters.Add(new List<Piece> { piece });
                }
                return clusters;
            })
            .ToArray();

        return groups.Select((group, index) =>
        {
            var direction = Normalize(new CadStructurePoint2(
                group.Average(piece => piece.Direction.X),
                group.Average(piece => piece.Direction.Y)));
            var normal = new CadStructurePoint2(-direction.Y, direction.X);
            var offset = group.Average(piece =>
                (Dot(piece.Segment.Start, normal) + Dot(piece.Segment.End, normal)) / 2.0);
            var intervals = MergeIntervals(group.Select(piece =>
            {
                var a = Dot(piece.Segment.Start, direction);
                var b = Dot(piece.Segment.End, direction);
                return new Interval(Math.Min(a, b), Math.Max(a, b));
            }), gapToleranceMm);
            var sources = group.Select(piece =>
            {
                var a = Dot(piece.Segment.Start, direction);
                var b = Dot(piece.Segment.End, direction);
                return new RailSource(piece.Segment.Id, Math.Min(a, b), Math.Max(a, b));
            }).ToArray();
            return new Rail(index + 1, direction, normal, offset, intervals,
                group.Select(piece => piece.Segment.Id).Distinct().ToArray(), sources);
        }).ToArray();
    }

    private static IReadOnlyList<CadBeamCandidate> PairRails(
        IReadOnlyList<Rail> rails,
        IReadOnlyList<CadStructureSegment> grids,
        IReadOnlyList<CadStructureAnnotation> annotations,
        CadBeamAnalysisOptions options)
    {
        var pairs = new List<ScoredPair>();
        var railFamilies = rails.GroupBy(rail =>
            (int)Math.Round(Angle(rail.Direction) / AngleToleranceDegrees));
        foreach (var family in railFamilies)
        {
            var orderedRails = family.OrderBy(rail => rail.Offset).ToArray();
            for (var firstIndex = 0; firstIndex < orderedRails.Length; firstIndex++)
            {
            // BuildRails has already collapsed each cluster of near-equal offsets to one rail. Starting at
            // MinimumWidth avoids rescanning dense duplicate/near-duplicate rails; the remaining
            // search is bounded by the configured width window, not total family size.
            var secondIndex = LowerBoundOffset(
                orderedRails, firstIndex + 1, orderedRails[firstIndex].Offset + options.MinimumWidthMm);
            for (; secondIndex < orderedRails.Length; secondIndex++)
            {
            var first = orderedRails[firstIndex];
            var second = orderedRails[secondIndex];
            var width = Math.Abs(first.Offset - second.Offset);
            if (width > options.MaximumWidthMm) break;
            if (width < options.MinimumWidthMm || width > options.MaximumWidthMm) continue;

            var facings = FacingIntervals(first, second, options.GapJoinToleranceMm);
            // Fragments that each look solid on their own can still be too sparse to be one beam.
            // Measuring across the whole facing stretch keeps the gap tolerance meaningful: raising
            // it bridges the fragments, lowering it leaves them below the coverage gate.
            var span = facings.Count == 0
                ? default
                : new Interval(facings.Min(item => item.Start), facings.Max(item => item.End));
            var spanLength = facings.Count == 0 ? 0.0 : span.End - span.Start;
            var spanValid = spanLength <= 0.0
                            || (CoveredWithin(first, span) / spanLength >= options.MinimumRailCoverageRatio
                                && CoveredWithin(second, span) / spanLength >= options.MinimumRailCoverageRatio);
            if (!spanValid) continue;

            foreach (var facing in facings)
            {
            var start = facing.Start;
            var end = facing.End;
            var extent = end - start;
            if (extent < options.MinimumLineLengthMm) continue;
            var firstCoverage = CoveredWithin(first, facing) / extent;
            var secondCoverage = CoveredWithin(second, facing) / extent;
            var centerOffset = (first.Offset + second.Offset) / 2.0;
            var grid = FindGridAxis(grids, first.Direction, centerOffset);
            var onGrid = grid is not null;
            if (grid is not null) centerOffset = grid.Value.Offset;
            var coverageValid = firstCoverage >= options.MinimumRailCoverageRatio
                                && secondCoverage >= options.MinimumRailCoverageRatio;
            if (!coverageValid) continue;

            (start, end) = SnapEndpointStations(
                grids, first.Direction, first.Normal, centerOffset, start, end);
            var axisStart = first.Direction * start + first.Normal * centerOffset;
            var axisEnd = first.Direction * end + first.Normal * centerOffset;
            var matches = MatchAnnotations(annotations, axisStart, axisEnd, options.TextSearchDistanceMm);
            var best = matches.OrderBy(match => Math.Abs(match.WidthMm - width))
                .ThenBy(match => match.Score).FirstOrDefault();
            var candidate = CreateCandidate(axisStart, axisEnd, width, best, onGrid,
                SourceIdsWithin(first, facing).Concat(SourceIdsWithin(second, facing)).Distinct().ToArray());
            var widthPenalty = best is null ? 500.0 : Math.Abs(best.WidthMm - width);
            var gridBonus = onGrid ? -100.0 : 0.0;
            var coveragePenalty = (2.0 - firstCoverage - secondCoverage) * 1000.0;
            var baseScore = gridBonus + coveragePenalty;
            pairs.Add(new ScoredPair(candidate, widthPenalty + baseScore, matches, baseScore));
            }
            }
            }
        }

        pairs = AssignAnnotationOwnership(pairs).ToList();

        // Parallel lines that are not beam boundaries -- a wall face beside a beam, the far side
        // of an adjacent room, a short stub between two beams -- still satisfy the width and
        // coverage gates and would surface as candidates no annotation claims. Those cannot be
        // created and only clutter the review, so a section has to come from a nearby label.
        // Pairs are kept when nothing in the selection carries a section at all, since then the
        // whole scan is unlabelled and the review is the user's only way to see that.
        if (pairs.Any(pair => !string.IsNullOrEmpty(pair.Candidate.MatchedText)))
            pairs = pairs
                .Where(pair => !string.IsNullOrEmpty(pair.Candidate.MatchedText))
                .ToList();

        // Competing pairs may overlap on an axis, while a real section-width transition produces
        // adjacent, non-overlapping pairs. Retain the best compatible interval set rather than one
        // pair for the whole axis, otherwise a 200 -> 300 width transition loses half the run.
        var selected = pairs
            .GroupBy(item => AxisKey(item.Candidate))
            .SelectMany(SelectCompatiblePairs)
            .ToArray();

        var kept = RemoveEnvelopePairs(selected);
        return ExtendTrimmedEnds(kept, rails, options.MaximumRunGapMm)
            .SelectMany(pair => ExpandSections(pair, grids))
            .ToArray();
    }

    /// <summary>
    /// Drops pairs formed from the outer boundaries of two neighbouring beams. When a stub meets a
    /// longer beam, the stub's far rail can also pair with the long beam's far rail and yield a
    /// candidate as wide as both beams together. Such a pair straddles a narrower pair that has an
    /// annotation, so it is an envelope of real beams rather than a beam of its own.
    /// </summary>
    private static IReadOnlyList<ScoredPair> RemoveEnvelopePairs(IReadOnlyList<ScoredPair> pairs) =>
        pairs.Where(pair => !pairs.Any(inner => IsEnvelopeOf(pair, inner))).ToArray();

    private static bool IsEnvelopeOf(ScoredPair outer, ScoredPair inner)
    {
        if (ReferenceEquals(outer, inner)) return false;
        if (inner.Candidate.GeometryWidthMm >= outer.Candidate.GeometryWidthMm - SectionToleranceMm)
            return false;
        if (string.IsNullOrEmpty(inner.Candidate.Mark) && !string.IsNullOrEmpty(outer.Candidate.Mark))
            return false;

        var outerDirection = Normalize(outer.Candidate.EndMm - outer.Candidate.StartMm);
        var innerDirection = Normalize(inner.Candidate.EndMm - inner.Candidate.StartMm);
        if (Math.Abs(Dot(CanonicalDirection(outerDirection), CanonicalDirection(innerDirection)))
            < Math.Cos(AngleToleranceDegrees * Math.PI / 180.0)) return false;

        var normal = new CadStructurePoint2(-outerDirection.Y, outerDirection.X);
        var separation = Math.Abs(Dot(inner.Candidate.StartMm, normal) - Dot(outer.Candidate.StartMm, normal));
        if (separation > (outer.Candidate.GeometryWidthMm - inner.Candidate.GeometryWidthMm) / 2.0
            + SectionToleranceMm) return false;

        var overlap = InteriorOverlap(outer.Candidate, inner.Candidate);
        return overlap >= inner.Candidate.LengthMm - SectionToleranceMm;
    }

    /// <summary>
    /// Restores the length of a beam whose boundaries were trimmed to different stations. A pair
    /// only spans the stretch both boundaries share, so a beam whose top boundary stops early at a
    /// column face comes out short. Where one boundary continues alone and no other beam claims
    /// that stretch, the beam really does run on, so the end is pushed out to it. A stretch that a
    /// neighbouring beam already occupies is left alone -- that is a second beam sharing the rail,
    /// not this one continuing.
    /// </summary>
    private static IReadOnlyList<ScoredPair> ExtendTrimmedEnds(
        IReadOnlyList<ScoredPair> pairs,
        IReadOnlyList<Rail> rails,
        double maximumRunGapMm)
    {
        return pairs.Select(pair =>
        {
            var direction = Normalize(pair.Candidate.EndMm - pair.Candidate.StartMm);
            var normal = new CadStructurePoint2(-direction.Y, direction.X);
            var offset = Dot(pair.Candidate.StartMm, normal);
            var start = Dot(pair.Candidate.StartMm, direction);
            var end = Dot(pair.Candidate.EndMm, direction);

            var half = pair.Candidate.GeometryWidthMm / 2.0;
            var reach = rails.Where(rail =>
                AngleDifference(rail.Direction, direction) <= AngleToleranceDegrees
                && Math.Abs(Math.Abs(rail.Offset - offset) - half) <= SectionToleranceMm).ToArray();
            if (reach.Length == 0) return pair;

            // Follow the surviving boundary only while its geometry keeps up: step out interval by
            // interval and stop at the first break wider than a run may span. Reaching straight to
            // the rail's far end would jump a gap that separates two beams.
            var covered = MergeIntervals(
                reach.SelectMany(rail => rail.Intervals), maximumRunGapMm);
            var touching = covered.FirstOrDefault(interval =>
                Math.Min(interval.End, end) - Math.Max(interval.Start, start) > SectionToleranceMm);
            if (touching.End <= touching.Start) return pair;
            var grown = new Interval(
                Math.Min(start, touching.Start),
                Math.Max(end, touching.End));

            foreach (var other in pairs)
            {
                if (ReferenceEquals(other, pair)) continue;
                var otherStart = Dot(other.Candidate.StartMm, direction);
                var otherEnd = Dot(other.Candidate.EndMm, direction);
                if (otherStart > otherEnd) (otherStart, otherEnd) = (otherEnd, otherStart);
                if (Math.Min(otherEnd, grown.End) - Math.Max(otherStart, grown.Start)
                    <= SectionToleranceMm) continue;
                // Another beam sits in the stretch we would grow into: stop at its boundary.
                if (otherEnd <= start + SectionToleranceMm) grown = new Interval(
                    Math.Max(grown.Start, otherEnd), grown.End);
                else if (otherStart >= end - SectionToleranceMm) grown = new Interval(
                    grown.Start, Math.Min(grown.End, otherStart));
                else return pair;
            }

            if (grown.End - grown.Start <= end - start + SectionToleranceMm) return pair;
            var origin = pair.Candidate.StartMm - direction * start;
            return pair with
            {
                Candidate = pair.Candidate with
                {
                    StartMm = origin + direction * grown.Start,
                    EndMm = origin + direction * grown.End
                }
            };
        }).ToArray();
    }

    private static IReadOnlyList<ScoredPair> SelectCompatiblePairs(IEnumerable<ScoredPair> source)
    {
        var accepted = new List<ScoredPair>();
        foreach (var pair in source.OrderBy(item => item.Score)
                     .ThenByDescending(item => item.Candidate.LengthMm))
        {
            if (accepted.Any(item => InteriorOverlap(item.Candidate, pair.Candidate) > SectionToleranceMm))
                continue;
            accepted.Add(pair);
        }
        return accepted;
    }

    private static double InteriorOverlap(CadBeamCandidate first, CadBeamCandidate second)
    {
        var direction = Normalize(first.EndMm - first.StartMm);
        // Only beams along the same line compete for a stretch. Projecting a branch that meets
        // this beam at an angle onto this axis would read as overlap and discard the beam its
        // branches hang off, leaving the branches and losing the run they share a node with.
        var secondDirection = Normalize(second.EndMm - second.StartMm);
        if (Math.Abs(Dot(CanonicalDirection(direction), CanonicalDirection(secondDirection)))
            < Math.Cos(AngleToleranceDegrees * Math.PI / 180.0)) return 0.0;
        var firstStart = Math.Min(Dot(first.StartMm, direction), Dot(first.EndMm, direction));
        var firstEnd = Math.Max(Dot(first.StartMm, direction), Dot(first.EndMm, direction));
        var secondStart = Math.Min(Dot(second.StartMm, direction), Dot(second.EndMm, direction));
        var secondEnd = Math.Max(Dot(second.StartMm, direction), Dot(second.EndMm, direction));
        return Math.Max(0.0, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));
    }

    private static IReadOnlyList<AnnotationMatch> FilterMatchesForGeometry(
        IReadOnlyList<AnnotationMatch> matches,
        double geometryWidth)
    {
        if (matches.Count <= 1) return matches;
        var closestDelta = matches.Min(match => Math.Abs(match.WidthMm - geometryWidth));
        var widthMatches = matches.Where(match =>
                Math.Abs(match.WidthMm - geometryWidth) <= closestDelta + SectionToleranceMm)
            .ToArray();
        var closestPerpendicular = widthMatches.Min(match => match.PerpendicularDistanceMm);
        return widthMatches.Where(match =>
                match.PerpendicularDistanceMm <= closestPerpendicular + TextOwnershipBandToleranceMm)
            .ToArray();
    }

    private static IReadOnlyList<ScoredPair> AssignAnnotationOwnership(
        IReadOnlyList<ScoredPair> pairs)
    {
        var owners = pairs
            .SelectMany(pair => pair.Matches.Select(match => new { Pair = pair, Match = match }))
            .GroupBy(item => item.Match.Annotation.Id)
            .ToDictionary(
                group => group.Key,
                group => AxisKey(group.OrderBy(item =>
                        item.Match.Score
                        + Math.Abs(item.Match.WidthMm - item.Pair.Candidate.GeometryWidthMm) * 10.0)
                    .ThenBy(item => item.Pair.Score)
                    .First().Pair.Candidate));

        return pairs.Select(pair =>
        {
            var axisKey = AxisKey(pair.Candidate);
            var owned = FilterMatchesForGeometry(pair.Matches
                .Where(match => owners.TryGetValue(match.Annotation.Id, out var owner)
                                && owner == axisKey)
                .ToArray(), pair.Candidate.GeometryWidthMm);
            var best = owned.OrderBy(match => Math.Abs(match.WidthMm - pair.Candidate.GeometryWidthMm))
                .ThenBy(match => match.Score).FirstOrDefault();
            var candidate = CreateCandidate(
                pair.Candidate.StartMm, pair.Candidate.EndMm, pair.Candidate.GeometryWidthMm,
                best, pair.Candidate.ReconstructedOnGridAxis, pair.Candidate.SourceSegmentIds);
            var widthPenalty = best is null
                ? 500.0
                : Math.Abs(best.WidthMm - pair.Candidate.GeometryWidthMm);
            return pair with
            {
                Candidate = candidate,
                Score = pair.BaseScore + widthPenalty,
                Matches = owned
            };
        }).ToArray();
    }

    private static IReadOnlyList<CadBeamCandidate> ExpandSections(
        ScoredPair pair,
        IReadOnlyList<CadStructureSegment> grids)
    {
        if (pair.Matches.Count == 0) return new[] { pair.Candidate };
        var ordered = pair.Matches.OrderBy(match => match.Station).ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (Math.Abs(ordered[index].Station - ordered[index - 1].Station) > 100.0) continue;
            if (SameSection(ordered[index], ordered[index - 1])) continue;
            return new[] { pair.Candidate with { Status = CadBeamCandidateStatus.AmbiguousText } };
        }

        var zones = new List<AnnotationMatch>();
        foreach (var match in ordered)
        {
            if (zones.Count > 0 && SameSection(zones[^1], match)) continue;
            zones.Add(match);
        }
        if (zones.Count == 1)
            return new[] { CreateCandidate(pair.Candidate.StartMm, pair.Candidate.EndMm,
                pair.Candidate.GeometryWidthMm, zones[0], pair.Candidate.ReconstructedOnGridAxis,
                pair.Candidate.SourceSegmentIds) };

        var direction = Normalize(pair.Candidate.EndMm - pair.Candidate.StartMm);
        var length = pair.Candidate.LengthMm;
        var result = new List<CadBeamCandidate>();
        for (var index = 0; index < zones.Count; index++)
        {
            var zoneStart = index == 0
                ? 0.0
                : SnapTransitionStation(pair.Candidate, grids,
                    (zones[index - 1].Station + zones[index].Station) / 2.0);
            var zoneEnd = index == zones.Count - 1
                ? length
                : SnapTransitionStation(pair.Candidate, grids,
                    (zones[index].Station + zones[index + 1].Station) / 2.0);
            result.Add(CreateCandidate(
                pair.Candidate.StartMm + direction * zoneStart,
                pair.Candidate.StartMm + direction * zoneEnd,
                pair.Candidate.GeometryWidthMm,
                zones[index],
                pair.Candidate.ReconstructedOnGridAxis,
                pair.Candidate.SourceSegmentIds));
        }
        return result;
    }

    private static double SnapTransitionStation(
        CadBeamCandidate candidate,
        IReadOnlyList<CadStructureSegment> grids,
        double proposedStation)
    {
        var direction = Normalize(candidate.EndMm - candidate.StartMm);
        var stations = new List<double>();
        foreach (var grid in grids)
        {
            var gridVector = grid.End - grid.Start;
            var denominator = Cross(direction, gridVector);
            if (Math.Abs(denominator) < 1e-9) continue;
            var station = Cross(grid.Start - candidate.StartMm, gridVector) / denominator;
            if (station > SectionToleranceMm && station < candidate.LengthMm - SectionToleranceMm)
                stations.Add(station);
        }
        if (stations.Count == 0) return proposedStation;
        var closest = stations.OrderBy(station => Math.Abs(station - proposedStation)).First();
        return Math.Abs(closest - proposedStation) <= EndpointSnapToleranceMm
            ? closest
            : proposedStation;
    }

    private static int LowerBoundOffset(Rail[] rails, int startIndex, double targetOffset)
    {
        var low = startIndex;
        var high = rails.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (rails[middle].Offset < targetOffset) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static CadBeamCandidate CreateCandidate(
        CadStructurePoint2 start,
        CadStructurePoint2 end,
        double geometryWidth,
        AnnotationMatch? match,
        bool onGrid,
        IReadOnlyList<int> sourceIds)
    {
        var status = match is null
            ? CadBeamCandidateStatus.MissingText
            : Math.Abs(match.WidthMm - geometryWidth) > SectionToleranceMm
                ? CadBeamCandidateStatus.TextWidthMismatch
                : CadBeamCandidateStatus.Ready;
        return new CadBeamCandidate(
            0, start, end, geometryWidth, match?.WidthMm, match?.HeightMm,
            geometryWidth, match?.HeightMm ?? 0.0, match?.Mark ?? string.Empty,
            match?.Annotation.Text ?? string.Empty, status, onGrid, sourceIds,
            match is null ? Array.Empty<int>() : new[] { match.Annotation.Id });
    }

    private static bool SameSection(AnnotationMatch first, AnnotationMatch second) =>
        Math.Abs(first.WidthMm - second.WidthMm) <= SectionToleranceMm
        && Math.Abs(first.HeightMm - second.HeightMm) <= SectionToleranceMm;

    private static IReadOnlyList<CadBeamCandidate> MergeRuns(
        IReadOnlyList<CadBeamCandidate> candidates,
        double maximumRunGapMm)
    {
        var result = new List<CadBeamCandidate>();
        foreach (var group in candidates.GroupBy(AxisKey))
        {
            var seed = group.First();
            var direction = Normalize(seed.EndMm - seed.StartMm);
            var ordered = group.OrderBy(candidate => Math.Min(
                Dot(candidate.StartMm, direction), Dot(candidate.EndMm, direction))).ToArray();
            foreach (var candidate in ordered)
            {
                if (result.Count == 0 || AxisKey(result[^1]) != AxisKey(candidate)
                    || Math.Abs(result[^1].EffectiveWidthMm - candidate.EffectiveWidthMm) > SectionToleranceMm
                    || Math.Abs(result[^1].EffectiveHeightMm - candidate.EffectiveHeightMm) > SectionToleranceMm)
                {
                    result.Add(candidate);
                    continue;
                }
                var previous = result[^1];
                // A break wider than the configured maximum reads as two beams that happen to
                // share an axis and a section, not as one beam interrupted by a support.
                var gap = Dot(candidate.StartMm, direction) - Dot(previous.EndMm, direction);
                if (gap > maximumRunGapMm)
                {
                    result.Add(candidate);
                    continue;
                }
                result[^1] = previous with
                {
                    EndMm = candidate.EndMm,
                    SourceSegmentIds = previous.SourceSegmentIds.Concat(candidate.SourceSegmentIds).Distinct().ToArray(),
                    SourceAnnotationIds = previous.SourceAnnotationIds.Concat(candidate.SourceAnnotationIds).Distinct().ToArray()
                };
            }
        }
        return result;
    }

    private static GridAxis? FindGridAxis(
        IReadOnlyList<CadStructureSegment> grids,
        CadStructurePoint2 direction,
        double centerOffset)
    {
        GridAxis? best = null;
        foreach (var grid in grids)
        {
            var vector = grid.End - grid.Start;
            if (Length(vector) < 1.0) continue;
            var gridDirection = CanonicalDirection(Normalize(vector));
            if (AngleDifference(gridDirection, direction) > AngleToleranceDegrees) continue;
            var normal = new CadStructurePoint2(-direction.Y, direction.X);
            var offset = (Dot(grid.Start, normal) + Dot(grid.End, normal)) / 2.0;
            var distance = Math.Abs(offset - centerOffset);
            if (distance > GridAxisSnapToleranceMm || best is not null && best.Value.Distance <= distance) continue;
            best = new GridAxis(offset, distance);
        }
        return best;
    }

    private static (double Start, double End) SnapEndpointStations(
        IReadOnlyList<CadStructureSegment> grids,
        CadStructurePoint2 direction,
        CadStructurePoint2 normal,
        double centerOffset,
        double start,
        double end)
    {
        var origin = normal * centerOffset;
        var stations = new List<double>();
        foreach (var grid in grids)
        {
            var gridVector = grid.End - grid.Start;
            var denominator = Cross(direction, gridVector);
            if (Math.Abs(denominator) < 1e-9) continue;
            var station = Cross(grid.Start - origin, gridVector) / denominator;
            stations.Add(station);
        }
        var startSnap = stations.Count == 0
            ? start
            : stations.OrderBy(station => Math.Abs(station - start)).First();
        var endSnap = stations.Count == 0
            ? end
            : stations.OrderBy(station => Math.Abs(station - end)).First();
        if (Math.Abs(startSnap - start) > EndpointSnapToleranceMm) startSnap = start;
        if (Math.Abs(endSnap - end) > EndpointSnapToleranceMm) endSnap = end;
        return startSnap < endSnap ? (startSnap, endSnap) : (start, end);
    }

    private static IReadOnlyList<AnnotationMatch> MatchAnnotations(
        IReadOnlyList<CadStructureAnnotation> annotations,
        CadStructurePoint2 start,
        CadStructurePoint2 end,
        double searchDistanceMm)
    {
        var direction = Normalize(end - start);
        var length = start.DistanceTo(end);
        var matches = new List<AnnotationMatch>();
        foreach (var annotation in annotations)
        {
            var parsed = ParseSection(annotation.Text);
            if (parsed is null) continue;
            var relative = annotation.Position - start;
            var station = Dot(relative, direction);
            if (station < -searchDistanceMm || station > length + searchDistanceMm) continue;
            var perpendicular = Math.Abs(Cross(relative, direction));
            if (perpendicular > searchDistanceMm) continue;
            var longitudinalPenalty = station < 0 ? -station : station > length ? station - length : 0.0;
            // A label is written along the beam it names. Where beams meet, several are within
            // reach of the same label, and without this the one running across it can win on
            // distance alone and leave the beam the label belongs to with no section at all.
            var annotationRadians = annotation.RotationDegrees * Math.PI / 180.0;
            var annotationDirection = new CadStructurePoint2(
                Math.Cos(annotationRadians), Math.Sin(annotationRadians));
            var alignment = Math.Abs(Dot(
                CanonicalDirection(annotationDirection), CanonicalDirection(direction)));
            var orientationPenalty = (1.0 - alignment) * searchDistanceMm;
            matches.Add(new AnnotationMatch(annotation, parsed.Value.Width, parsed.Value.Height,
                parsed.Value.Mark, Math.Max(0.0, Math.Min(length, station)),
                perpendicular + longitudinalPenalty * 0.5 + orientationPenalty, perpendicular));
        }
        return matches.OrderBy(match => match.Score).ToArray();
    }

    private static (double Width, double Height, string Mark)? ParseSection(string text)
    {
        var match = SectionRegex.Match(text);
        if (!match.Success) return null;
        if (!TryInvariant(match.Groups["b"].Value, out var width)
            || !TryInvariant(match.Groups["h"].Value, out var height)
            || width <= 0 || height <= 0) return null;
        // A mark never contains a semicolon, so anything up to the last one is a formatting code
        // that survived normalisation rather than part of the name.
        var prefix = text[..match.Index];
        var lastControl = prefix.LastIndexOf(';');
        if (lastControl >= 0) prefix = prefix[(lastControl + 1)..];
        var mark = prefix.Trim(' ', '-', '(', '[', ':', '_');
        return (width, height, mark);
    }

    /// <summary>
    /// Rejoins a label drawn as two pieces of text. Plans often carry the name and the section as
    /// separate entities so each can be placed or styled on its own, which leaves the section text
    /// with no name in front of it and the beam with a blank mark. A name sitting beside a section,
    /// on the same line and close enough to read as one label, is folded into it.
    /// </summary>
    private static CadStructureAnnotation[] JoinSplitLabels(CadStructureAnnotation[] annotations)
    {
        var sections = annotations
            .Where(annotation => SectionRegex.IsMatch(annotation.Text))
            .ToArray();
        if (sections.Length == 0) return annotations;

        var names = annotations
            .Where(annotation => !SectionRegex.IsMatch(annotation.Text)
                                 && MarkRegex.IsMatch(annotation.Text))
            .ToArray();
        if (names.Length == 0) return annotations;

        var consumed = new HashSet<int>();
        var joined = sections.Select(section =>
        {
            if (!string.IsNullOrEmpty(ParseSection(section.Text)?.Mark)) return section;
            var radians = section.RotationDegrees * Math.PI / 180.0;
            var along = new CadStructurePoint2(Math.Cos(radians), Math.Sin(radians));
            var best = names
                .Where(name => !consumed.Contains(name.Id)
                               && AngleDifference(
                                   new CadStructurePoint2(
                                       Math.Cos(name.RotationDegrees * Math.PI / 180.0),
                                       Math.Sin(name.RotationDegrees * Math.PI / 180.0)),
                                   along) <= AngleToleranceDegrees)
                .Select(name => new
                {
                    Name = name,
                    Along = Dot(name.Position - section.Position, along),
                    Across = Math.Abs(Cross(name.Position - section.Position, along))
                })
                .Where(item => item.Across <= SplitLabelAcrossToleranceMm
                               && item.Along < 0
                               && -item.Along <= SplitLabelAlongToleranceMm)
                .OrderBy(item => -item.Along)
                .FirstOrDefault();
            if (best is null) return section;
            consumed.Add(best.Name.Id);
            // Separate the two pieces unless the section already starts with a separator, so a
            // name running straight into the digits cannot be read as part of the width.
            var name = best.Name.Text.Trim();
            var separator = section.Text.Length > 0 && !char.IsLetterOrDigit(section.Text[0])
                ? string.Empty
                : "-";
            return section with { Text = name + separator + section.Text };
        }).ToArray();

        return annotations
            .Where(annotation => !SectionRegex.IsMatch(annotation.Text)
                                 && !consumed.Contains(annotation.Id))
            .Concat(joined)
            .ToArray();
    }

    private static string NormalizeText(string value)
    {
        var normalized = MTextControlRegex.Replace(value, string.Empty);
        return normalized.Replace("\\P", " ")
            .Replace("\\p", " ")
            .Replace("{", string.Empty)
            .Replace("}", string.Empty)
            .Trim();
    }

    private static IReadOnlyList<Interval> MergeIntervals(IEnumerable<Interval> source, double gap)
    {
        var ordered = source.OrderBy(item => item.Start).ToArray();
        if (ordered.Length == 0) return Array.Empty<Interval>();
        var merged = new List<Interval> { ordered[0] };
        foreach (var current in ordered.Skip(1))
        {
            var last = merged[^1];
            if (current.Start <= last.End + gap)
                merged[^1] = new Interval(last.Start, Math.Max(last.End, current.End));
            else
                merged.Add(current);
        }
        return merged;
    }

    /// <summary>
    /// Stretches of an axis where a pair of rails can form beams. A rail collects every collinear
    /// boundary in the drawing, so a short stub sharing a line with a long beam would otherwise
    /// inherit the long beam's extent. Stations covered by either rail stay in one stretch, which
    /// keeps staggered fragments and interior gaps as a single continuous beam; a stretch ends
    /// only where neither rail has geometry, which is what separates unrelated beams.
    /// </summary>
    private static IReadOnlyList<Interval> FacingIntervals(Rail first, Rail second, double gap)
    {
        // The two boundaries of one beam rarely stop at the same station: one gets trimmed at a
        // column face while the other runs on. Ending the beam where the shorter rail stops would
        // cut it short, so a stretch runs as far as either boundary reaches and the coverage gate
        // in the caller decides whether that stretch is really one beam.
        var shared = new Interval(
            Math.Max(first.Start, second.Start),
            Math.Min(first.End, second.End));
        if (shared.End <= shared.Start) return Array.Empty<Interval>();
        var windows = new List<Interval>();
        foreach (var window in MergeIntervals(first.Intervals.Concat(second.Intervals), gap))
        {
            var start = Math.Max(window.Start, shared.Start);
            var end = Math.Min(window.End, shared.End);
            if (end <= start) continue;
            var clamped = new Interval(start, end);
            if (!first.Intervals.Any(interval => Overlaps(interval, clamped))
                || !second.Intervals.Any(interval => Overlaps(interval, clamped))) continue;
            windows.Add(clamped);
        }
        return windows;
    }

    private static double RailStartWithin(Rail rail, Interval window) =>
        rail.Intervals.Where(interval => Overlaps(interval, window))
            .Select(interval => interval.Start).DefaultIfEmpty(window.Start).Min();

    private static double RailEndWithin(Rail rail, Interval window) =>
        rail.Intervals.Where(interval => Overlaps(interval, window))
            .Select(interval => interval.End).DefaultIfEmpty(window.End).Max();

    private static bool Overlaps(Interval first, Interval second) =>
        Math.Min(first.End, second.End) - Math.Max(first.Start, second.Start) > 0.0;

    private static double CoveredWithin(Rail rail, Interval window) =>
        rail.Intervals.Sum(interval => Math.Max(
            0.0,
            Math.Min(interval.End, window.End) - Math.Max(interval.Start, window.Start)));

    private static IReadOnlyList<int> SourceIdsWithin(Rail rail, Interval window) =>
        rail.Sources
            .Where(source => Math.Min(source.End, window.End)
                             - Math.Max(source.Start, window.Start) > 0.0)
            .Select(source => source.SegmentId)
            .Distinct()
            .ToArray();

    private static string AxisKey(CadBeamCandidate candidate)
    {
        var direction = CanonicalDirection(Normalize(candidate.EndMm - candidate.StartMm));
        var normal = new CadStructurePoint2(-direction.Y, direction.X);
        var offset = Dot(candidate.StartMm, normal);
        return $"{Math.Round(Angle(direction) / AngleToleranceDegrees)}:{Math.Round(offset / GridAxisSnapToleranceMm)}";
    }

    private static string? Validate(CadStructureTransferPackage package, CadBeamAnalysisOptions options)
    {
        if (package.SchemaVersion != CadStructureTransferPackage.CurrentSchemaVersion)
            return $"Schema CAD {package.SchemaVersion} không được hỗ trợ.";
        if (package.Segments.Count > CadStructureAnalyzer.MaximumSegmentCount)
            return $"Vùng chọn Beam vượt {CadStructureAnalyzer.MaximumSegmentCount:N0} segment.";
        if (!Finite(options.MinimumLineLengthMm) || options.MinimumLineLengthMm < 0)
            return "Min Line không hợp lệ.";
        if (!Finite(options.GapJoinToleranceMm)
            || options.GapJoinToleranceMm < 0 || options.GapJoinToleranceMm > 2000)
            return "Gap Join phải nằm trong khoảng 0–2000 mm.";
        if (!Finite(options.TextSearchDistanceMm) || options.TextSearchDistanceMm < 0)
            return "Text Search không hợp lệ.";
        if (options.MinimumRailCoverageRatio is < 0 or > 1)
            return "Minimum Rail Coverage phải nằm trong khoảng 0–1.";
        // A tolerance approaching the narrowest beam would merge the two boundaries of that beam
        // into one rail and lose it entirely, so keep it well below the minimum width.
        if (!Finite(options.RailOffsetToleranceMm) || options.RailOffsetToleranceMm < 0
            || options.RailOffsetToleranceMm >= options.MinimumWidthMm / 2.0)
            return "Rail Offset phải nhỏ hơn nửa bề rộng dầm nhỏ nhất.";
        return null;
    }

    private static CadBeamAnalysis Invalid(string error) => new(
        default, default, Array.Empty<CadBeamCandidate>(), 0, Array.Empty<string>(), error);

    private static bool TryInvariant(string value, out double result) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out result);

    private static bool Finite(CadStructurePoint2 point) => Finite(point.X) && Finite(point.Y);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static double Dot(CadStructurePoint2 first, CadStructurePoint2 second) =>
        first.X * second.X + first.Y * second.Y;
    private static double Cross(CadStructurePoint2 first, CadStructurePoint2 second) =>
        first.X * second.Y - first.Y * second.X;
    private static double Length(CadStructurePoint2 value) => Math.Sqrt(Dot(value, value));
    private static CadStructurePoint2 Normalize(CadStructurePoint2 value)
    {
        var length = Length(value);
        return length <= 1e-12 ? new CadStructurePoint2(1, 0) : value * (1.0 / length);
    }
    private static CadStructurePoint2 CanonicalDirection(CadStructurePoint2 direction) =>
        direction.X < -1e-12 || Math.Abs(direction.X) <= 1e-12 && direction.Y < 0
            ? direction * -1
            : direction;
    private static double Angle(CadStructurePoint2 direction)
    {
        var angle = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;
        while (angle < 0) angle += 180.0;
        while (angle >= 180) angle -= 180.0;
        return angle;
    }
    private static double AngleDifference(CadStructurePoint2 first, CadStructurePoint2 second)
    {
        var difference = Math.Abs(Angle(first) - Angle(second));
        return Math.Min(difference, 180.0 - difference);
    }

    private sealed record Piece(
        CadStructureSegment Segment,
        CadStructurePoint2 Direction,
        CadStructurePoint2 Normal,
        double Offset,
        double Start,
        double End);

    private sealed record Rail(
        int Id,
        CadStructurePoint2 Direction,
        CadStructurePoint2 Normal,
        double Offset,
        IReadOnlyList<Interval> Intervals,
        IReadOnlyList<int> SourceIds,
        IReadOnlyList<RailSource> Sources)
    {
        public double Start => Intervals.Min(interval => interval.Start);
        public double End => Intervals.Max(interval => interval.End);
        public double CoveredLength => Intervals.Sum(interval => interval.End - interval.Start);
    }

    private readonly record struct RailSource(int SegmentId, double Start, double End);

    private readonly record struct Interval(double Start, double End);
    private readonly record struct GridAxis(double Offset, double Distance);
    private sealed record AnnotationMatch(
        CadStructureAnnotation Annotation,
        double WidthMm,
        double HeightMm,
        string Mark,
        double Station,
        double Score,
        double PerpendicularDistanceMm);
    private sealed record ScoredPair(
        CadBeamCandidate Candidate,
        double Score,
        IReadOnlyList<AnnotationMatch> Matches,
        double BaseScore);
}
