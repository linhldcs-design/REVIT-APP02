using Autodesk.Revit.DB;
using FootingDrawing.Core.Models;

namespace FootingDrawing.Addin.Services.Annotation;

public sealed class FootingDimensionPlacer
{
    private const double LaneFeet = 0.5;
    private const double ClusterTolFeet = 15.0 / 304.8;
    private const double BlindingOffsetFeet = 100.0 / 304.8;
    private const double PedestalDimGapFeet = 150.0 / 304.8;

    public int Place(Document doc, View view, FootingGeometry geometry, FootingDrawingSetting setting,
        Element footing, ElementId? dimensionTypeId, List<string> warnings)
    {
        var (wantOverall, wantBase) = (setting.DimOverallEnabled, setting.DimBaseEnabled);
        var wantPedestal = setting.DimPedestalEnabled && geometry.Pedestal != null;
        if (!wantOverall && !wantBase && !wantPedestal) return 0;

        var center = ProjectToView(new XYZ(geometry.BaseCenter.X, geometry.BaseCenter.Y, geometry.BaseCenter.Z), view);
        var dirX = new XYZ(geometry.DirX.X, geometry.DirX.Y, geometry.DirX.Z).Normalize();
        var dirY = new XYZ(geometry.DirY.X, geometry.DirY.Y, geometry.DirY.Z).Normalize();
        var dimType = dimensionTypeId != null && dimensionTypeId != ElementId.InvalidElementId ? doc.GetElement(dimensionTypeId) as DimensionType : null;
        var faces = CollectVerticalFaces(footing);
        var foundationFaces = CollectNearCenterVerticalFaces(doc, center, BuiltInCategory.OST_StructuralFoundation);
        var columnFaces = CollectNearCenterVerticalFaces(doc, center, BuiltInCategory.OST_StructuralColumns);
        center = AlignCenterToBaseFaces(center, faces, dirX, dirY, geometry.WidthXFeet / 2, geometry.WidthYFeet / 2);

        return PlaceAxis(doc, view, dimType, center, dirX, -dirY, faces, foundationFaces, columnFaces,
            geometry.WidthXFeet / 2, geometry.WidthYFeet / 2,
            geometry.Pedestal?.WidthXFeet / 2, geometry.Pedestal?.WidthYFeet / 2,
            wantOverall, wantBase, wantPedestal, warnings)
            + PlaceAxis(doc, view, dimType, center, dirY, -dirX, faces, foundationFaces, columnFaces,
            geometry.WidthYFeet / 2, geometry.WidthXFeet / 2,
            geometry.Pedestal?.WidthYFeet / 2, geometry.Pedestal?.WidthXFeet / 2,
            wantOverall, wantBase, wantPedestal, warnings);
    }

    private static int PlaceAxis(Document doc, View view, DimensionType? dimType,
        XYZ center, XYZ axis, XYZ offsetDir, IReadOnlyList<FaceRef> faces,
        IReadOnlyList<FaceRef> foundationFaces, IReadOnlyList<FaceRef> columnFaces,
        double baseHalf, double offsetHalf, double? pedestalHalf, double? pedestalOffsetHalf, bool wantOverall,
        bool wantBase, bool wantPedestal, List<string> warnings)
    {
        var placed = 0;
        var lane = 0;
        var axisFaces = ClusterFaces(faces, axis, center);
        if (axisFaces.Count < 2)
        {
            warnings.Add($"Không đủ mặt thật theo phương dimension trong '{view.Name}'.");
            return 0;
        }

        if (wantPedestal && pedestalHalf is > 0 && pedestalOffsetHalf is > 0)
        {
            var pedestalFaces = SelectPairByHalfWidth(faces, axis, center, pedestalHalf.Value);
            if (pedestalFaces.Count < 2) pedestalFaces = SelectPairByHalfWidth(foundationFaces, axis, center, pedestalHalf.Value);

            var coreHalf = System.Math.Max(pedestalHalf.Value - BlindingOffsetFeet / 2, 0);
            var coreFaces = coreHalf > 0 ? SelectPairByHalfWidth(columnFaces, axis, center, coreHalf) : [];
            if (coreFaces.Count < 2 && coreHalf > 0) coreFaces = SelectPairByHalfWidth(faces, axis, center, coreHalf);
            var pedestalChain = MergeFaces(pedestalFaces, coreFaces, axis);

            if (pedestalChain.Count >= 4)
                placed += TryFaceDimension(doc, view, dimType, pedestalChain, axis, offsetDir, center,
                    pedestalHalf.Value + PedestalDimGapFeet, pedestalOffsetHalf.Value + PedestalDimGapFeet, "cổ", warnings);
            else if (pedestalFaces.Count >= 2)
                warnings.Add($"Dimension 'cổ' thiếu 2 mặt cột/lõi giữa để ra chuỗi 50-...-50 trong '{view.Name}'.");
            else
                warnings.Add($"Không tìm thấy đủ 2 mặt cổ móng thật theo phương '{axis}' để dựng dimension cổ.");
        }

        if (wantBase)
        {
            var pedestalFaces = pedestalHalf is > 0
                ? SelectPairByHalfWidth(faces, axis, center, pedestalHalf.Value)
                : [];
            if (pedestalFaces.Count < 2 && pedestalHalf is > 0)
                pedestalFaces = SelectPairByHalfWidth(foundationFaces, axis, center, pedestalHalf.Value);
            axisFaces = MergeFaces(axisFaces, pedestalFaces, axis);
            var lineHalf = axisFaces.Max(f => System.Math.Abs((f.Origin - center).DotProduct(axis)));
            placed += TryFaceDimension(doc, view, dimType, axisFaces, axis, offsetDir, center,
                lineHalf, offsetHalf + LaneFeet * (++lane), "đế", warnings);
        }

        if (wantOverall)
        {
            var footingHalf = System.Math.Max(baseHalf - BlindingOffsetFeet, 0);
            var baseFaces = SelectPlanarPairByHalfWidth(faces, axis, center, footingHalf);
            if (baseFaces.Count >= 2)
            {
                placed += TryFaceDimension(doc, view, dimType, baseFaces, axis, offsetDir, center,
                    footingHalf, offsetHalf + LaneFeet * (++lane), "bao", warnings);
            }
            else
            {
                warnings.Add("Không tìm thấy đủ 2 mặt móng chính sau khi trừ bê tông lót 100mm để dựng dimension bao.");
            }
        }

        return placed;
    }

