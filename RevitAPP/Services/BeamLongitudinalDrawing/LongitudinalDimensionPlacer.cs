using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

public sealed class LongitudinalDimensionPlacer
{
    private sealed record Witness(Reference Ref, double Projection);

    public void Place(Document document, View view, IReadOnlyList<FamilyInstance> beams,
        IReadOnlyList<Rebar> rebars, BeamChainModel chain, ElementId? dimensionTypeId,
        double offsetMm, List<string> warnings)
    {
        var axis = (ToXyz(chain.End) - ToXyz(chain.Start)).Normalize();
        var bottom = chain.Spans.Min(span => Math.Min(span.Start.Z, span.End.Z) - span.HeightFeet * 0.5);
        var top = chain.Spans.Max(span => Math.Max(span.Start.Z, span.End.Z) + span.HeightFeet * 0.5);
        var type = dimensionTypeId == null ? null : document.GetElement(dimensionTypeId) as DimensionType;
        var baseOffset = Math.Max(offsetMm, 100) / 304.8;
        var paperFoot = Math.Max(view.Scale, 1) / 304.8;
        var tagBoxes = new FilteredElementCollector(document, view.Id).OfClass(typeof(IndependentTag))
            .Cast<IndependentTag>().Select(tag => tag.get_BoundingBox(view)).Where(box => box != null).ToList();
        var upperDimensionZ = tagBoxes.Count == 0
            ? top + baseOffset
            : Math.Max(top + baseOffset, tagBoxes.Max(box => box!.Max.Z) + 3 * paperFoot);
        var lowerFirstDimensionZ = tagBoxes.Count == 0
            ? bottom - baseOffset
            : Math.Min(bottom - baseOffset, tagBoxes.Min(box => box!.Min.Z) - 3 * paperFoot);

        var columnFaces = ColumnFaceWitnesses(document, view, axis, chain);
        var (upperRebarEnds, lowerRebarEnds) = CreateReinforcementEndWitnesses(
            document, rebars, view, axis, chain);

        // User rule: one continuous upper chain = column faces + upper additional-rebar ends.
        TryPlace(document, view, Merge(columnFaces, upperRebarEnds), chain,
            upperDimensionZ, type, "DIM tren: cot + thep tang cuong tren", warnings);

        // User rule: first lower chain = column faces + lower additional-rebar ends.
        TryPlace(document, view, Merge(columnFaces, lowerRebarEnds), chain,
            lowerFirstDimensionZ, type, "DIM duoi lop 1: cot + thep tang cuong duoi", warnings);

        // User rule: second lower chain = continuous grid dimensions only.
        TryPlace(document, view, GridWitnesses(document, view, axis, chain), chain,
            lowerFirstDimensionZ - 8 * paperFoot, type, "DIM duoi lop 2: cac truc", warnings);
    }

    private static IReadOnlyList<Witness> Merge(params IReadOnlyList<Witness>[] groups) =>
        Normalize(groups.SelectMany(group => group));

    private static IReadOnlyList<Witness> Normalize(IEnumerable<Witness> witnesses) => witnesses
        .OrderBy(item => item.Projection)
        .GroupBy(item => Math.Round(item.Projection * 304.8 / 2.0))
        .Select(group => group.First()).ToList();

    private static void TryPlace(Document document, View view, IReadOnlyList<Witness> witnesses,
        BeamChainModel chain, double z, DimensionType? type, string label, List<string> warnings)
    {
        if (witnesses.Count < 2) { warnings.Add($"Khong du reference cho {label}."); return; }
        try
        {
            var array = new ReferenceArray();
            foreach (var witness in witnesses) array.Append(witness.Ref);
            var start = ToXyz(chain.Start);
            var end = ToXyz(chain.End);
            var line = Line.CreateBound(new XYZ(start.X, start.Y, z), new XYZ(end.X, end.Y, z));
            _ = type == null ? document.Create.NewDimension(view, line, array)
                : document.Create.NewDimension(view, line, array, type);
        }
        catch (Exception exception) { warnings.Add($"Khong tao duoc {label}: {exception.Message}"); }
    }

    private static IReadOnlyList<Witness> ColumnFaceWitnesses(Document document, View view,
        XYZ axis, BeamChainModel chain)
    {
        var (min, max) = ProjectionRange(chain, axis, 500.0 / 304.8);
        var result = new List<Witness>();
        foreach (var column in new FilteredElementCollector(document, view.Id)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType())
        {
            try
            {
                foreach (var solid in Solids(column.get_Geometry(new Options
                             { ComputeReferences = true, View = view })))
                foreach (Face face in solid.Faces)
                    if (face is PlanarFace planar && planar.Reference != null &&
                        Math.Abs(planar.FaceNormal.Normalize().DotProduct(axis)) > 0.90)
                    {
                        var projection = planar.Origin.DotProduct(axis);
                        if (projection >= min && projection <= max)
                            result.Add(new Witness(planar.Reference, projection));
                    }
            }
            catch { }
        }
        return Normalize(result);
    }

