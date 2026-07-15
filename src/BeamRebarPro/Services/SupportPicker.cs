using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BeamRebarPro.Models;

namespace BeamRebarPro.Services;

/// <summary>
///     Picks columns or framing members that act as internal supports for one long beam run.
/// </summary>
public sealed class SupportPicker
{
    public IReadOnlyList<Point3> PickSupportPoints(UIDocument uiDocument)
        => PickSupportInfos(uiDocument, null).Select(s => s.Location).ToList();

    public IReadOnlyList<SupportInfo> PickSupportInfos(
        UIDocument uiDocument,
        IReadOnlyList<FamilyInstance>? mainBeams)
    {
        var doc = uiDocument.Document;
        try
        {
            var refs = uiDocument.Selection.PickObjects(
                ObjectType.Element,
                new SupportSelectionFilter(),
                "Chọn cột/gối nằm trên tuyến dầm. Nhấn Finish khi xong.");

            var mainLines = ResolveMainBeamLines(mainBeams);
            var fallbackAxis = ResolveMainBeamAxis(mainLines.FirstOrDefault());
            return refs
                .Select(r => ToSupportInfo(doc.GetElement(r), mainLines, fallbackAxis))
                .Where(i => i is not null)
                .Select(i => i!)
                .ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return [];
        }
    }

    public IReadOnlyList<SecondaryBeamInfo> PickSecondaryBeams(
        UIDocument uiDocument,
        IReadOnlyList<FamilyInstance>? mainBeams)
    {
        var doc = uiDocument.Document;
        try
        {
            var refs = uiDocument.Selection.PickObjects(
                ObjectType.Element,
                new SupportSelectionFilter(),
                "Chon dam phu gac len dam chinh. Nhan Finish khi xong.");

            var mainLines = ResolveMainBeamLines(mainBeams);
            var fallbackAxis = ResolveMainBeamAxis(mainLines.FirstOrDefault());
            return refs
                .Select(r => doc.GetElement(r))
                .Select(e => ToSecondaryBeamInfo(e, mainLines, fallbackAxis))
                .Where(i => i is not null)
                .Select(i => i!)
                .ToList();
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return [];
        }
    }

    private static XYZ? GetRepresentativePoint(Element element)
    {
        if (element.Location is LocationPoint locationPoint)
            return locationPoint.Point;

        if (element.Location is LocationCurve { Curve: Line line })
            return (line.GetEndPoint(0) + line.GetEndPoint(1)) / 2.0;

        var bbox = element.get_BoundingBox(null);
        return bbox is null ? null : (bbox.Min + bbox.Max) / 2.0;
    }

    private static SecondaryBeamInfo? ToSecondaryBeamInfo(Element element, IReadOnlyList<Line> mainLines, XYZ fallbackAxis)
    {
        var point = TryGetBestMainSecondaryIntersection(mainLines, element, out var intersection, out var mainAxis)
            ? intersection
            : GetRepresentativePoint(element);
        if (point is null) return null;

        if (mainAxis is null)
            mainAxis = fallbackAxis;

        if (TryGetSecondaryProjectionOnMainAxis(element, mainAxis, out var projectionMid, out var projectionHalfWidth))
        {
            var currentProjection = point.DotProduct(mainAxis);
            point += mainAxis * (projectionMid - currentProjection);
            return new SecondaryBeamInfo(new Point3(point.X, point.Y, point.Z), projectionHalfWidth);
        }

        var halfWidth = TryGetSecondaryHalfWidthOnMainAxis(element, mainAxis, out var exactHalfWidth)
            ? exactHalfWidth
            : HalfWidthAlong(element.get_BoundingBox(null), mainAxis);
        return new SecondaryBeamInfo(new Point3(point.X, point.Y, point.Z), halfWidth);
    }

    private static SupportInfo? ToSupportInfo(Element element, IReadOnlyList<Line> mainLines, XYZ fallbackAxis)
    {
        var point = TryGetBestMainSecondaryIntersection(mainLines, element, out var intersection, out var mainAxis)
            ? intersection
            : GetRepresentativePoint(element);
        if (point is null) return null;

        mainAxis ??= fallbackAxis;

        if (TryGetSecondaryProjectionOnMainAxis(element, mainAxis, out var projectionMid, out var projectionHalfWidth))
        {
            var currentProjection = point.DotProduct(mainAxis);
            point += mainAxis * (projectionMid - currentProjection);
            return new SupportInfo(new Point3(point.X, point.Y, point.Z), projectionHalfWidth);
        }

        var halfWidth = TryGetSecondaryHalfWidthOnMainAxis(element, mainAxis, out var exactHalfWidth)
            ? exactHalfWidth
            : HalfWidthAlong(element.get_BoundingBox(null), mainAxis);
        return new SupportInfo(new Point3(point.X, point.Y, point.Z), halfWidth);
    }

    private static IReadOnlyList<Line> ResolveMainBeamLines(IReadOnlyList<FamilyInstance>? mainBeams)
    {
        return mainBeams?
            .Select(b => b.Location)
            .OfType<LocationCurve>()
            .Select(l => l.Curve)
            .OfType<Line>()
            .ToList() ?? [];
    }

    private static XYZ ResolveMainBeamAxis(Line? line)
    {
        if (line is not null)
        {
            var axis = line.GetEndPoint(1) - line.GetEndPoint(0);
            var xy = new XYZ(axis.X, axis.Y, 0);
            if (xy.GetLength() > 1e-6)
                return xy.Normalize();
        }

        return XYZ.BasisX;
    }