    private static int TryFaceDimension(Document doc, View view, DimensionType? dimType, IReadOnlyList<FaceRef> faces,
        XYZ axis, XYZ offsetDir, XYZ center, double lineHalfLength, double offsetDist, string label,
        List<string> warnings)
    {
        try
        {
            var refs = new ReferenceArray();
            foreach (var face in faces.OrderBy(f => f.Origin.DotProduct(axis)))
                refs.Append(face.Reference);

            var lineCenter = center + offsetDir * offsetDist;
            var line = Line.CreateBound(lineCenter - axis * lineHalfLength, lineCenter + axis * lineHalfLength);
            var dim = dimType != null
                ? doc.Create.NewDimension(view, line, refs, dimType)
                : doc.Create.NewDimension(view, line, refs);

            doc.Regenerate();
            if (dim?.get_BoundingBox(view) != null) return 1;

            warnings.Add($"Dimension '{label}' đã tạo nhưng không hiển thị trong '{view.Name}'.");
            return 0;
        }
        catch (Exception ex)
        {
            warnings.Add($"Không dựng được dimension '{label}': {ex.Message}");
            return 0;
        }
    }

    private static XYZ ProjectToView(XYZ point, View view)
    {
        var normal = view.ViewDirection.Normalize();
        return point - normal * normal.DotProduct(point - view.Origin);
    }

    private static List<FaceRef> ClusterFaces(IReadOnlyList<FaceRef> faces, XYZ axis, XYZ center)
    {
        var ordered = faces
            .Where(f => System.Math.Abs(f.Normal.DotProduct(axis)) > 0.9)
            .OrderBy(f => f.Origin.DotProduct(axis))
            .ToList();
        var result = new List<FaceRef>();

        foreach (var face in ordered)
        {
            if (result.Count == 0) { result.Add(face); continue; }

            var prev = result[^1];
            var distance = System.Math.Abs(face.Origin.DotProduct(axis) - prev.Origin.DotProduct(axis));
            if (distance > ClusterTolFeet) { result.Add(face); continue; }

            if (System.Math.Abs(face.Origin.DistanceTo(center)) < System.Math.Abs(prev.Origin.DistanceTo(center)))
                result[^1] = face;
        }

        return result;
    }

    private static XYZ AlignCenterToBaseFaces(XYZ center, IReadOnlyList<FaceRef> faces, XYZ dirX, XYZ dirY,
        double halfX, double halfY)
    {
        var aligned = center;
        aligned = AlignOneAxis(aligned, faces, dirX, halfX);
        aligned = AlignOneAxis(aligned, faces, dirY, halfY);
        return aligned;
    }

    private static XYZ AlignOneAxis(XYZ center, IReadOnlyList<FaceRef> faces, XYZ axis, double targetHalfWidth)
    {
        var pair = SelectPairByHalfWidth(faces, axis, center, targetHalfWidth);
        if (pair.Count < 2) return center;

        var actualCenter = pair.Average(f => f.Origin.DotProduct(axis));
        var delta = actualCenter - center.DotProduct(axis);
        return center + axis * delta;
    }

