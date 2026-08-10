using System.Globalization;
using System.Text.RegularExpressions;
using RevitAPP.Core.Models.CadStructure;

namespace RevitAPP.Core.Services;

/// <summary>
/// Turns a scanned slab plan into the floors it describes.
///
/// The plan states a thickness and a level in each bay and leaves the rest to convention: a cross
/// means an opening, a hatch means the slab drops, and a beam between two bays does not divide the
/// pour. Cells are therefore only an intermediate step -- what gets created is the merged area of
/// every cell sharing an elevation and a thickness.
/// </summary>
public static class CadSlabAnalyzer
{
    private const double ElevationToleranceMm = 1.0;
    private const double ThicknessToleranceMm = 1.0;

    // An elevation is written with a sign or three decimals: +0.000, -0.050, 0.000. A thickness is
    // a whole number of millimetres. Keeping the two patterns apart is what stops -0.100 being
    // read as a 100 mm slab.
    // A level is written in metres with at most two whole digits: +0.000, -0.050, -1.500. Allowing
    // more digits let a dimension standing next to the label bleed into the number, so 1818 beside
    // -0.100 read as +81818.100.
    private static readonly Regex ElevationRegex = new(
        @"(?<![\d.,])(?<sign>[+\-±])?\s*(?<value>\d{1,2}[.,]\d{2,3})(?![\d.,])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LabelledThicknessRegex = new(
        @"(?:hs|h|s)\s*[=:]?\s*(?<value>\d{2,4})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BareThicknessRegex = new(
        @"^\s*(?<value>\d{2,4})\s*(?:mm)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex MTextControlRegex = new(
        @"\\[ACFHQTWp][^;]*;|\\[LlOoKk]|(?<=^|[;}])[ACFHQTWpxt][\d.,]+;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static CadSlabAnalysis Analyze(
        CadStructureTransferPackage slabPackage,
        IReadOnlyList<CadHatchRegion> hatches,
        CadSlabAnalysisOptions? options = null)
    {
        options ??= new CadSlabAnalysisOptions();
        var validation = Validate(options);
        if (validation is not null) return Invalid(validation);

        double scale;
        try
        {
            scale = CadGridUnitConverter.MillimetresPerDrawingUnit(slabPackage.InsUnits);
        }
        catch (InvalidDataException exception)
        {
            return Invalid(exception.Message);
        }

        var scaled = slabPackage.Segments
            .Where(segment => Finite(segment.Start) && Finite(segment.End))
            .Select(segment => segment with
            {
                Start = segment.Start * scale,
                End = segment.End * scale
            })
            .ToArray();
        if (scaled.Length == 0) return Invalid("Vùng chọn Sàn không có LINE/POLYLINE hợp lệ.");

        var origin = new CadStructurePoint2(
            scaled.Min(segment => Math.Min(segment.Start.X, segment.End.X)),
            scaled.Min(segment => Math.Min(segment.Start.Y, segment.End.Y)));
        var segments = scaled
            .Select(segment => segment with
            {
                Start = segment.Start - origin,
                End = segment.End - origin
            })
            .ToArray();

        // A short piece that joins two longer ones closes a bay: trimming at every column face
        // leaves plenty of them. Dropping a piece for its length alone loses the whole region, so
        // only a piece touching nothing else is noise -- a tick, a witness line, a stray mark.
        var usable = segments
            .Where(segment => segment.Start.DistanceTo(segment.End) >= options.MinimumLineLengthMm
                              || TouchesAnother(segment, segments, options.VertexSnapToleranceMm))
            .ToArray();
        var shortLines = segments.Length - usable.Length;

        var annotations = slabPackage.Annotations
            .Where(annotation => Finite(annotation.Position))
            .Select(annotation => annotation with
            {
                Position = annotation.Position * scale - origin,
                Text = NormalizeText(annotation.Text)
            })
            .Where(annotation => !string.IsNullOrWhiteSpace(annotation.Text))
            .ToArray();

        var hatchRegions = hatches
            .Select(hatch => hatch with
            {
                BoundaryMm = hatch.BoundaryMm.Select(point => point * scale - origin).ToArray()
            })
            .Where(hatch => hatch.BoundaryMm.Count >= 3)
            .ToArray();

        // The outlines the user selected are closed in their own right, so they are traced on
        // their own: the shape of a hole comes from the outline drawn around it, not from how the
        // slab lines happen to divide the floor.
        var openingOutlines = options.OpeningOutlinesMm
            .Where(segment => Finite(segment.Start) && Finite(segment.End))
            .Select(segment => segment with
            {
                Start = segment.Start * scale - origin,
                End = segment.End * scale - origin
            })
            .ToArray();
        var marks = openingOutlines.Length == 0
            ? Array.Empty<CadSlabLoop>()
            : CadPlanarGraph.BuildFaces(openingOutlines, options.VertexSnapToleranceMm, out _);

        var faces = CadPlanarGraph.BuildFaces(usable, options.VertexSnapToleranceMm, out var unclosed);
        // A shaded area bounds the pour just as a drawn line does: the slab drops where the shading
        // ends, whether or not a line runs there. Where the plan shades part of a bay -- or two
        // parts of one bay -- the lines alone give a single face and the shading inside it is lost,
        // so those bays are cut along their shading before anything is read from them.
        faces = SplitAlongHatches(faces, hatchRegions, options);
        var cells = ClassifyCells(faces, usable, annotations, hatchRegions, marks, options);
        var regions = MergeCells(cells, marks, usable, options, hatchRegions);
        // The cells as the regions saw them, after labels were carried between neighbours -- which
        // is what the levels below have to be read from, not the cells before that step.
        var regionCells = SpreadLabelsAcrossNeighbours(
            cells.Where(cell => !cell.IsOpening && !cell.IsColumn).ToArray(), cells);

        var warnings = new List<string>();
        if (shortLines > 0)
            warnings.Add($"Đã bỏ qua {shortLines} line ngắn hơn {options.MinimumLineLengthMm:0} mm.");
        if (unclosed > 0)
            warnings.Add($"{unclosed} đầu line chưa khép thành vùng. "
                + $"Tăng Vertex Snap ({options.VertexSnapToleranceMm:0} mm) nếu biên bị hở.");
        var orphanHatches = hatchRegions.Count(hatch =>
            !cells.Any(cell => ContainsPoint(hatch.BoundaryMm, cell.CentroidMm)));
        if (hatchRegions.Length == 0 && slabPackage.Segments.Count > 0)
            warnings.Add("Không đọc được vùng hatch nào — kiểm tra vùng quét có chứa hatch không.");
        else if (orphanHatches > 0)
            warnings.Add($"{orphanHatches}/{hatchRegions.Length} vùng hatch không khớp ô sàn nào.");
        if (regions.Count == 0)
            warnings.Add("Không dựng được vùng sàn kín nào từ line đã quét.");

        var styles = cells
            .Select(cell => cell.HatchStyleKey)
            .Where(key => !string.IsNullOrEmpty(key))
            .Distinct()
            .OrderBy(key => key)
            .ToArray();
        if (styles.Length > 1)
            warnings.Add($"Bản vẽ dùng {styles.Length} kiểu hatch — mỗi kiểu là một mức sàn riêng.");

        // Which levels were read, and from how many bays. When a slab comes out at a level the plan
        // does not state, this says whether the label was never read, or read and then outvoted.
        var readLevels = regionCells
            .Where(cell => cell.ElevationMm is not null)
            .GroupBy(cell => (Elevation: cell.ElevationMm!.Value,
                Shaded: !string.IsNullOrEmpty(cell.HatchStyleKey)))
            .OrderBy(group => group.Key.Shaded)
            .ThenBy(group => group.Key.Elevation)
            .Select(group => $"{group.Key.Elevation / 1000.0:+0.000;-0.000}"
                + $" ({(group.Key.Shaded ? "hatch" : "thường")}, {group.Count()} ô)")
            .ToArray();
        warnings.Add(readLevels.Length == 0
            ? "Không đọc được cao độ nào từ text — kiểm tra text có nằm trong vùng quét không."
            : "Cao độ đọc được: " + string.Join(", ", readLevels) + ".");

        var readThicknesses = regionCells
            .Where(cell => cell.ThicknessMm is not null)
            .GroupBy(cell => cell.ThicknessMm!.Value)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key:0} ({group.Count()} ô)")
            .ToArray();
        if (readThicknesses.Length > 0)
            warnings.Add("Chiều dày đọc được: " + string.Join(", ", readThicknesses) + " mm.");

        // Where each hole came from. A floor is cut for an outline the user picked and for a pour
        // laid at another level, and for nothing else -- so a count that does not add up says the
        // cut came from somewhere it should not have.
        var holeCount = regions.Sum(region => region.Holes.Count);
        if (holeCount > 0 || marks.Count > 0)
        {
            var loweredCount = regions.Count(region => region.IsLowered);
            warnings.Add($"Lỗ khoét: {holeCount} (ô pick: {marks.Count}, sàn hạ: {loweredCount}). "
                + "Sàn chỉ khoét cho ô pick và sàn hạ — số khác là bất thường.");
        }