    private static bool TryGetBestMainSecondaryIntersection(
        IReadOnlyList<Line> mainLines,
        Element secondaryElement,
        out XYZ point,
        out XYZ? mainAxis)
    {
        point = XYZ.Zero;
        mainAxis = null;
        if (mainLines.Count == 0 || secondaryElement.Location is not LocationCurve { Curve: Line secondaryLine })
            return false;

        var bestDistance = double.MaxValue;
        XYZ? bestPoint = null;
        XYZ? bestAxis = null;
        foreach (var mainLine in mainLines)
        {
            if (!TryIntersectLines2D(mainLine, secondaryLine, out var candidate)) continue;

            var mainDistance = DistanceToSegment2D(candidate, mainLine);
            var secondaryDistance = DistanceToSegment2D(candidate, secondaryLine);
            var score = mainDistance + secondaryDistance;
            if (score >= bestDistance) continue;

            bestDistance = score;
            bestPoint = candidate;
            bestAxis = ResolveMainBeamAxis(mainLine);
        }

        if (bestPoint is null || bestAxis is null)
            return false;

        point = bestPoint;
        mainAxis = bestAxis;
        return true;
    }

    private static bool TryIntersectLines2D(Line a, Line b, out XYZ point)
    {
        point = XYZ.Zero;
        var p = a.GetEndPoint(0);
        var r = a.GetEndPoint(1) - p;
        var q = b.GetEndPoint(0);
        var s = b.GetEndPoint(1) - q;
        var denominator = Cross2D(r, s);
        if (Math.Abs(denominator) < 1e-9) return false;

        var t = Cross2D(q - p, s) / denominator;
        point = p + r * t;
        return true;
    }

    private static double DistanceToSegment2D(XYZ point, Line segment)
    {
        var a = segment.GetEndPoint(0);
        var b = segment.GetEndPoint(1);
        var ab = b - a;
        var len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 <= 1e-12) return Distance2D(point, a);

        var t = ((point.X - a.X) * ab.X + (point.Y - a.Y) * ab.Y) / len2;
        t = Math.Clamp(t, 0, 1);
        var closest = new XYZ(a.X + ab.X * t, a.Y + ab.Y * t, point.Z);
        return Distance2D(point, closest);
    }

    private static double Distance2D(XYZ a, XYZ b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Cross2D(XYZ a, XYZ b) => a.X * b.Y - a.Y * b.X;

    private static bool TryGetSecondaryHalfWidthOnMainAxis(Element element, XYZ mainAxis, out double halfWidthFeet)
    {
        halfWidthFeet = 0;
        if (element is not FamilyInstance familyInstance
            || element.Location is not LocationCurve { Curve: Line secondaryLine })
            return false;

        var widthFeet = ReadWidthFeet(familyInstance);
        if (widthFeet <= 0) return false;

        var secondaryAxis = secondaryLine.GetEndPoint(1) - secondaryLine.GetEndPoint(0);
        var secondaryAxisXy = new XYZ(secondaryAxis.X, secondaryAxis.Y, 0);
        if (secondaryAxisXy.GetLength() <= 1e-6) return false;

        secondaryAxisXy = secondaryAxisXy.Normalize();
        var secondaryAcross = secondaryAxisXy.CrossProduct(XYZ.BasisZ);
        if (secondaryAcross.GetLength() <= 1e-6) return false;

        secondaryAcross = secondaryAcross.Normalize();
        var projection = Math.Abs(mainAxis.DotProduct(secondaryAcross));
        if (projection <= 1e-3) return false;

        halfWidthFeet = widthFeet / 2.0 / projection;
        return true;
    }

    private static bool TryGetSecondaryProjectionOnMainAxis(
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
                    var instanceGeometry = instance.GetInstanceGeometry();
                    var instancePoints = new List<XYZ>();
                    CollectGeometryPoints(instanceGeometry, instancePoints);
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

    private static double ReadWidthFeet(FamilyInstance familyInstance)
    {
        foreach (var name in new[] { "b", "B", "Width", "width", "WIDTH", "Chieu rong", "Chiều rộng" })
        {
            var instanceValue = ReadDoubleParameter(familyInstance, name);
            if (instanceValue > 0) return instanceValue;

            var symbolValue = ReadDoubleParameter(familyInstance.Symbol, name);
            if (symbolValue > 0) return symbolValue;
        }

        return 0;
    }

    private static double ReadDoubleParameter(Element element, string name)
    {
        var param = element.LookupParameter(name);
        return param is { StorageType: StorageType.Double } && param.AsDouble() > 0
            ? param.AsDouble()
            : 0;
    }

    private static double HalfWidthAlong(BoundingBoxXYZ? bbox, XYZ axisXy)
    {
        if (bbox is null) return 0;
        var size = bbox.Max - bbox.Min;
        var projected = Math.Abs(size.X * axisXy.X) + Math.Abs(size.Y * axisXy.Y);
        return projected / 2;
    }
}

public sealed class SupportSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        var categoryId = elem.Category?.Id.ToValue();
        return elem is FamilyInstance
               && (categoryId == (long)BuiltInCategory.OST_StructuralColumns
                   || categoryId == (long)BuiltInCategory.OST_Columns
                   || categoryId == (long)BuiltInCategory.OST_StructuralFraming);
    }

    public bool AllowReference(Reference reference, XYZ position) => false;
}
