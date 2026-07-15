using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamDrawing;
using RevitAPP.Core.Services;
using RevitAPP.Helpers;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
///     Đặt spot elevation cao độ mặt trên dầm trên sectional view. Best-effort: lỗi → warn, không chặn.
///     PHẢI gọi trong Transaction đang mở, sau khi view đã commit + regenerate.
/// </summary>
public sealed class SpotElevationPlacer
{
    public bool Place(Document doc, View view, ViewBeamPair pair, ElementId? spotTypeId,
        double offsetMm, List<string> warnings)
    {
        try
        {
            var target = FindTopFaceTarget(doc, view, pair, out var searchDiagnostics);
            if (target == null)
            {
                var diagnosticPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "RevitAPP-spot-diagnostic.txt");
                try { System.IO.File.AppendAllText(diagnosticPath, searchDiagnostics + Environment.NewLine); }
                catch { /* diagnostic only */ }
                warnings.Add($"Không tìm thấy mặt trên hợp lệ để đặt spot elevation ở view '{view.Name}'.");
                return false;
            }

            // Reference point bắt buộc nằm trên chính geometric face. Reference(element) hoặc một điểm dựng từ
            // CropBox có thể tạo spot tạm thời nhưng Revit sẽ xóa ở lần regenerate kế tiếp vì mất reference.
            var right = Normalize(view.RightDirection);
            var extraOffset = Math.Max(offsetMm, 0) / 304.8;
            var bend = target.Point - right * (0.18 + extraOffset);
            var end = target.Point - right * (0.33 + extraOffset);
            var spot = doc.Create.NewSpotElevation(
                view, target.Reference, target.Point, bend, end, target.Point, true);
            if (spot != null && spotTypeId != null && spotTypeId != ElementId.InvalidElementId)
                spot.ChangeTypeId(spotTypeId);
            doc.Regenerate();
            return spot != null;
        }
        catch (Exception ex)
        {
            warnings.Add($"Không đặt được spot elevation ở view '{view.Name}': {ex.Message}");
            return false;
        }
    }

    private sealed record FaceTarget(Reference Reference, XYZ Point, double Z);

    private static FaceTarget? FindTopFaceTarget(Document doc, View view, ViewBeamPair pair,
        out string diagnostics)
    {
        var trace = new System.Text.StringBuilder();
        var station = StationPoint(pair);
        var expectedTop = pair.Geometry.TopZFeet;
        var topTolerance = CrossDimensionLayerMath.CoincidentToleranceMm / 304.8;
        trace.AppendLine($"VIEW={view.Name}; BEAM={pair.Beam.Id.ToValue()}; STATION={pair.Station:F6}; " +
                         $"EXPECTED_TOP_MM={expectedTop * 304.8:F3}; TOL_MM={topTolerance * 304.8:F3}");
        var options = new Options { ComputeReferences = true, View = view };
        var targets = CollectHorizontalFaceTargets(pair.Beam, options, station, view, pair.Geometry.WidthFeet,
            trace, "BEAM_VIEW");
        if (targets.Count == 0)
            targets = CollectHorizontalFaceTargets(pair.Beam, new Options { ComputeReferences = true },
                station, view, pair.Geometry.WidthFeet, trace, "BEAM_MODEL");
        trace.AppendLine("BEAM_ACCEPTED_Z_MM=" + string.Join(",", targets.Select(x => (x.Z * 304.8).ToString("F3"))));
        var beamTop = targets
            .Where(target => Math.Abs(target.Z - expectedTop) <= topTolerance)
            .OrderBy(target => Math.Abs(target.Z - expectedTop))
            .FirstOrDefault();
        if (beamTop != null)
        {
            diagnostics = trace.ToString();
            return beamTop;
        }

        // A joined slab can consume the beam's top face completely. Only in the aligned-top case may the floor
        // top face substitute for it; a depressed slab must never be reported as the beam-top elevation.
        var probe = 2.0 / 304.8;
        var floors = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(new Outline(
                new XYZ(station.X - probe, station.Y - probe, expectedTop - 300.0 / 304.8),
                new XYZ(station.X + probe, station.Y + probe, expectedTop + 300.0 / 304.8))))
            .Cast<Element>();
        var floorList = floors.ToList();
        trace.AppendLine("FLOOR_IDS=" + string.Join(",", floorList.Select(x => x.Id.ToValue())));
        var floorTargets = new List<FaceTarget>();
        foreach (var floor in floorList)
        {
            var found = CollectHorizontalFaceTargets(floor, options, station, view, pair.Geometry.WidthFeet,
                trace, $"FLOOR_{floor.Id.ToValue()}_VIEW");
            if (found.Count == 0)
                found = CollectHorizontalFaceTargets(floor, new Options { ComputeReferences = true },
                    station, view, pair.Geometry.WidthFeet, trace, $"FLOOR_{floor.Id.ToValue()}_MODEL");
            floorTargets.AddRange(found);
        }

        trace.AppendLine("FLOOR_ACCEPTED_Z_MM=" +
                         string.Join(",", floorTargets.Select(x => (x.Z * 304.8).ToString("F3"))));
        var result = floorTargets
            .Where(target => Math.Abs(target.Z - expectedTop) <= topTolerance)
            .OrderBy(target => Math.Abs(target.Z - expectedTop))
            .FirstOrDefault();
        diagnostics = trace.ToString();
        return result;
    }

    private static List<FaceTarget> CollectHorizontalFaceTargets(Element element, Options options, XYZ station,
        View view, double beamWidth, System.Text.StringBuilder trace, string source)
    {
        var result = new List<FaceTarget>();
        var geometry = element.get_Geometry(options);
        if (geometry == null)
        {
            trace.AppendLine($"{source}:GEOMETRY_NULL");
            return result;
        }
        var faceIndex = 0;
        foreach (var solid in CollectSolids(geometry))
        {
            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace planar) continue;
                var normal = Normalize(planar.FaceNormal);
                if (Math.Abs(normal.Z) < 0.5) continue;

                var z = planar.Origin.Z -
                        (normal.X * (station.X - planar.Origin.X) + normal.Y * (station.Y - planar.Origin.Y)) /
                        normal.Z;

                // Chọn điểm gần mép trái nhưng lùi 2mm vào trong để IsInside ổn định sau join/cut.
                var leftTarget = new XYZ(station.X, station.Y, z) -
                                 Normalize(view.RightDirection) * Math.Max(beamWidth * 0.5 - 2.0 / 304.8, 0);
                var projectionTarget = leftTarget;
                var projection = planar.Project(projectionTarget);
                var leftInside = projection != null && planar.IsInside(projection.UVPoint);
                if (!leftInside)
                {
                    projectionTarget = new XYZ(station.X, station.Y, z);
                    projection = planar.Project(projectionTarget);
                }
                var finalInside = projection != null && planar.IsInside(projection.UVPoint);
                var distanceMm = projection == null
                    ? double.NaN
                    : projection.XYZPoint.DistanceTo(projectionTarget) * 304.8;
                trace.AppendLine($"{source}:FACE={faceIndex++}; Z_MM={z * 304.8:F3}; NZ={normal.Z:F3}; " +
                                 $"REF={(face.Reference != null)}; LEFT_INSIDE={leftInside}; " +
                                 $"FINAL_INSIDE={finalInside}; DIST_MM={distanceMm:F3}");
                if (face.Reference == null || projection == null || !finalInside || distanceMm > 1.0) continue;
                result.Add(new FaceTarget(face.Reference, projection.XYZPoint, projection.XYZPoint.Z));
            }
        }
        return result;
    }

    private static IEnumerable<Solid> CollectSolids(GeometryElement geometry)
    {
        foreach (var obj in geometry)
        {
            if (obj is Solid { Faces.Size: > 0, Volume: > 1e-9 } solid) yield return solid;
            else if (obj is GeometryInstance instance)
            {
                foreach (var nested in CollectSolids(instance.GetInstanceGeometry())) yield return nested;
            }
        }
    }

    private static XYZ StationPoint(ViewBeamPair pair)
    {
        var t = pair.Station ?? 0.5;
        return new XYZ(
            pair.Geometry.Start.X + (pair.Geometry.End.X - pair.Geometry.Start.X) * t,
            pair.Geometry.Start.Y + (pair.Geometry.End.Y - pair.Geometry.Start.Y) * t,
            (pair.Geometry.TopZFeet + pair.Geometry.BottomZFeet) * 0.5);
    }

    private static XYZ Normalize(XYZ vector)
    {
        var length = vector.GetLength();
        return length < 1e-9 ? XYZ.BasisX : vector / length;
    }
}