        var anchor = slabPackage.SourceAnchor * scale - origin;
        return new CadSlabAnalysis(origin, anchor, regions, cells,
            shortLines, unclosed, orphanHatches, warnings, null)
        {
            HatchStyles = styles
        };
    }

    /// <summary>
    /// Cuts each bay the plan shades only part of into the shaded pieces and what is left over. A
    /// bay whose shading the lines already trace is left as it is.
    /// </summary>
    private static IReadOnlyList<CadSlabLoop> SplitAlongHatches(
        IReadOnlyList<CadSlabLoop> faces,
        IReadOnlyList<CadHatchRegion> hatches,
        CadSlabAnalysisOptions options)
    {
        if (hatches.Count == 0) return faces;

        var result = new List<CadSlabLoop>();
        foreach (var face in faces)
        {
            // A hatch clipped to the bay: it may straddle a line and shade part of the next bay
            // too, and only the part falling in this one divides it.
            var inside = hatches
                .Where(hatch => !SameArea(face, hatch.BoundaryMm))
                .Where(hatch => hatch.BoundaryMm.Any(point =>
                    ContainsPoint(face.VerticesMm, point)))
                .Select(hatch => hatch.BoundaryMm)
                .ToArray();
            if (inside.Length == 0)
            {
                result.Add(face);
                continue;
            }

            var pieces = CadPlanarGraph.Subdivide(
                face.VerticesMm, inside, options.VertexSnapToleranceMm);
            // The cut has to stay within the bay, or the shading crosses its edge in a way the cut
            // cannot express and the bay is safer left whole.
            if (pieces.Count < 2
                || pieces.Sum(piece => piece.AreaMm2) > face.AreaMm2 * 1.05)
            {
                result.Add(face);
                continue;
            }

            // A cell is a single closed area with no holes of its own. What the shading leaves is a
            // ring, which no cell can hold, so it is left out here: the floor's outline is traced
            // from the lines regardless of the cells, and what the shading covers is cut out of it
            // as a hole further on. Keeping the ring's whole edge instead would lay it over the
            // shaded pieces and pour the same ground twice.
            result.AddRange(pieces
                .Where(piece => piece.Holes.Count == 0)
                .Select(piece => piece.Outer));
        }
        return result;
    }

    /// <summary>
    /// Whether two outlines cover the same ground, to the tolerance a drawing is worked to.
    /// </summary>
    private static bool SameArea(CadSlabLoop face, IReadOnlyList<CadStructurePoint2> other)
    {
        var area = new CadSlabLoop(other.ToArray()).AreaMm2;
        return Math.Abs(face.AreaMm2 - area) <= Math.Max(area, 1.0) * 0.05;
    }

    private static IReadOnlyList<CadSlabCell> ClassifyCells(
        IReadOnlyList<CadSlabLoop> faces,
        IReadOnlyList<CadStructureSegment> segments,
        IReadOnlyList<CadStructureAnnotation> annotations,
        IReadOnlyList<CadHatchRegion> hatches,
        IReadOnlyList<CadSlabLoop> marks,
        CadSlabAnalysisOptions options)
    {
        var cells = new List<CadSlabCell>();
        for (var index = 0; index < faces.Count; index++)
        {
            var face = faces[index];
            var centroid = Centroid(face);
            var inside = annotations
                .Where(annotation => ContainsPoint(face.VerticesMm, annotation.Position))
                .ToArray();

            var thickness = ReadThickness(inside, options);
            var elevation = ReadElevation(inside, options);
            var hatch = hatches.FirstOrDefault(item => ContainsPoint(item.BoundaryMm, centroid));
            var lowered = hatch is not null;
            // An opening is what the user selected an outline around. Guessing one from a cross in
            // the drawing read marks that were never openings and missed the ones drawn another
            // way, and there is nothing to guess once the outlines are picked.
            var opening = MarkedAsOpening(face, marks);
            var text = inside.Length > 0 ? string.Join(" ", inside.Select(item => item.Text)) : string.Empty;

            cells.Add(new CadSlabCell(index + 1, face, SourceIdsWithin(segments, face))
            {
                ThicknessMm = thickness.Value,
                ElevationMm = elevation.Value,
                IsOpening = opening,
                IsLowered = lowered,
                HatchStyleKey = hatch?.StyleKey ?? string.Empty,
                IsBeamStrip = IsNarrowStrip(face, options.MaximumBeamStripWidthMm),
                IsColumn = IsColumnFootprint(face, options.MaximumColumnSizeMm),
                MatchedText = text
            });
        }
        return cells;
    }

    /// <summary>
    /// A cross drawn corner to corner marks a shaft or a stairwell, which is left open rather than
    /// poured. Reading the mark from the geometry keeps it working whatever layer it sits on.
    /// </summary>
    /// <summary>
    /// Whether this bay lies inside an outline the user selected around an open area.
    /// </summary>
    private static bool MarkedAsOpening(
        CadSlabLoop face,
        IReadOnlyList<CadSlabLoop> outlines)
    {
        var centre = Centroid(face);
        return outlines.Any(outline => ContainsPoint(outline.VerticesMm, centre));
    }

    private static (double? Value, bool Ambiguous) ReadThickness(
        IReadOnlyList<CadStructureAnnotation> annotations,
        CadSlabAnalysisOptions options)
    {
        var values = new List<double>();
        foreach (var annotation in annotations)
        {
            var labelled = LabelledThicknessRegex.Match(annotation.Text);
            if (labelled.Success && TryInvariant(labelled.Groups["value"].Value, out var withLabel))
            {
                values.Add(withLabel);
                continue;
            }
            // A bare number is only a thickness inside the configured range: a plan carries grid
            // spacings and dimensions that would otherwise be read as slabs.
            var bare = BareThicknessRegex.Match(annotation.Text);
            if (!bare.Success) continue;
            if (!TryInvariant(bare.Groups["value"].Value, out var plain)) continue;
            if (plain < options.MinimumThicknessMm || plain > options.MaximumThicknessMm) continue;
            values.Add(plain);
        }

        var distinct = values.Distinct().ToArray();
        if (distinct.Length == 0) return (null, false);
        if (distinct.Length > 1) return (distinct[0], true);
        return (distinct[0], false);
    }

    private static (double? Value, bool Ambiguous) ReadElevation(
        IReadOnlyList<CadStructureAnnotation> annotations,
        CadSlabAnalysisOptions options)
    {
        var values = new List<double>();
        foreach (var annotation in annotations)
        {
            var match = ElevationRegex.Match(annotation.Text);
            if (!match.Success) continue;
            if (!TryInvariant(match.Groups["value"].Value, out var metres)) continue;
            var sign = match.Groups["sign"].Value == "-" ? -1.0 : 1.0;
            var millimetres = sign * metres * 1000.0;
            // A floor drops by a step, a screed or a storey. A reading of a millimetre or two came
            // from a number that is not a level at all, and taking it splits a floor the plan
            // shows as one into pieces at levels it never states.
            if (millimetres != 0.0 && Math.Abs(millimetres) < options.MinimumElevationStepMm)
                continue;
            values.Add(millimetres);
        }

        var distinct = values.Distinct().ToArray();
        if (distinct.Length == 0) return (null, false);
        if (distinct.Length > 1) return (distinct[0], true);
        return (distinct[0], false);
    }

    /// <summary>
    /// Groups cells that share an elevation and a thickness, then rebuilds the boundary of each
    /// group. A slab is poured across the beams inside it, so the shared edges disappear and the
    /// group becomes one floor which may be L-shaped or carry holes.
    /// </summary>
    private static IReadOnlyList<CadSlabRegionCandidate> MergeCells(
        IReadOnlyList<CadSlabCell> cells,
        IReadOnlyList<CadSlabLoop> marks,
        IReadOnlyList<CadStructureSegment> usableLines,
        CadSlabAnalysisOptions options,
        IReadOnlyList<CadHatchRegion>? hatches = null)
    {
        // Neither an opening nor a column takes concrete, so both stay out of the pour and become
        // holes in the slab that surrounds them.
        var poured = cells.Where(cell => !cell.IsOpening && !cell.IsColumn).ToArray();

        // A beam drawn by both its faces leaves a narrow strip between two bays. The pour runs
        // across it, so the strip takes the elevation and thickness of the bays it separates
        // instead of standing as a slab of its own with no label of its own.
        var resolved = poured.Select(cell =>
        {
            if (!cell.IsBeamStrip || cell.ThicknessMm is not null) return cell;
            var host = NearestLabelledNeighbour(cell, poured);
            return host is null
                ? cell
                : cell with { ThicknessMm = host.ThicknessMm, ElevationMm = host.ElevationMm };
        }).ToArray();

        // A plan writes the section once in a bay and leaves the cells the lines carve out of it
        // unlabelled, so the label has to reach them before grouping. Without this the labelled
        // cell forms a slab on its own and everything around it falls back to the defaults.
        // Spreading walks between neighbouring cells, and the cells taking concrete are not always
        // connected to each other: a stair core can separate one part of a floor from another. The
        // marked cells are passed in so a label can travel across them without pouring into them.
        resolved = SpreadLabelsAcrossNeighbours(resolved, cells);

        // Whatever a label could not reach still belongs to the pour around it, so a connected run
        // of bays with one stated level takes that level throughout. Leaving the rest on the
        // default would break a floor apart at bays the plan never marked.
        resolved = resolved
            .GroupBy(cell => cell.HatchStyleKey)
            .SelectMany(group => ConnectedParts(group.ToArray()))
            .SelectMany(FillFromStatedValues)
            .ToArray();
        // Spreading runs over the cells that take concrete, and openings and columns were already
        // excluded from that set, so nothing here can revive them as slabs.

        // The scan is one floor: trace its outside edge once, over every cell together. What sits
        // inside that edge and takes no concrete -- a hatched area, a bay the user picked, a stair
        // core, a column -- is then cut out of it as a hole.
        // A column is cast with the floor around it, so it is neither a hole nor a break in the
        // edge. Tracing over the columns would leave a saw-tooth outline with a sliver of slab
        // beside every one of them.
        var poursAndInteriorVoids = cells.Where(cell => !cell.IsColumn).ToArray();
        // The outline comes from the lines themselves: every line the user scanned, joined and
        // walked round the outside. Assembling it from the bays instead let a column notch the
        // edge and a stair core cut the floor in two.
        // A grid axis runs past the building to carry its bubble, so it would drag the outline out
        // with it. Only lines bounding a bay describe the floor, and those are the ones the cells
        // were built from.
        // A shaded area drawn clear of the floor -- a lowered bay off to one side -- is a pour of
        // its own, not part of this one. Taking its lines into the outline pulled the edge out to
        // reach it and left a spur of slab where the plan shows none.
        // A lowered bay is a slab of its own laid beside the floor, so the floor's edge stops where
        // the shading starts. Tracing over the shaded bays as well pushed the edge out to cover
        // them, and the floor was then poured on top of the very slabs it should sit beside.
        // A column is cast with the floor round it, so it neither breaks the run of cells nor
        // notches the edge. Leaving the columns out left the edge running round every one of them
        // and gave the floor a saw-tooth outline.
        var plainBlock = LargestConnectedRun(cells
            .Where(cell => !cell.IsLowered || cell.IsColumn)
            .ToArray());
        var plainEdge = plainBlock.Length == 0
            ? null
            : CadPlanarGraph.BuildOuterBoundary(
                usableLines
                    .Where(line => plainBlock.SelectMany(cell => cell.SourceSegmentIds)
                        .ToHashSet().Contains(line.Id))
                    .ToArray(),
                options.VertexSnapToleranceMm);
        // A shaded bay the floor surrounds is a hole in it, so the edge still runs round the
        // outside of it. One hanging off the floor is a slab laid beside it, and taking that into
        // the edge poured the floor over the very slab it should sit next to.
        var mainBlock = plainEdge is null
            ? LargestConnectedRun(cells)
            : LargestConnectedRun(cells
                .Where(cell => !cell.IsLowered
                               || cell.IsColumn
                               || ContainsPoint(plainEdge.VerticesMm, cell.CentroidMm))
                .ToArray());
        var boundaryLineIds = mainBlock
            .SelectMany(cell => cell.SourceSegmentIds)
            .ToHashSet();
        var outline = CadPlanarGraph.BuildOuterBoundary(
            usableLines.Where(line => boundaryLineIds.Contains(line.Id)).ToArray(),
            options.VertexSnapToleranceMm,
            options.MaximumColumnSizeMm);
        var totalBoundary = outline is null
            ? null
            : new GroupBoundary(outline, Array.Empty<CadSlabLoop>());
        if (totalBoundary is not null)
        {
            var plainCells = resolved
                .Where(cell => string.IsNullOrEmpty(cell.HatchStyleKey))
                .ToArray();
            // Unshaded floor is one pour at one level, whatever the plan writes where. It states
            // the level in a bay or two and leaves the rest to follow, so a bay a label never
            // reached is not a section of its own -- reading it as one broke the floor into a slab
            // per label. The level comes from the largest run of bays that agree on it.
            // The level comes from the bays the plan actually labels, weighed by how much floor
            // they cover. Reading it from the largest group of bays instead let the unlabelled ones
            // -- which are always the many -- outvote the label and hold the floor at the default.
            var statedElevation = plainCells
                .Where(cell => cell.ElevationMm is not null)
                .GroupBy(cell => Math.Round(cell.ElevationMm!.Value / ElevationToleranceMm))
                .OrderByDescending(group => group.Sum(cell => cell.Loop.AreaMm2))
                .FirstOrDefault()
                ?.First().ElevationMm;
            var statedThickness = plainCells
                .Where(cell => cell.ThicknessMm is not null)
                .GroupBy(cell => Math.Round(cell.ThicknessMm!.Value / ThicknessToleranceMm))
                .OrderByDescending(group => group.Sum(cell => cell.Loop.AreaMm2))
                .FirstOrDefault()
                ?.First().ThicknessMm;
            var plain = plainCells
                .Select(cell => cell with
                {
                    ThicknessMm = cell.ThicknessMm ?? statedThickness,
                    ElevationMm = cell.ElevationMm ?? statedElevation
                })
                .ToArray();
            // Unshaded floor at one level is still more than one slab when the plan leaves real
            // ground between its parts -- two wings of a building, a floor either side of a well.
            // The largest run keeps the floor's edge and the rest are slabs standing beside it.
            // Bays on either side of a shaded area are still one floor: the shading is a slab laid
            // into it, not ground between two of them. So the runs are found over the floor with
            // its shaded bays included, and only the unshaded cells of each run are kept.
            var plainIds = plain.Select(cell => cell.Id).ToHashSet();
            var plainRuns = ConnectedParts(plain.Concat(resolved
                    .Where(cell => !plainIds.Contains(cell.Id)))
                    .ToArray())
                .Select(run => run.Where(cell => plainIds.Contains(cell.Id)).ToArray())
                .Where(run => run.Length > 0)
                .OrderByDescending(run => run.Sum(cell => cell.Loop.AreaMm2))
                .ToArray();
            var detachedRuns = plainRuns.Skip(1).ToArray();
            if (plainRuns.Length > 0) plain = plainRuns[0];
            var otherSections = Array.Empty<IGrouping<(double, double), CadSlabCell>>();
            if (plain.Length > 0)
            {
                // Each hatched area is poured at its own level, so it becomes a slab beside the
                // floor rather than part of it.
                // Areas hatched alike join when only a beam runs between them and stay apart when
                // the plan leaves real ground between them, so they are joined by proximity as
                // well as by touching.
                var hatchedRegions = resolved
                    .Where(cell => !string.IsNullOrEmpty(cell.HatchStyleKey))
                    .GroupBy(cell => cell.HatchStyleKey)
                    .SelectMany(group => JoinNearbyParts(
                        ConnectedParts(group.ToArray()), options.HatchJoinDistanceMm))
                    .Select((part, index) => BuildRegion(part, index + 2, cells, marks, options, hatches))
                    .Where(region => region is not null)
                    .Select(region => region!)
                    .ToArray();

                // A plain area the plan gives another level is a slab inside the floor, exactly
                // like a hatched one, so it is built the same way rather than by tracing a second
                // outside edge.
                var otherRegions = detachedRuns
                    .Select((part, index) =>
                        BuildRegion(part, index + 2 + hatchedRegions.Length, cells, marks, options))
                    .Where(region => region is not null)
                    .Select(region => region!)
                    .ToArray();


                var outerEdge = totalBoundary.Outer;
                var cutOut = poursAndInteriorVoids
                    .Where(cell => !plain.Any(member => member.Id == cell.Id))
                    .Where(cell => ContainsPoint(outerEdge.VerticesMm, cell.CentroidMm))
                    .ToArray();

                // A picked outline states the hole exactly; the cells under it would repeat the
                // same void in the shape the slab lines happen to give it.
                var markedCells = cutOut
                    .Where(cell => marks.Any(mark =>
                        ContainsPoint(mark.VerticesMm, cell.CentroidMm)))
                    .Select(cell => cell.Id)
                    .ToHashSet();
                // A lowered pour is cut out of the floor along its own edge, which is the edge it
                // was built with. Rebuilding the hole from the cells underneath gave a shape that
                // did not match the slab laid into it, so the floor was left open where the lowered
                // slab did not reach.
                var pouredElsewhere = hatchedRegions
                    .Concat(otherRegions)
                    .Select(region => region.OuterLoop)
                    .ToArray();
                var coveredCells = cutOut
                    .Where(cell => pouredElsewhere.Any(pour =>
                        ContainsPoint(pour.VerticesMm, cell.CentroidMm)))
                    .Select(cell => cell.Id)
                    .ToHashSet();
                // A pour reaching past the floor is cut back to it first: only the stretch lying on
                // the floor is a hole in it. Passing the whole pour left the hole reaching outside
                // the edge, where it was dropped as invalid, and the floor stayed poured underneath.
                var poursOnTheFloor = pouredElsewhere
                    .Select(pour => ClipToOutline(pour, outerEdge, options))
                    .Where(pour => pour is not null)
                    .Select(pour => pour!)
                    .ToArray();
                // The floor is cut only for what the plan says is not poured: an outline the user
                // picked, and a pour laid at another level. A bay left over for any other reason --
                // a column, a shaft the lines happen to enclose, a bay whose edge did not close --
                // is concrete, and cutting it left the slab riddled with holes the plan never shows.
                var wholeFloorHoles = marks
                    .Where(mark => ContainsPoint(outerEdge.VerticesMm, Centroid(mark)))
                    .Concat(poursOnTheFloor)
                    .Select(hole => Orient(hole, counterClockwise: false))
                    .Where(hole => hole.AreaMm2 >= 10_000.0)
                    .ToArray();
                // A void reaching the floor's edge is not a hole in it but a bite out of it: Revit
                // refuses a profile whose loops touch, so the edge is cut back round such a void
                // instead of a hole being left against it.
                var edgeVoids = wholeFloorHoles
                    .Where(hole => TouchesLoop(hole, outerEdge))
                    .ToArray();
                if (edgeVoids.Length > 0)
                {
                    var bitten = CutBackFrom(outerEdge, edgeVoids, options);
                    if (bitten is not null)
                    {
                        outerEdge = bitten;
                        wholeFloorHoles = wholeFloorHoles.Except(edgeVoids).ToArray();
                    }
                }
                wholeFloorHoles = SeparateHoles(wholeFloorHoles, outerEdge).ToArray();

                // The floor takes the level most of it is drawn at, not whichever labelled bay came
                // first. One bay reading differently -- a stray label, a bay the shading marks --
                // decided the whole pour when the first was taken, so a floor of eighty-eight bays
                // at one level was built at the level of the odd one out.
                var labelledCell = plain[0] with
                {
                    ElevationMm = MostOfTheFloor(plain, cell => cell.ElevationMm),
                    ThicknessMm = MostOfTheFloor(plain, cell => cell.ThicknessMm),
                    MatchedText = plain
                        .Select(cell => cell.MatchedText)
                        .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty
                };

                var wholeFloor = new CadSlabRegionCandidate(
                    1,
                    Orient(outerEdge, counterClockwise: true),
                    wholeFloorHoles,
                    plain.Select(cell => cell.Id).ToArray(),
                    plain.SelectMany(cell => cell.SourceSegmentIds).Distinct().ToArray(),
                    labelledCell.ThicknessMm,
                    labelledCell.ElevationMm,
                    EffectiveThickness(labelledCell, options),
                    EffectiveElevation(labelledCell, options),
                    ResolveStatus(plain),
                    plain.Select(cell => cell.MatchedText)
                        .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty)
                {
                    AbsorbedStripCount = plain.Count(cell => cell.IsBeamStrip)
                };

                return new[] { wholeFloor }
                    .Concat(hatchedRegions)
                    .Concat(otherRegions)
                    .ToArray();
            }
        }

        var groups = resolved
            .GroupBy(cell => cell.HatchStyleKey)
            .ToArray();

        var regions = new List<CadSlabRegionCandidate>();
        var id = 1;
        // Cells sharing a hatch need not touch: a hatched area can sit between two parts of the
        // same pour and leave them on either side of it. Each connected part is a slab of its own.
        // A part is then split again only where the plan states two levels or two thicknesses
        // within it, which is a real division rather than an artefact of the grid.
        foreach (var members in groups
                     .SelectMany(group => ConnectedParts(group.ToArray()))
                     .SelectMany(part => part
                         .GroupBy(cell => (
                             Elevation: Math.Round(EffectiveElevation(cell, options) / ElevationToleranceMm),
                             Thickness: Math.Round(EffectiveThickness(cell, options) / ThicknessToleranceMm)))
                         .SelectMany(section => ConnectedParts(section.ToArray()))))
        {
            // The outside edge comes from the pour together with the voids inside it: a stair core
            // does not push the edge of the floor inwards, it makes a hole in it. Leaving the
            // marked cells out of this step would shrink the slab to the shape around them.
            var enclosed = members
                .Concat(cells.Where(cell => cell.IsOpening && TouchesAnyCell(cell, members)))
                .ToArray();
            var boundary = BuildGroupBoundary(enclosed);
            if (boundary is null) continue;
            var outer = boundary.Outer;
            if (outer.AreaMm2 / 1_000_000.0 < options.MinimumRegionAreaM2) continue;

            // A column is cast with the floor around it, so nothing is cut for it. The only voids
            // are the areas the user picked, handled below.
            var voidCells = Array.Empty<CadSlabCell>();
            // An outline the user selected is the hole, exactly as drawn. Rebuilding it from the
            // bays it covers would follow the slab lines crossing it instead of its own shape.
            var selectedHoles = marks
                .Where(mark => ContainsPoint(outer.VerticesMm, Centroid(mark)))
                .ToArray();
            var holes = SeparateHoles(boundary.Holes
                .Concat(selectedHoles)
                .Concat(MergeVoids(voidCells))
                .Select(hole => Orient(hole, counterClockwise: false))
                // A sliver left where beams cross is not a hole worth cutting, and Revit rejects a
                // profile carrying a loop that small.
                .Where(hole => hole.AreaMm2 >= 10_000.0)
                .ToArray(), outer);

            // A plan labels a bay, not every cell the lines carve out of it, so the group takes
            // the values from whichever of its cells carries the label. Reading them from an
            // arbitrary member left a slab of a hundred cells with no section at all.
            var labelled = members.FirstOrDefault(cell => cell.ThicknessMm is not null)
                           ?? members.FirstOrDefault(cell => cell.ElevationMm is not null)
                           ?? members[0];
            var thickness = EffectiveThickness(labelled, options);
            var elevation = EffectiveElevation(labelled, options);
            var status = ResolveStatus(members);

            regions.Add(new CadSlabRegionCandidate(
                id++,
                outer,
                holes,
                members.Select(cell => cell.Id).ToArray(),
                members.SelectMany(cell => cell.SourceSegmentIds).Distinct().ToArray(),
                labelled.ThicknessMm,
                labelled.ElevationMm,
                thickness,
                elevation,
                status,
                members.Select(cell => cell.MatchedText)
                    .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty)
            {
                IsLowered = members.Any(cell => cell.IsLowered),
                AbsorbedStripCount = members.Count(cell => cell.IsBeamStrip)
            });
        }

        return regions;
    }

    /// <summary>
    /// Carries a label from the cell that holds it to the cells around it, stopping at a cell that
    /// carries a label of its own or that a hatch marks differently. A bay is drawn as many cells
    /// once beams and openings cut through it, and the plan labels the bay once.
    /// </summary>
    private static CadSlabCell[] SpreadLabelsAcrossNeighbours(
        CadSlabCell[] cells,
        IReadOnlyList<CadSlabCell> allCells)
    {
        // Walk over every cell, including the voids, so a label reaches the far side of a stair
        // core. Only the cells that take concrete are returned, so the voids gain nothing from it.
        var pouredIds = cells.Select(cell => cell.Id).ToHashSet();
        var walk = cells
            .Concat(allCells.Where(cell => !pouredIds.Contains(cell.Id)))
            .ToArray();
        var adjacency = BuildAdjacency(walk);
        var result = walk.ToArray();
        var pending = new Queue<(int At, int From)>();
        for (var index = 0; index < result.Length; index++)
            if (result[index].ThicknessMm is not null || result[index].ElevationMm is not null)
                pending.Enqueue((index, index));

        while (pending.Count > 0)
        {
            var (index, from) = pending.Dequeue();
            var source = result[from];
            foreach (var neighbour in adjacency[index])
            {
                var target = result[neighbour];
                // Each value travels on its own: a bay can state a thickness while its level was
                // rejected as implausible, and it still belongs to the pour around it.
                var takesThickness = target.ThicknessMm is null && source.ThicknessMm is not null;
                var takesElevation = target.ElevationMm is null && source.ElevationMm is not null;
                if (!takesThickness && !takesElevation) continue;
                // A level written on one kind of pour says nothing about the other. Shading marks a
                // slab in its own right, so its label stays inside it and the floor's label stays
                // outside it. A bay the label cannot reach without crossing that edge takes the
                // level of the floor it belongs to instead, which is settled once the bays are
                // grouped -- carrying the label through the shading gave bays beyond it the level
                // of the drop laid into the floor.
                if (!string.Equals(target.HatchStyleKey, source.HatchStyleKey, StringComparison.Ordinal))
                    continue;
                result[neighbour] = target with
                {
                    ThicknessMm = takesThickness ? source.ThicknessMm : target.ThicknessMm,
                    ElevationMm = takesElevation ? source.ElevationMm : target.ElevationMm,
                    MatchedText = string.IsNullOrEmpty(target.MatchedText)
                        ? source.MatchedText
                        : target.MatchedText
                };
                pending.Enqueue((neighbour, neighbour));
            }
        }
        return result.Where(cell => pouredIds.Contains(cell.Id)).ToArray();
    }

    /// <summary>
    /// Which cells touch. Two bays of one pour rarely meet along matching edges: a beam or an
    /// opening on one side splits the shared boundary into pieces, so the edges overlap without
    /// being equal. Cells therefore count as neighbours when their edges run along each other,
    /// not only when the two edges are identical.
    /// </summary>
    private static List<int>[] BuildAdjacency(IReadOnlyList<CadSlabCell> cells)
    {
        var adjacency = new List<int>[cells.Count];
        for (var index = 0; index < adjacency.Length; index++) adjacency[index] = new List<int>();

        var edges = cells
            .Select(cell => LoopSegments(cell.Loop).ToArray())
            .ToArray();

        for (var first = 0; first < cells.Count; first++)
        for (var second = first + 1; second < cells.Count; second++)
        {
            if (!edges[first].Any(a => edges[second].Any(b => EdgesRunTogether(a, b)))) continue;
            adjacency[first].Add(second);
            adjacency[second].Add(first);
        }
        return adjacency;
    }

    private static IEnumerable<(CadStructurePoint2 A, CadStructurePoint2 B)> LoopSegments(CadSlabLoop loop)
    {
        var vertices = loop.VerticesMm;
        for (var index = 0; index < vertices.Count; index++)
            yield return (vertices[index], vertices[(index + 1) % vertices.Count]);
    }

    /// <summary>
    /// Whether two edges lie on the same line and share a stretch of it, which is what makes the
    /// cells on either side of them neighbours.
    /// </summary>
    private static bool EdgesRunTogether(
        (CadStructurePoint2 A, CadStructurePoint2 B) first,
        (CadStructurePoint2 A, CadStructurePoint2 B) second)
    {
        var direction = first.B - first.A;
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < 1.0) return false;
        var unit = new CadStructurePoint2(direction.X / length, direction.Y / length);
        var normal = new CadStructurePoint2(-unit.Y, unit.X);

        var offset = Dot(first.A, normal);
        if (Math.Abs(Dot(second.A, normal) - offset) > 1.0) return false;
        if (Math.Abs(Dot(second.B, normal) - offset) > 1.0) return false;

        var firstStart = Dot(first.A, unit);
        var firstEnd = Dot(first.B, unit);
        if (firstStart > firstEnd) (firstStart, firstEnd) = (firstEnd, firstStart);
        var secondStart = Dot(second.A, unit);
        var secondEnd = Dot(second.B, unit);
        if (secondStart > secondEnd) (secondStart, secondEnd) = (secondEnd, secondStart);

        return Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart) > 1.0;
    }

    private sealed record GroupBoundary(CadSlabLoop Outer, IReadOnlyList<CadSlabLoop> Holes)
    {
        /// <summary>
        /// Parts of the same pour standing clear of the outer loop -- bays a beam separates.
        /// </summary>
        public IReadOnlyList<CadSlabLoop> DetachedParts { get; init; } = Array.Empty<CadSlabLoop>();
    }

    /// <summary>
    /// The outline of a merged group, read the way the plan is: the outside edge first, then the
    /// openings inside it.
    ///
    /// An edge shared by two cells of the group is interior to the pour and disappears; an edge
    /// belonging to one cell is on the outside. Stitching what remains gives one loop around the
    /// outside and one around each void within, told apart by area: the outside encloses the rest.
    /// </summary>
    private static GroupBoundary? BuildGroupBoundary(IReadOnlyList<CadSlabCell> members)
    {
        var counts = new Dictionary<(long, long, long, long), (CadStructurePoint2 A, CadStructurePoint2 B, int Count)>();
        foreach (var cell in members)
        {
            var loop = cell.Loop.VerticesMm;
            for (var index = 0; index < loop.Count; index++)
            {
                var a = loop[index];
                var b = loop[(index + 1) % loop.Count];
                var key = EdgeKey(a, b);
                counts[key] = counts.TryGetValue(key, out var found)
                    ? (found.A, found.B, found.Count + 1)
                    : (a, b, 1);
            }
        }

        var border = counts.Values.Where(edge => edge.Count == 1).ToList();
        if (border.Count < 3) return null;

        var loops = new List<CadSlabLoop>();
        while (border.Count > 0)
        {
            var chain = new List<CadStructurePoint2> { border[0].A, border[0].B };
            border.RemoveAt(0);
            var closed = false;
            while (!closed && border.Count > 0)
            {
                var tail = chain[^1];
                var next = border.FindIndex(edge =>
                    Near(edge.A, tail) || Near(edge.B, tail));
                if (next < 0) break;
                var edge = border[next];
                border.RemoveAt(next);
                chain.Add(Near(edge.A, tail) ? edge.B : edge.A);
                if (Near(chain[^1], chain[0]))
                {
                    chain.RemoveAt(chain.Count - 1);
                    closed = true;
                }
            }
            if (chain.Count >= 3) loops.Add(new CadSlabLoop(chain));
        }

        if (loops.Count == 0) return null;
        var ordered = loops.OrderByDescending(loop => loop.AreaMm2).ToArray();
        // Stitching picks up an edge in whichever direction it was drawn, so a loop can come out
        // either way round. Revit rejects a profile whose loops disagree, and a reversed outer
        // loop also reports a negative area in the review.
        var outer = Orient(ordered[0], counterClockwise: true);
        // A loop the largest one surrounds is a bay the pour runs round -- a column, a shaft --
        // and the pour is cast round it rather than cut for it. Only what the plan states is not
        // poured makes a hole, and that is decided by the caller from the outlines the user
        // picked. Cutting here as well left a hole at every column in the floor.
        var holes = Array.Empty<CadSlabLoop>();
        var detached = ordered.Skip(1)
            .Where(loop => !ContainsPoint(outer.VerticesMm, Centroid(loop)))
            .ToArray();
        return new GroupBoundary(outer, holes) { DetachedParts = detached };
    }

    /// <summary>
    /// Joins voids that touch into one outline. A stair core is drawn as several bays, and cutting
    /// each of them separately would leave lines of slab between them that no plan shows.
    /// </summary>
    /// <summary>
    /// Gives every bay of a connected run the level and thickness the plan states for it. Where a
    /// run states one value, the bays it never labelled take that value rather than a default;
    /// where it states several, the bays keep what they were given and the run divides later.
    /// </summary>
    private static IReadOnlyList<CadSlabCell> FillFromStatedValues(CadSlabCell[] part)
    {
        var elevations = part.Where(cell => cell.ElevationMm is not null)
            .Select(cell => cell.ElevationMm!.Value).Distinct().ToArray();
        var thicknesses = part.Where(cell => cell.ThicknessMm is not null)
            .Select(cell => cell.ThicknessMm!.Value).Distinct().ToArray();
        var elevation = elevations.Length == 1 ? elevations[0] : (double?)null;
        var thickness = thicknesses.Length == 1 ? thicknesses[0] : (double?)null;
        if (elevation is null && thickness is null) return part;

        return part.Select(cell => cell with
        {
            ElevationMm = cell.ElevationMm ?? elevation,
            ThicknessMm = cell.ThicknessMm ?? thickness
        }).ToArray();
    }

    /// <summary>
    /// Splits a set of cells into the parts that actually touch. Cells can share a level and a
    /// thickness while lying on either side of something poured separately, and each part is then
    /// a slab in its own right.
    /// </summary>
    /// <summary>
    /// The value most of the floor is drawn at, weighed by the ground each value covers. A bay or
    /// two reading differently does not settle the level of the pour they sit in.
    /// </summary>
    private static double? MostOfTheFloor(
        IReadOnlyList<CadSlabCell> cells,
        Func<CadSlabCell, double?> value) =>
        cells
            .Where(cell => value(cell) is not null)
            .GroupBy(cell => Math.Round(value(cell)!.Value, 3))
            .OrderByDescending(group => group.Sum(cell => cell.Loop.AreaMm2))
            .FirstOrDefault()
            ?.Key;

    /// <summary>
    /// Whether a loop reaches the edge of another: a vertex of one lands on a side of the other.
    /// Revit refuses a profile whose loops touch, so this is as bad as crossing.
    /// </summary>
    private static bool TouchesLoop(CadSlabLoop inner, CadSlabLoop outer) =>
        inner.VerticesMm.Any(point => DistanceToLoop(outer, point) <= 1.0)
        || outer.VerticesMm.Any(point => DistanceToLoop(inner, point) <= 1.0);

    /// <summary>
    /// The floor with the given voids taken out of its edge rather than left as holes against it.
    /// Null when the cut does not leave one piece the floor can be built from.
    /// </summary>
    private static CadSlabLoop? CutBackFrom(
        CadSlabLoop outline,
        IReadOnlyList<CadSlabLoop> voids,
        CadSlabAnalysisOptions options)
    {
        var pieces = CadPlanarGraph.Subdivide(
            outline.VerticesMm,
            voids.Select(area => (IReadOnlyList<CadStructurePoint2>)area.VerticesMm).ToArray(),
            options.VertexSnapToleranceMm);

        // What is left of the floor is the piece none of the voids covers, and there has to be
        // exactly one of it -- a cut leaving the floor in two says the voids were read wrongly.
        var remaining = pieces
            .Where(piece => piece.Holes.Count == 0)
            .Where(piece => !voids.Any(area =>
                ContainsPoint(area.VerticesMm, Centroid(piece.Outer))))
            .Where(piece => piece.Outer.AreaMm2 >= options.MinimumRegionAreaM2 * 1_000_000.0)
            .ToArray();
        return remaining.Length == 1 ? remaining[0].Outer : null;
    }

    /// <summary>
    /// The part of a pour that lies on the floor. A pour falling wholly inside is returned as it
    /// is; one reaching past the edge is cut back to it; one lying wholly outside gives nothing.
    /// </summary>
    private static CadSlabLoop? ClipToOutline(
        CadSlabLoop pour,
        CadSlabLoop outline,
        CadSlabAnalysisOptions options)
    {
        if (pour.VerticesMm.All(point => ContainsPoint(outline.VerticesMm, point))) return pour;
        // A pour mostly off the floor still overlaps it, and its middle can sit outside -- so the
        // overlap is what decides, not where the middle falls.
        if (!pour.VerticesMm.Any(point => ContainsPoint(outline.VerticesMm, point))
            && !outline.VerticesMm.Any(point => ContainsPoint(pour.VerticesMm, point)))
            return null;

        // Cutting the floor along the pour's sides gives the piece they share, which is the one
        // holding the pour's middle.
        var pieces = CadPlanarGraph.Subdivide(
            outline.VerticesMm, new[] { pour.VerticesMm }, options.VertexSnapToleranceMm);
        var shared = pieces
            .Where(piece => piece.Holes.Count == 0)
            .Where(piece => ContainsPoint(pour.VerticesMm, Centroid(piece.Outer))
                            && ContainsPoint(outline.VerticesMm, Centroid(piece.Outer)))
            .OrderByDescending(piece => piece.Outer.AreaMm2)
            .FirstOrDefault();
        return shared?.Outer;
    }

    /// <summary>
    /// The largest run of cells that touch one another. That is the floor; anything standing clear
    /// of it is a pour drawn beside it and describes its own edge.
    /// </summary>
    private static CadSlabCell[] LargestConnectedRun(IReadOnlyList<CadSlabCell> cells)
    {
        if (cells.Count == 0) return Array.Empty<CadSlabCell>();
        var parts = ConnectedParts(cells.ToArray());
        return parts
            .OrderByDescending(part => part.Sum(cell => cell.Loop.AreaMm2))
            .First();
    }

    private static IReadOnlyList<CadSlabCell[]> ConnectedParts(CadSlabCell[] cells)
    {
        if (cells.Length <= 1) return new[] { cells };
        var adjacency = BuildAdjacency(cells);
        var part = new int[cells.Length];
        for (var index = 0; index < part.Length; index++) part[index] = -1;
        var next = 0;

        for (var index = 0; index < cells.Length; index++)
        {
            if (part[index] >= 0) continue;
            var current = next++;
            var pending = new Queue<int>();
            pending.Enqueue(index);
            part[index] = current;
            while (pending.Count > 0)
            {
                var at = pending.Dequeue();
                foreach (var neighbour in adjacency[at])
                {
                    if (part[neighbour] >= 0) continue;
                    part[neighbour] = current;
                    pending.Enqueue(neighbour);
                }
            }
        }

        return Enumerable.Range(0, next)
            .Select(current => cells.Where((_, index) => part[index] == current).ToArray())
            .ToArray();
    }

    /// <summary>
    /// Joins parts that a beam separates. A beam drawn between two hatched bays leaves a strip of
    /// plain floor between them, but the drop runs across it and the two are poured together.
    /// Parts further apart than a beam is wide are left as they are.
    /// </summary>
    private static IReadOnlyList<CadSlabCell[]> JoinNearbyParts(
        IReadOnlyList<CadSlabCell[]> parts,
        double maximumGapMm)
    {
        if (parts.Count <= 1) return parts;
        var merged = parts.Select(part => part.ToList()).ToList();

        var joined = true;
        while (joined)
        {
            joined = false;
            for (var first = 0; first < merged.Count && !joined; first++)
            for (var second = first + 1; second < merged.Count && !joined; second++)
            {
                if (GapBetween(merged[first], merged[second]) > maximumGapMm) continue;
                merged[first].AddRange(merged[second]);
                merged.RemoveAt(second);
                joined = true;
            }
        }
        return merged.Select(part => part.ToArray()).ToArray();
    }

    /// <summary>
    /// The clear distance between two areas -- the width of the ground lying between them.
    /// Measuring vertex to vertex reads zero wherever the two share a corner, which happens at
    /// both ends of a corridor, so the whole corridor looked like no gap at all.
    /// </summary>
    private static double GapBetween(
        IReadOnlyList<CadSlabCell> first,
        IReadOnlyList<CadSlabCell> second)
    {
        var a = SpanOfCells(first);
        var b = SpanOfCells(second);
        var horizontal = Math.Max(0.0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
        var vertical = Math.Max(0.0, Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY));
        // Areas that overlap on one axis are separated only along the other, which is the width
        // of the strip between them.
        if (horizontal <= 0.0) return vertical;
        if (vertical <= 0.0) return horizontal;
        return Math.Sqrt(horizontal * horizontal + vertical * vertical);
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) SpanOfCells(
        IReadOnlyList<CadSlabCell> cells)
    {
        var points = cells.SelectMany(cell => cell.Loop.VerticesMm).ToArray();
        return (points.Min(point => point.X), points.Min(point => point.Y),
            points.Max(point => point.X), points.Max(point => point.Y));
    }

    /// <summary>
    /// The edge the plan shades for a set of cells, taken from the hatches themselves. Null when
    /// no hatch covers the cells, or when several hatches do and they cannot be traced as one edge.
    /// </summary>
    private static CadSlabLoop? HatchOutline(
        CadSlabCell[] part,
        IReadOnlyList<CadHatchRegion>? hatches)
    {
        if (hatches is null || hatches.Count == 0) return null;

        // Only cells the plan actually shades name a hatch. A neighbouring bay pulled in because a
        // beam runs between the two is part of the pour, but it must not drag in the hatch that
        // covers it -- that is how a shaded bay grew over the unshaded one beside it.
        var covering = hatches
            .Where(hatch => part.Any(cell =>
                cell.IsLowered && ContainsPoint(hatch.BoundaryMm, cell.CentroidMm)))
            .ToArray();
        if (covering.Length == 0) return null;

        // The hatch edge only stands in for the grid when it holds the whole pour. When the pour
        // reaches past the shading -- a bay joined across a beam -- the grid edge is the true one.
        if (part.Any(cell => !covering.Any(hatch =>
                ContainsPoint(hatch.BoundaryMm, cell.CentroidMm))))
            return null;

        if (covering.Length == 1)
            return new CadSlabLoop(covering[0].BoundaryMm.ToArray());

        // Shaded areas standing apart are in one pour only because a beam runs between them, so
        // the edge runs round the outside of them all and takes the beam's strip in. What separates
        // them was already judged narrow enough for that before they were put together.
        return JoinLoops(covering
            .Select(hatch => new CadSlabLoop(hatch.BoundaryMm.ToArray()))
            .ToArray());
    }

    /// <summary>
    /// A rectangle covering the strip between two shapes, square to the plan. Null when the shapes
    /// already meet, or when they stand so that no strip lies between them.
    /// </summary>
    private static IReadOnlyList<CadStructurePoint2>? GapFiller(
        CadSlabLoop first,
        CadSlabLoop second)
    {
        var a = Extent(first);
        var b = Extent(second);

        // Where the two overlap along one axis, the strip runs across the other, and the filler
        // spans the overlap so it meets both shapes squarely.
        var sharedY = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
        var sharedX = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);

        if (sharedY > 0.0)
        {
            var left = Math.Min(a.MaxX, b.MaxX);
            var right = Math.Max(a.MinX, b.MinX);
            if (right <= left) return null;
            var bottom = Math.Max(a.MinY, b.MinY);
            var top = Math.Min(a.MaxY, b.MaxY);
            return Corners(left, bottom, right, top);
        }

        if (sharedX > 0.0)
        {
            var bottom = Math.Min(a.MaxY, b.MaxY);
            var top = Math.Max(a.MinY, b.MinY);
            if (top <= bottom) return null;
            var left = Math.Max(a.MinX, b.MinX);
            var right = Math.Min(a.MaxX, b.MaxX);
            return Corners(left, bottom, right, top);
        }

        return null;
    }

    private static IReadOnlyList<CadStructurePoint2> Corners(
        double left, double bottom, double right, double top) =>
        new[]
        {
            new CadStructurePoint2(left, bottom),
            new CadStructurePoint2(right, bottom),
            new CadStructurePoint2(right, top),
            new CadStructurePoint2(left, top)
        };

    private static (double MinX, double MinY, double MaxX, double MaxY) Extent(CadSlabLoop loop) =>
        (loop.VerticesMm.Min(point => point.X), loop.VerticesMm.Min(point => point.Y),
            loop.VerticesMm.Max(point => point.X), loop.VerticesMm.Max(point => point.Y));

    /// <summary>
    /// Whether two points lie square to one another -- the line between them runs along one of the
    /// plan's own directions rather than cutting across at an angle.
    /// </summary>
    private static bool Square(CadStructurePoint2 first, CadStructurePoint2 second) =>
        Math.Abs(first.X - second.X) <= 1.0 || Math.Abs(first.Y - second.Y) <= 1.0;

    /// <summary>
    /// One edge round several separate shapes. Shapes standing apart cannot be walked round as one,
    /// so the strip between each pair is bridged at their nearest corners first, closing them into
    /// a single figure the walk can follow.
    /// </summary>
    private static CadSlabLoop? JoinLoops(IReadOnlyList<CadSlabLoop> loops)
    {
        if (loops.Count == 0) return null;
        if (loops.Count == 1) return loops[0];

        var segments = new List<CadStructureSegment>();
        var id = 0;
        foreach (var loop in loops)
        {
            var points = loop.VerticesMm;
            for (var index = 0; index < points.Count; index++)
                segments.Add(new CadStructureSegment(
                    --id, points[index], points[(index + 1) % points.Count],
                    "EDGE", string.Empty));
        }

        // The gap is filled rather than spanned. A link drawn corner to corner runs at whatever
        // angle the corners happen to lie at, and left the joined slab with a slanted step across
        // the gap; a rectangle covering the strip between the two shapes has only the plan's own
        // directions in it, and the walk round the outside then follows those.
        for (var index = 1; index < loops.Count; index++)
        {
            var filler = GapFiller(loops[index - 1], loops[index]);
            if (filler is null) continue;
            for (var corner = 0; corner < filler.Count; corner++)
                segments.Add(new CadStructureSegment(
                    --id, filler[corner], filler[(corner + 1) % filler.Count],
                    "BRIDGE", string.Empty));
        }

        return CadPlanarGraph.BuildOuterBoundary(segments, 20.0);
    }


    /// <summary>
    /// One slab from a set of cells: the edge round them, and whatever they enclose cut out of it.
    /// </summary>
    private static CadSlabRegionCandidate? BuildRegion(
        CadSlabCell[] part,
        int id,
        IReadOnlyList<CadSlabCell> allCells,
        IReadOnlyList<CadSlabLoop> marks,
        CadSlabAnalysisOptions options,
        IReadOnlyList<CadHatchRegion>? hatches = null)
    {
        // A hatched pour is drawn by its hatch, not by the grid of lines that happens to cross it.
        // Following the hatch edge keeps the slab exactly where the plan shades it, including the
        // stretches the lines never enclose, and holds parts a beam separates together -- which the
        // edge round the cells cannot do, as it keeps only the largest of them.
        var shadedEdge = HatchOutline(part, hatches);
        var boundary = BuildGroupBoundary(part);
        if (boundary is null && shadedEdge is null) return null;
        // Parts a beam separates are one pour, so the edge runs round the outside of them all and
        // takes the beam's strip in. They were put together only because they stand close enough
        // for that, so the ground taken in is the beam and no more.
        var outer = shadedEdge
                    ?? (boundary!.DetachedParts.Count > 0
                        ? JoinLoops(new[] { boundary.Outer }.Concat(boundary.DetachedParts).ToArray())
                          ?? boundary.Outer
                        : boundary.Outer);
        if (outer.AreaMm2 / 1_000_000.0 < options.MinimumRegionAreaM2) return null;

        // A pour is cut for an outline the user picked and for a pour laid at another level,
        // and for nothing else. Cutting every bay that fell inside its edge took the columns,
        // the shafts and the bays whose edges never closed with it, and left the slab riddled
        // with holes the plan does not show.
        var holes = marks
            .Where(mark => ContainsPoint(outer.VerticesMm, Centroid(mark)))
            .Concat(boundary?.Holes ?? Array.Empty<CadSlabLoop>())
            .Select(hole => Orient(hole, counterClockwise: false))
            .Where(hole => hole.AreaMm2 >= 10_000.0)
            .ToArray();

        var labelled = part.FirstOrDefault(cell => cell.ThicknessMm is not null)
                       ?? part.FirstOrDefault(cell => cell.ElevationMm is not null)
                       ?? part[0];

        return new CadSlabRegionCandidate(
            id,
            Orient(outer, counterClockwise: true),
            holes,
            part.Select(cell => cell.Id).ToArray(),
            part.SelectMany(cell => cell.SourceSegmentIds).Distinct().ToArray(),
            labelled.ThicknessMm,
            labelled.ElevationMm,
            EffectiveThickness(labelled, options),
            EffectiveElevation(labelled, options),
            ResolveStatus(part),
            part.Select(cell => cell.MatchedText)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty)
        {
            IsLowered = part.Any(cell => cell.IsLowered),
            AbsorbedStripCount = part.Count(cell => cell.IsBeamStrip)
        };
    }

    /// <summary>
    /// Keeps only holes Revit will accept in one profile: each inside the outline, and no two
    /// touching or overlapping. Revit refuses the whole slab when loops cross, so a hole that
    /// clashes with one already kept is dropped rather than left to fail creation.
    /// </summary>
    /// <summary>
    /// Distance from a point to the nearest edge of a loop, zero when the point lies on it.
    /// </summary>
    private static double DistanceToLoop(CadSlabLoop loop, CadStructurePoint2 point)
    {
        var nearest = double.MaxValue;
        var vertices = loop.VerticesMm;
        for (var index = 0; index < vertices.Count; index++)
        {
            var a = vertices[index];
            var b = vertices[(index + 1) % vertices.Count];
            var direction = b - a;
            var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length < 1e-9) continue;
            var along = Math.Max(0.0, Math.Min(1.0,
                ((point.X - a.X) * direction.X + (point.Y - a.Y) * direction.Y) / (length * length)));
            var closest = new CadStructurePoint2(
                a.X + direction.X * along, a.Y + direction.Y * along);
            nearest = Math.Min(nearest, closest.DistanceTo(point));
        }
        return nearest;
    }

    private static IReadOnlyList<CadSlabLoop> SeparateHoles(
        IReadOnlyList<CadSlabLoop> holes,
        CadSlabLoop outer)
    {
        var kept = new List<CadSlabLoop>();
        foreach (var hole in holes.OrderByDescending(hole => hole.AreaMm2))
        {
            // A hole has to lie wholly inside the slab. An area sitting outside it, or straddling
            // its edge, is a slab of its own -- and a loop that leaves the outline makes the whole
            // profile invalid, so nothing is created at all.
            if (!ContainsPoint(outer.VerticesMm, Centroid(hole))) continue;
            if (!hole.VerticesMm.All(vertex =>
                    ContainsPoint(outer.VerticesMm, vertex)
                    || DistanceToLoop(outer, vertex) <= 1.0)) continue;
            if (kept.Any(existing => LoopsClash(existing, hole))) continue;
            kept.Add(hole);
        }
        return kept;
    }

    private static bool LoopsClash(CadSlabLoop first, CadSlabLoop second)
    {
        // Sharing any vertex or containing one another's points is enough for Revit to call the
        // profile self-intersecting.
        if (second.VerticesMm.Any(vertex => ContainsPoint(first.VerticesMm, vertex))) return true;
        if (first.VerticesMm.Any(vertex => ContainsPoint(second.VerticesMm, vertex))) return true;
        return second.VerticesMm.Any(vertex =>
            first.VerticesMm.Any(other => other.DistanceTo(vertex) <= 1.0));
    }

    /// <summary>
    /// Whether a cell shares an edge with any of the given cells, which is what puts a void inside
    /// a pour rather than beside it.
    /// </summary>
    private static bool TouchesAnyCell(CadSlabCell cell, IReadOnlyList<CadSlabCell> others)
    {
        var edges = LoopEdges(cell.Loop).ToHashSet();
        return others.Any(other => LoopEdges(other.Loop).Any(edges.Contains));
    }

    private static IEnumerable<(long, long, long, long)> LoopEdges(CadSlabLoop loop)
    {
        var vertices = loop.VerticesMm;
        for (var index = 0; index < vertices.Count; index++)
            yield return EdgeKey(vertices[index], vertices[(index + 1) % vertices.Count]);
    }

    private static IReadOnlyList<CadSlabLoop> MergeVoids(IReadOnlyList<CadSlabCell> voidCells)
    {
        if (voidCells.Count == 0) return Array.Empty<CadSlabLoop>();
        var adjacency = BuildAdjacency(voidCells);
        var group = new int[voidCells.Count];
        for (var index = 0; index < group.Length; index++) group[index] = -1;
        var next = 0;

        for (var index = 0; index < voidCells.Count; index++)
        {
            if (group[index] >= 0) continue;
            var current = next++;
            var pending = new Queue<int>();
            pending.Enqueue(index);
            group[index] = current;
            while (pending.Count > 0)
            {
                var at = pending.Dequeue();
                foreach (var neighbour in adjacency[at])
                {
                    if (group[neighbour] >= 0) continue;
                    group[neighbour] = current;
                    pending.Enqueue(neighbour);
                }
            }
        }

        var loops = new List<CadSlabLoop>();
        for (var current = 0; current < next; current++)
        {
            var members = voidCells.Where((_, index) => group[index] == current).ToArray();
            if (members.Length == 1)
            {
                loops.Add(members[0].Loop);
                continue;
            }
            var boundary = BuildGroupBoundary(members);
            if (boundary is not null) loops.Add(boundary.Outer);
        }
        return loops;
    }

    private static CadSlabLoop Orient(CadSlabLoop loop, bool counterClockwise)
    {
        var wanted = counterClockwise ? loop.SignedAreaMm2 > 0 : loop.SignedAreaMm2 < 0;
        return wanted ? loop : new CadSlabLoop(loop.VerticesMm.Reverse().ToArray());
    }

    private static (long, long, long, long) EdgeKey(CadStructurePoint2 a, CadStructurePoint2 b)
    {
        var first = (Quantise(a.X), Quantise(a.Y));
        var second = (Quantise(b.X), Quantise(b.Y));
        return first.CompareTo(second) <= 0
            ? (first.Item1, first.Item2, second.Item1, second.Item2)
            : (second.Item1, second.Item2, first.Item1, first.Item2);
    }

    private static long Quantise(double value) => (long)Math.Round(value);

    private static bool Near(CadStructurePoint2 a, CadStructurePoint2 b) => a.DistanceTo(b) <= 1.0;

    private static bool TouchesAnother(
        CadStructureSegment segment,
        IReadOnlyList<CadStructureSegment> segments,
        double toleranceMm)
    {
        var touchedStart = false;
        var touchedEnd = false;
        foreach (var other in segments)
        {
            if (other.Id == segment.Id) continue;
            if (!touchedStart
                && (other.Start.DistanceTo(segment.Start) <= toleranceMm
                    || other.End.DistanceTo(segment.Start) <= toleranceMm))
                touchedStart = true;
            if (!touchedEnd
                && (other.Start.DistanceTo(segment.End) <= toleranceMm
                    || other.End.DistanceTo(segment.End) <= toleranceMm))
                touchedEnd = true;
            // Both ends meeting other geometry is what makes a short piece part of a boundary
            // rather than a mark standing on its own.
            if (touchedStart && touchedEnd) return true;
        }
        return false;
    }

    /// <summary>
    /// The labelled bay a strip belongs to. A strip sits between the bays it separates, so the
    /// nearest labelled cell is the slab it is part of.
    /// </summary>
    private static CadSlabCell? NearestLabelledNeighbour(
        CadSlabCell strip,
        IReadOnlyList<CadSlabCell> cells)
    {
        // The strip belongs to the pour it lies in, so it reads only bays of that pour. Taking the
        // nearest labelled bay of any kind let a strip beside a shaded area carry the drop's level
        // out into the floor, where spreading then carried it on across dozens of bays.
        var centre = strip.CentroidMm;
        return cells
            .Where(cell => cell.Id != strip.Id && !cell.IsBeamStrip && cell.ThicknessMm is not null)
            .Where(cell => string.Equals(
                cell.HatchStyleKey, strip.HatchStyleKey, StringComparison.Ordinal))
            .OrderBy(cell => cell.CentroidMm.DistanceTo(centre))
            .FirstOrDefault();
    }

    private static CadSlabRegionStatus ResolveStatus(IReadOnlyList<CadSlabCell> members)
    {
        if (members.All(cell => cell.ThicknessMm is null)) return CadSlabRegionStatus.MissingThickness;
        if (members.All(cell => cell.ElevationMm is null)) return CadSlabRegionStatus.MissingElevation;
        return CadSlabRegionStatus.Ready;
    }

    private static double EffectiveThickness(CadSlabCell cell, CadSlabAnalysisOptions options) =>
        options.OverrideThickness || cell.ThicknessMm is null
            ? options.DefaultThicknessMm
            : cell.ThicknessMm.Value;

    private static double EffectiveElevation(CadSlabCell cell, CadSlabAnalysisOptions options)
    {
        if (options.OverrideElevation) return options.DefaultOffsetMm;
        if (cell.ElevationMm is not null) return cell.ElevationMm.Value;
        if (!cell.IsLowered) return options.DefaultOffsetMm;
        return options.HatchOffsetsMm.TryGetValue(cell.HatchStyleKey, out var perStyle)
            ? perStyle
            : options.LoweredDefaultOffsetMm;
    }

    /// <summary>
    /// A long, narrow cell between two bays is the footprint of a beam drawn by both faces rather
    /// than a slab of its own, so it merges with its neighbours instead of standing alone.
    /// </summary>
    /// <summary>
    /// A cell small on both sides is a column standing in the floor rather than a bay of it. It
    /// appears wherever beams meet, and no concrete is poured through it.
    /// </summary>
    private static bool IsColumnFootprint(CadSlabLoop face, double maximumSizeMm)
    {
        var minX = face.VerticesMm.Min(point => point.X);
        var maxX = face.VerticesMm.Max(point => point.X);
        var minY = face.VerticesMm.Min(point => point.Y);
        var maxY = face.VerticesMm.Max(point => point.Y);
        var width = maxX - minX;
        var height = maxY - minY;
        if (width > maximumSizeMm || height > maximumSizeMm) return false;
        // A column is roughly as deep as it is wide; a short length of beam between two bays is
        // not, and belongs to the pour rather than to a hole in it.
        var longer = Math.Max(width, height);
        var shorter = Math.Min(width, height);
        return shorter > 0 && longer <= shorter * 2.5;
    }

    private static bool IsNarrowStrip(CadSlabLoop face, double maximumWidthMm)
    {
        var minX = face.VerticesMm.Min(point => point.X);
        var maxX = face.VerticesMm.Max(point => point.X);
        var minY = face.VerticesMm.Min(point => point.Y);
        var maxY = face.VerticesMm.Max(point => point.Y);
        var width = Math.Min(maxX - minX, maxY - minY);
        var length = Math.Max(maxX - minX, maxY - minY);
        return width <= maximumWidthMm && length > width * 2.0;
    }

    private static IReadOnlyList<int> SourceIdsWithin(
        IReadOnlyList<CadStructureSegment> segments,
        CadSlabLoop face) =>
        segments
            .Where(segment =>
            {
                var mid = new CadStructurePoint2(
                    (segment.Start.X + segment.End.X) / 2.0,
                    (segment.Start.Y + segment.End.Y) / 2.0);
                return OnBoundary(face.VerticesMm, mid);
            })
            .Select(segment => segment.Id)
            .Distinct()
            .ToArray();

    private static bool OnBoundary(IReadOnlyList<CadStructurePoint2> loop, CadStructurePoint2 point)
    {
        for (var index = 0; index < loop.Count; index++)
        {
            var a = loop[index];
            var b = loop[(index + 1) % loop.Count];
            var length = a.DistanceTo(b);
            if (length < 1e-9) continue;
            var distance = Math.Abs((b.X - a.X) * (a.Y - point.Y) - (a.X - point.X) * (b.Y - a.Y)) / length;
            if (distance > 1.0) continue;
            var along = ((point.X - a.X) * (b.X - a.X) + (point.Y - a.Y) * (b.Y - a.Y)) / (length * length);
            if (along >= -0.001 && along <= 1.001) return true;
        }
        return false;
    }

    private static bool ContainsPoint(IReadOnlyList<CadStructurePoint2> loop, CadStructurePoint2 point)
    {
        var inside = false;
        for (int index = 0, previous = loop.Count - 1; index < loop.Count; previous = index++)
        {
            var a = loop[index];
            var b = loop[previous];
            if (a.Y > point.Y != b.Y > point.Y
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static CadStructurePoint2 Centroid(CadSlabLoop face)
    {
        var x = 0.0;
        var y = 0.0;
        foreach (var vertex in face.VerticesMm)
        {
            x += vertex.X;
            y += vertex.Y;
        }
        var count = Math.Max(1, face.VerticesMm.Count);
        return new CadStructurePoint2(x / count, y / count);
    }

    private static CadStructurePoint2? Intersect(CadStructureSegment first, CadStructureSegment second)
    {
        var r = first.End - first.Start;
        var s = second.End - second.Start;
        var denominator = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(denominator) < 1e-9) return null;
        var offset = second.Start - first.Start;
        var t = (offset.X * s.Y - offset.Y * s.X) / denominator;
        var u = (offset.X * r.Y - offset.Y * r.X) / denominator;
        if (t < 0 || t > 1 || u < 0 || u > 1) return null;
        return first.Start + r * t;
    }

    private static string NormalizeText(string value)
    {
        // \P is a line break and must become a separator before the formatting codes are stripped;
        // otherwise the code pattern consumes it and the two lines run together, hiding the
        // elevation written above the thickness.
        var normalized = MTextControlRegex.Replace(
            value.Replace("\\P", " ").Replace("\\p", " "), string.Empty);
        return normalized
            .Replace("{", string.Empty)
            .Replace("}", string.Empty)
            .Trim();
    }

    private static string? Validate(CadSlabAnalysisOptions options)
    {
        if (!Finite(options.VertexSnapToleranceMm) || options.VertexSnapToleranceMm < 0)
            return "Vertex Snap không hợp lệ.";
        if (!Finite(options.MinimumLineLengthMm) || options.MinimumLineLengthMm < 0)
            return "Min Line không hợp lệ.";
        if (options.MinimumRegionAreaM2 < 0) return "Diện tích tối thiểu không hợp lệ.";
        if (options.MinimumThicknessMm <= 0 || options.MaximumThicknessMm <= options.MinimumThicknessMm)
            return "Dải chiều dày không hợp lệ.";
        if (options.MaximumBeamStripWidthMm < 0) return "Bề rộng dầm tối đa không hợp lệ.";
        if (!Finite(options.HatchJoinDistanceMm) || options.HatchJoinDistanceMm < 0)
            return "Khoảng nối hatch không hợp lệ.";
        return null;
    }

    private static CadSlabAnalysis Invalid(string error) => new(
        default, default, Array.Empty<CadSlabRegionCandidate>(), Array.Empty<CadSlabCell>(),
        0, 0, 0, Array.Empty<string>(), error);

    private static bool TryInvariant(string value, out double result) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out result);

    private static double Dot(CadStructurePoint2 first, CadStructurePoint2 second) =>
        first.X * second.X + first.Y * second.Y;

    private static bool Finite(CadStructurePoint2 point) => Finite(point.X) && Finite(point.Y);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
