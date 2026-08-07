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

        var marks = options.OpeningMarksMm
            .Where(mark => Finite(mark.Start) && Finite(mark.End))
            .Select(mark => mark with
            {
                Start = mark.Start * scale - origin,
                End = mark.End * scale - origin
            })
            .ToArray();

        var faces = CadPlanarGraph.BuildFaces(usable, options.VertexSnapToleranceMm, out var unclosed);
        var cells = ClassifyCells(faces, usable, annotations, hatchRegions, marks, options);
        var regions = MergeCells(cells, options);

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

        var anchor = slabPackage.SourceAnchor * scale - origin;
        return new CadSlabAnalysis(origin, anchor, regions, cells,
            shortLines, unclosed, orphanHatches, warnings, null)
        {
            HatchStyles = styles
        };
    }

    private static IReadOnlyList<CadSlabCell> ClassifyCells(
        IReadOnlyList<CadSlabLoop> faces,
        IReadOnlyList<CadStructureSegment> segments,
        IReadOnlyList<CadStructureAnnotation> annotations,
        IReadOnlyList<CadHatchRegion> hatches,
        IReadOnlyList<CadStructureSegment> marks,
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
            var elevation = ReadElevation(inside);
            var hatch = hatches.FirstOrDefault(item => ContainsPoint(item.BoundaryMm, centroid));
            var lowered = hatch is not null;
            // A mark the user picked settles the matter; the geometric guess only stands in when
            // nothing was picked.
            var opening = marks.Count > 0
                ? MarkedAsOpening(face, marks)
                : HasCrossMark(face, segments);
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
    /// Whether a mark the user picked falls in this bay. A stroke of a cross runs through the bay
    /// it marks, so either an endpoint or the middle of the stroke lands inside it.
    /// </summary>
    private static bool MarkedAsOpening(
        CadSlabLoop face,
        IReadOnlyList<CadStructureSegment> marks)
    {
        // Judge the mark by its middle alone. A stroke ends at the corner of a bay, which is a
        // corner of the neighbouring bays too, so testing its ends marks them open as well.
        foreach (var mark in marks)
        {
            var mid = new CadStructurePoint2(
                (mark.Start.X + mark.End.X) / 2.0,
                (mark.Start.Y + mark.End.Y) / 2.0);
            if (ContainsPoint(face.VerticesMm, mid)) return true;
        }
        return false;
    }

    private static bool HasCrossMark(CadSlabLoop face, IReadOnlyList<CadStructureSegment> segments)
    {
        var centre = Centroid(face);
        var minX = face.VerticesMm.Min(point => point.X);
        var maxX = face.VerticesMm.Max(point => point.X);
        var minY = face.VerticesMm.Min(point => point.Y);
        var maxY = face.VerticesMm.Max(point => point.Y);
        var diagonalLength = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        if (diagonalLength < 1.0) return false;

        // A stroke of the cross runs corner to corner, so it is long relative to the bay and
        // slanted against its sides. Edges of the bay and the beams inside it run along the sides
        // and are excluded by the slant alone.
        var strokes = segments.Where(segment =>
        {
            var mid = new CadStructurePoint2(
                (segment.Start.X + segment.End.X) / 2.0,
                (segment.Start.Y + segment.End.Y) / 2.0);
            if (!ContainsPoint(face.VerticesMm, mid)) return false;
            var length = segment.Start.DistanceTo(segment.End);
            if (length < diagonalLength * 0.3) return false;
            var dx = Math.Abs(segment.End.X - segment.Start.X);
            var dy = Math.Abs(segment.End.Y - segment.Start.Y);
            return dx > length * 0.2 && dy > length * 0.2;
        }).ToArray();
        if (strokes.Length < 2) return false;

        for (var first = 0; first < strokes.Length; first++)
        for (var second = first + 1; second < strokes.Length; second++)
        {
            // The two strokes must lean opposite ways, which is what makes the mark a cross
            // rather than two parallel slashes.
            var firstSlope = (strokes[first].End.Y - strokes[first].Start.Y)
                             * (strokes[first].End.X - strokes[first].Start.X);
            var secondSlope = (strokes[second].End.Y - strokes[second].Start.Y)
                              * (strokes[second].End.X - strokes[second].Start.X);
            if (firstSlope * secondSlope >= 0) continue;

            var crossing = Intersect(strokes[first], strokes[second]);
            if (crossing is null) continue;
            if (crossing.Value.DistanceTo(centre) <= diagonalLength / 3.0) return true;
        }
        return false;
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
        IReadOnlyList<CadStructureAnnotation> annotations)
    {
        var values = new List<double>();
        foreach (var annotation in annotations)
        {
            var match = ElevationRegex.Match(annotation.Text);
            if (!match.Success) continue;
            if (!TryInvariant(match.Groups["value"].Value, out var metres)) continue;
            var sign = match.Groups["sign"].Value == "-" ? -1.0 : 1.0;
            values.Add(sign * metres * 1000.0);
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
        CadSlabAnalysisOptions options)
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
        // Spreading runs over the cells that take concrete, and openings and columns were already
        // excluded from that set, so nothing here can revive them as slabs.

        // Level and thickness are what separate one pour from another. A hatch only says a bay
        // drops when nothing states its level: hatched and plain bays at the same level are the
        // same slab, and grouping by the pattern as well would split them in two.
        var groups = resolved
            .GroupBy(cell => (
                Elevation: Math.Round(EffectiveElevation(cell, options) / ElevationToleranceMm),
                Thickness: Math.Round(EffectiveThickness(cell, options) / ThicknessToleranceMm)))
            .ToArray();

        var regions = new List<CadSlabRegionCandidate>();
        var id = 1;
        foreach (var group in groups)
        {
            var members = group.ToArray();
            // The outside edge comes from the pour together with the voids inside it: a stair core
            // does not push the edge of the floor inwards, it makes a hole in it. Leaving the
            // marked cells out of this step would shrink the slab to the shape around them.
            var enclosed = members
                .Concat(cells.Where(cell => (cell.IsOpening || cell.IsColumn)
                                            && TouchesAnyCell(cell, members)))
                .ToArray();
            var boundary = BuildGroupBoundary(enclosed);
            if (boundary is null) continue;
            var outer = boundary.Outer;
            if (outer.AreaMm2 / 1_000_000.0 < options.MinimumRegionAreaM2) continue;

            // Voids inside the pour come from two places: stitching leaves a loop around a part
            // the group never covered, and cells marked as openings or columns sit inside it.
            // Adjacent marked cells form one void, so a stair core drawn as several bays is cut
            // as a single opening rather than as a grid of small ones.
            var voidCells = enclosed
                .Where(cell => cell.IsOpening || cell.IsColumn)
                .ToArray();
            var holes = boundary.Holes
                .Concat(MergeVoids(voidCells))
                .Select(hole => Orient(hole, counterClockwise: false))
                // A sliver left where beams cross is not a hole worth cutting, and Revit rejects a
                // profile carrying a loop that small.
                .Where(hole => hole.AreaMm2 >= 10_000.0)
                .ToArray();

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
        var pending = new Queue<int>();
        for (var index = 0; index < result.Length; index++)
            if (result[index].ThicknessMm is not null || result[index].ElevationMm is not null)
                pending.Enqueue(index);

        while (pending.Count > 0)
        {
            var index = pending.Dequeue();
            var source = result[index];
            foreach (var neighbour in adjacency[index])
            {
                var target = result[neighbour];
                if (target.ThicknessMm is not null || target.ElevationMm is not null) continue;
                // A label crosses a hatch boundary freely: the hatch shades part of a pour, it
                // does not end it. Only a level of its own separates one slab from the next.
                result[neighbour] = target with
                {
                    ThicknessMm = source.ThicknessMm,
                    ElevationMm = source.ElevationMm,
                    MatchedText = source.MatchedText
                };
                pending.Enqueue(neighbour);
            }
        }
        return result.Where(cell => pouredIds.Contains(cell.Id)).ToArray();
    }

    private static List<int>[] BuildAdjacency(IReadOnlyList<CadSlabCell> cells)
    {
        var owners = new Dictionary<(long, long, long, long), List<int>>();
        for (var index = 0; index < cells.Count; index++)
        {
            var loop = cells[index].Loop.VerticesMm;
            for (var vertex = 0; vertex < loop.Count; vertex++)
            {
                var key = EdgeKey(loop[vertex], loop[(vertex + 1) % loop.Count]);
                if (!owners.TryGetValue(key, out var list)) owners[key] = list = new List<int>();
                if (!list.Contains(index)) list.Add(index);
            }
        }

        var adjacency = new List<int>[cells.Count];
        for (var index = 0; index < adjacency.Length; index++) adjacency[index] = new List<int>();
        foreach (var list in owners.Values.Where(list => list.Count == 2))
        {
            adjacency[list[0]].Add(list[1]);
            adjacency[list[1]].Add(list[0]);
        }
        return adjacency;
    }

    private sealed record GroupBoundary(CadSlabLoop Outer, IReadOnlyList<CadSlabLoop> Holes);

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
        var holes = ordered.Skip(1)
            .Select(loop => Orient(loop, counterClockwise: false))
            .ToArray();
        return new GroupBoundary(outer, holes);
    }

    /// <summary>
    /// Joins voids that touch into one outline. A stair core is drawn as several bays, and cutting
    /// each of them separately would leave lines of slab between them that no plan shows.
    /// </summary>
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
        var centre = strip.CentroidMm;
        return cells
            .Where(cell => cell.Id != strip.Id && !cell.IsBeamStrip && cell.ThicknessMm is not null)
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
        return null;
    }

    private static CadSlabAnalysis Invalid(string error) => new(
        default, default, Array.Empty<CadSlabRegionCandidate>(), Array.Empty<CadSlabCell>(),
        0, 0, 0, Array.Empty<string>(), error);

    private static bool TryInvariant(string value, out double result) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out result);

    private static bool Finite(CadStructurePoint2 point) => Finite(point.X) && Finite(point.Y);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
