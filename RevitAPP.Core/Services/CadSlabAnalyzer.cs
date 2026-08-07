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
    private static readonly Regex ElevationRegex = new(
        @"(?<sign>[+\-±])?\s*(?<value>\d+[.,]\d{2,3})\b",
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

        var shortLines = segments.Count(segment =>
            segment.Start.DistanceTo(segment.End) < options.MinimumLineLengthMm);
        var usable = segments
            .Where(segment => segment.Start.DistanceTo(segment.End) >= options.MinimumLineLengthMm)
            .ToArray();

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

        var faces = CadPlanarGraph.BuildFaces(usable, options.VertexSnapToleranceMm, out var unclosed);
        var cells = ClassifyCells(faces, usable, annotations, hatchRegions, options);
        var regions = MergeCells(cells, options);

        var warnings = new List<string>();
        if (shortLines > 0)
            warnings.Add($"Đã bỏ qua {shortLines} line ngắn hơn {options.MinimumLineLengthMm:0} mm.");
        if (unclosed > 0)
            warnings.Add($"{unclosed} đầu line chưa khép thành vùng. "
                + $"Tăng Vertex Snap ({options.VertexSnapToleranceMm:0} mm) nếu biên bị hở.");
        var orphanHatches = hatchRegions.Count(hatch =>
            !cells.Any(cell => ContainsPoint(hatch.BoundaryMm, cell.CentroidMm)));
        if (orphanHatches > 0)
            warnings.Add($"{orphanHatches} vùng hatch không nằm trong ô sàn nào.");
        if (regions.Count == 0)
            warnings.Add("Không dựng được vùng sàn kín nào từ line đã quét.");

        var anchor = slabPackage.SourceAnchor * scale - origin;
        return new CadSlabAnalysis(origin, anchor, regions, cells,
            shortLines, unclosed, orphanHatches, warnings, null);
    }

    private static IReadOnlyList<CadSlabCell> ClassifyCells(
        IReadOnlyList<CadSlabLoop> faces,
        IReadOnlyList<CadStructureSegment> segments,
        IReadOnlyList<CadStructureAnnotation> annotations,
        IReadOnlyList<CadHatchRegion> hatches,
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
            var lowered = hatches.Any(hatch => ContainsPoint(hatch.BoundaryMm, centroid));
            var opening = HasCrossMark(face, segments);
            var text = inside.Length > 0 ? string.Join(" ", inside.Select(item => item.Text)) : string.Empty;

            cells.Add(new CadSlabCell(index + 1, face, SourceIdsWithin(segments, face))
            {
                ThicknessMm = thickness.Value,
                ElevationMm = elevation.Value,
                IsOpening = opening,
                IsLowered = lowered,
                IsBeamStrip = IsNarrowStrip(face, options.MaximumBeamStripWidthMm),
                MatchedText = text
            });
        }
        return cells;
    }

    /// <summary>
    /// A cross drawn corner to corner marks a shaft or a stairwell, which is left open rather than
    /// poured. Reading the mark from the geometry keeps it working whatever layer it sits on.
    /// </summary>
    private static bool HasCrossMark(CadSlabLoop face, IReadOnlyList<CadStructureSegment> segments)
    {
        var centre = Centroid(face);
        var diagonals = segments.Where(segment =>
        {
            if (!ContainsPoint(face.VerticesMm, segment.Start)
                && !ContainsPoint(face.VerticesMm, segment.End)) return false;
            var mid = new CadStructurePoint2(
                (segment.Start.X + segment.End.X) / 2.0,
                (segment.Start.Y + segment.End.Y) / 2.0);
            return ContainsPoint(face.VerticesMm, mid);
        }).ToArray();
        if (diagonals.Length < 2) return false;

        for (var first = 0; first < diagonals.Length; first++)
        for (var second = first + 1; second < diagonals.Length; second++)
        {
            var crossing = Intersect(diagonals[first], diagonals[second]);
            if (crossing is null) continue;
            // The two strokes of a cross meet near the middle of the bay; two edges that merely
            // touch meet at a corner instead.
            var reach = Math.Sqrt(face.AreaMm2) / 3.0;
            if (crossing.Value.DistanceTo(centre) <= reach) return true;
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
        var poured = cells.Where(cell => !cell.IsOpening).ToArray();
        var groups = poured
            .GroupBy(cell => (
                Elevation: Math.Round(EffectiveElevation(cell, options) / ElevationToleranceMm),
                Thickness: Math.Round(EffectiveThickness(cell, options) / ThicknessToleranceMm)))
            .ToArray();

        var regions = new List<CadSlabRegionCandidate>();
        var id = 1;
        foreach (var group in groups)
        {
            var members = group.ToArray();
            var outer = members
                .OrderByDescending(cell => cell.Loop.AreaMm2)
                .First().Loop;
            var area = members.Sum(cell => cell.Loop.AreaMm2);
            if (area / 1_000_000.0 < options.MinimumRegionAreaM2) continue;

            var holes = cells
                .Where(cell => cell.IsOpening && ContainsPoint(outer.VerticesMm, cell.CentroidMm))
                .Select(cell => cell.Loop)
                .ToArray();

            var thickness = EffectiveThickness(members[0], options);
            var elevation = EffectiveElevation(members[0], options);
            var status = ResolveStatus(members);

            regions.Add(new CadSlabRegionCandidate(
                id++,
                outer,
                holes,
                members.Select(cell => cell.Id).ToArray(),
                members.SelectMany(cell => cell.SourceSegmentIds).Distinct().ToArray(),
                members[0].ThicknessMm,
                members[0].ElevationMm,
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
        return cell.IsLowered ? options.LoweredDefaultOffsetMm : options.DefaultOffsetMm;
    }

    /// <summary>
    /// A long, narrow cell between two bays is the footprint of a beam drawn by both faces rather
    /// than a slab of its own, so it merges with its neighbours instead of standing alone.
    /// </summary>
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
        var normalized = MTextControlRegex.Replace(value, string.Empty);
        return normalized.Replace("\\P", " ")
            .Replace("\\p", " ")
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