    private static List<FaceRef> SelectPairByHalfWidth(IReadOnlyList<FaceRef> faces, XYZ axis, XYZ center,
        double targetHalfWidth)
    {
        var alongAxis = faces.Where(f => System.Math.Abs(f.Normal.DotProduct(axis)) > 0.9).ToList();
        var negative = alongAxis
            .Where(f => f.Origin.DotProduct(axis) < center.DotProduct(axis))
            .OrderBy(f => System.Math.Abs(System.Math.Abs((f.Origin - center).DotProduct(axis)) - targetHalfWidth))
            .FirstOrDefault();
        var positive = alongAxis
            .Where(f => f.Origin.DotProduct(axis) > center.DotProduct(axis))
            .OrderBy(f => System.Math.Abs(System.Math.Abs((f.Origin - center).DotProduct(axis)) - targetHalfWidth))
            .FirstOrDefault();

        return negative != null && positive != null ? [negative, positive] : [];
    }

    private static List<FaceRef> MergeFaces(IReadOnlyList<FaceRef> first, IReadOnlyList<FaceRef> second, XYZ axis)
    {
        return first.Concat(second)
            .GroupBy(f => f.Reference.ElementId.ToLong() + ":" + f.Origin.DotProduct(axis).ToString("F6"))
            .Select(g => g.First())
            .OrderBy(f => f.Origin.DotProduct(axis))
            .ToList();
    }

    private sealed record FaceRef(Reference Reference, XYZ Normal, XYZ Origin, bool IsPlanarFace);

    private static List<FaceRef> CollectVerticalFaces(Element footing)
    {
        var result = new List<FaceRef>(); var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        var geom = footing.get_Geometry(opt);
        if (geom == null) return result;

        CollectVerticalFaces(geom, Transform.Identity, result);
        return result;
    }

    private static List<FaceRef> CollectNearCenterVerticalFaces(Document doc, XYZ center, BuiltInCategory category)
    {
        const double searchRadiusFeet = 2.0;
        var result = new List<FaceRef>();
        var elements = new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .Where(e => ElementProximity.IsNearCenter(e, center, searchRadiusFeet));
        foreach (var element in elements) CollectVerticalFaces(element, result);

        return result;
    }

    private static void CollectVerticalFaces(Element element, List<FaceRef> result)
    {
        var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        var geom = element.get_Geometry(opt);
        if (geom == null) return;

        CollectVerticalFaces(geom, Transform.Identity, result);
    }

    private static void CollectVerticalFaces(GeometryElement geom, Transform transform, List<FaceRef> result)
    {
        foreach (var obj in geom)
        {
            switch (obj)
            {
                case Solid { Volume: > 1e-9 } solid:
                    CollectVerticalFaces(solid, transform, result);
                    break;
                case GeometryInstance gi:
                    CollectVerticalFaces(gi.GetSymbolGeometry(), transform.Multiply(gi.Transform), result);
                    break;
                case Line line when line.Reference != null:
                    CollectCurveReference(line.Reference, line, transform, result);
                    break;
            }
        }
    }

    private static void CollectVerticalFaces(Solid solid, Transform transform, List<FaceRef> result)
    {
        foreach (Face face in solid.Faces)
        {
            if (face is not PlanarFace { Reference: not null } pf) continue;

            var normal = transform.OfVector(pf.FaceNormal).Normalize();
            if (System.Math.Abs(normal.Z) >= 0.1) continue;

            result.Add(new FaceRef(pf.Reference, normal, transform.OfPoint(pf.Origin), true));
        }

        foreach (Edge edge in solid.Edges)
        {
            if (edge.Reference == null) continue;
            if (edge.AsCurve() is Line line)
                CollectCurveReference(edge.Reference, line, transform, result);
        }
    }

    private static void CollectCurveReference(Reference reference, Line line, Transform transform, List<FaceRef> result)
    {
        var start = transform.OfPoint(line.GetEndPoint(0));
        var end = transform.OfPoint(line.GetEndPoint(1));
        var direction = (end - start).Normalize();
        if (System.Math.Abs(direction.Z) > 0.1) return;

        var normal = XYZ.BasisZ.CrossProduct(direction).Normalize();
        var origin = (start + end) * 0.5;
        result.Add(new FaceRef(reference, normal, origin, false));
    }

    private static List<FaceRef> SelectPlanarPairByHalfWidth(IReadOnlyList<FaceRef> faces, XYZ axis, XYZ center,
        double targetHalfWidth)
    {
        var planar = faces.Where(f => f.IsPlanarFace).ToList();
        return SelectPairByHalfWidth(planar, axis, center, targetHalfWidth);
    }

}