    private static (IReadOnlyList<Witness> Upper, IReadOnlyList<Witness> Lower)
        CreateReinforcementEndWitnesses(Document document, IReadOnlyList<Rebar> rebars,
            View view, XYZ axis, BeamChainModel chain)
    {
        var inverse = view.CropBox.Transform.Inverse;
        var candidates = new List<(Rebar Rebar, double Min, double Max, double LayerY, XYZ MinPoint, XYZ MaxPoint)>();
        foreach (var rebar in rebars)
        {
            try
            {
                var points = rebar.GetCenterlineCurves(false, false, false,
                        MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                    .SelectMany(curve => new[] { curve.GetEndPoint(0), curve.GetEndPoint(1) }).ToList();
                if (points.Count < 2) continue;
                var projected = points.Select(point => (Point: point, Value: point.DotProduct(axis))).ToList();
                var minItem = projected.MinBy(item => item.Value);
                var maxItem = projected.MaxBy(item => item.Value);
                var extent = maxItem.Value - minItem.Value;
                if (extent < 300.0 / 304.8 || extent >= chain.TotalLengthFeet * 0.85) continue;
                var layerY = points.Select(point => inverse.OfPoint(point).Y).Average();
                candidates.Add((rebar, minItem.Value, maxItem.Value, layerY, minItem.Point, maxItem.Point));
            }
            catch { }
        }
        if (candidates.Count == 0) return ([], []);
        var midY = (candidates.Min(item => item.LayerY) + candidates.Max(item => item.LayerY)) * 0.5;
        GraphicsStyle? invisibleStyle = null;
        try
        {
            invisibleStyle = Category.GetCategory(document, BuiltInCategory.OST_InvisibleLines)
                ?.GetGraphicsStyle(GraphicsStyleType.Projection);
        }
        catch { }

        var upper = new List<Witness>();
        var lower = new List<Witness>();
        foreach (var item in candidates)
        foreach (var endpoint in new[] { (item.Min, item.MinPoint), (item.Max, item.MaxPoint) })
        {
            try
            {
                var halfLength = 20.0 / 304.8;
                var line = Line.CreateBound(endpoint.Item2 - XYZ.BasisZ * halfLength,
                    endpoint.Item2 + XYZ.BasisZ * halfLength);
                var detail = document.Create.NewDetailCurve(view, line);
                if (invisibleStyle != null) detail.LineStyle = invisibleStyle;
                var witness = new Witness(detail.GeometryCurve.Reference, endpoint.Item1);
                (item.LayerY >= midY ? upper : lower).Add(witness);
            }
            catch { }
        }
        return (Normalize(upper), Normalize(lower));
    }

    private static (IReadOnlyList<Witness> Upper, IReadOnlyList<Witness> Lower)
        ReinforcementEndWitnesses(IReadOnlyList<Rebar> rebars, View view, XYZ axis, BeamChainModel chain)
    {
        var inverse = view.CropBox.Transform.Inverse;
        var chainLength = chain.TotalLengthFeet;
        var candidates = new List<(bool Upper, Witness Witness)>();
        var boxes = rebars.Select(rebar => rebar.get_BoundingBox(view)).Where(box => box != null).ToList();
        if (boxes.Count == 0) return ([], []);
        var localCenters = boxes.Select(box => inverse.OfPoint((box!.Min + box.Max) * 0.5).Y).ToList();
        var midY = (localCenters.Min() + localCenters.Max()) * 0.5;

        foreach (var rebar in rebars)
        {
            try
            {
                var box = rebar.get_BoundingBox(view);
                if (box == null) continue;
                var a = inverse.OfPoint(box.Min);
                var b = inverse.OfPoint(box.Max);
                var visibleLength = Math.Abs(b.X - a.X);
                if (visibleLength >= chainLength * 0.85) continue; // main continuous bar, not additional steel
                var upper = inverse.OfPoint((box.Min + box.Max) * 0.5).Y >= midY;
                foreach (var solid in Solids(rebar.get_Geometry(new Options
                             { ComputeReferences = true, View = view })))
                foreach (Face face in solid.Faces)
                    if (face is PlanarFace planar && planar.Reference != null &&
                        Math.Abs(planar.FaceNormal.Normalize().DotProduct(axis)) > 0.90)
                        candidates.Add((upper, new Witness(planar.Reference, planar.Origin.DotProduct(axis))));
            }
            catch { }
        }
        return (Normalize(candidates.Where(item => item.Upper).Select(item => item.Witness)),
            Normalize(candidates.Where(item => !item.Upper).Select(item => item.Witness)));
    }

    private static IReadOnlyList<Witness> GridWitnesses(Document document, View view,
        XYZ axis, BeamChainModel chain)
    {
        var (min, max) = ProjectionRange(chain, axis, 500.0 / 304.8);
        var result = new List<Witness>();
        foreach (var grid in new FilteredElementCollector(document, view.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            try
            {
                var curve = grid.Curve;
                var midpoint = (curve.GetEndPoint(0) + curve.GetEndPoint(1)) * 0.5;
                var projection = midpoint.DotProduct(axis);
                if (projection >= min && projection <= max)
                    result.Add(new Witness(new Reference(grid), projection));
            }
            catch { }
        }
        return Normalize(result);
    }

    private static (double Min, double Max) ProjectionRange(BeamChainModel chain, XYZ axis, double margin)
    {
        var a = ToXyz(chain.Start).DotProduct(axis);
        var b = ToXyz(chain.End).DotProduct(axis);
        return (Math.Min(a, b) - margin, Math.Max(a, b) + margin);
    }

    private static IEnumerable<Solid> Solids(GeometryElement? geometry)
    {
        if (geometry == null) yield break;
        foreach (var obj in geometry)
            if (obj is Solid { Faces.Size: > 0 } solid) yield return solid;
            else if (obj is GeometryInstance instance)
                foreach (var nested in Solids(instance.GetInstanceGeometry())) yield return nested;
    }

    private static XYZ ToXyz(RevitAPP.Core.Models.BeamDrawing.Point3 point) =>
        new(point.X, point.Y, point.Z);
}
