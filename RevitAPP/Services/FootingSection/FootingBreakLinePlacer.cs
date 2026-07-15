using Autodesk.Revit.DB;
using RevitAPP.Core.Models.FootingSection;

namespace RevitAPP.Services.FootingSection;

/// <summary>
///     Đặt break line NGANG cắt cột phía trên mặt cắt móng (bẻ gãy cột cao). Dùng family line-based
///     (CurveBasedDetail). Đường bẻ đặt gần đỉnh crop, chạy ngang theo phương right, rộng hơn cột một chút.
///     Best-effort: thiếu family / không hợp lệ → warn, không chặn. PHẢI gọi trong Transaction (view đã regenerate).
/// </summary>
public sealed class FootingBreakLinePlacer
{
    private const double MmPerFoot = 304.8;
    /// <summary>Đường bẻ cách đỉnh crop xuống dưới (feet ~ 150mm) để chừa đoạn cột trên đường bẻ.</summary>
    private const double BelowTopFeet = 150.0 / MmPerFoot;
    private const double FallbackHalfLineFeet = 300.0 / MmPerFoot;

    public int Place(Document doc, ViewSection view, FootingSectionGeometry geometry, ElementId? symbolId,
        List<string> warnings)
    {
        if (symbolId == null || symbolId == ElementId.InvalidElementId)
        {
            warnings.Add("Chưa chọn Break Line — bỏ qua cắt cột.");
            return 0;
        }
        if (doc.GetElement(symbolId) is not FamilySymbol symbol)
        {
            warnings.Add("Break Line đã chọn không phải Detail Component type.");
            return 0;
        }
        if (symbol.Family.FamilyPlacementType != FamilyPlacementType.CurveBasedDetail)
        {
            warnings.Add($"Break Line '{symbol.FamilyName}' không phải line-based (CurveBasedDetail) — bỏ qua.");
            return 0;
        }

        try
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            var right = Normalize(view.RightDirection);
            // Cao độ đường bẻ: dưới đỉnh section (topZ) một đoạn để cột "gãy" ngay dưới tầng 1.
            var breakZ = geometry.TopZFeet - BelowTopFeet;
            var tim = new XYZ(geometry.Center.X, geometry.Center.Y, breakZ);
            var halfLineFeet = GetColumnHalfWidth(doc, view, tim, right);

            var p0 = tim - right * halfLineFeet;
            var p1 = tim + right * halfLineFeet;
            if (p0.DistanceTo(p1) < 1e-6)
            {
                warnings.Add($"Đường break line quá ngắn ở view '{view.Name}'.");
                return 0;
            }

            CreateBreakLine(doc, view, symbol, p0, p1, tim, allowMirrorFallback: true);
            return 1 + PlaceBeamBreakLines(doc, view, symbol, warnings);
        }
        catch (Exception ex)
        {
            warnings.Add($"Không đặt được Break Line ở view '{view.Name}': {ex.Message}");
            return 0;
        }
    }

    private static double GetColumnHalfWidth(Document doc, ViewSection view, XYZ center, XYZ right)
    {
        var centerProjection = center.DotProduct(right);
        var tolerance = 20.0 / MmPerFoot;
        var candidates = new List<(double Distance, double HalfWidth)>();
        foreach (var column in new FilteredElementCollector(doc, view.Id)
                     .OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType())
        {
            var box = column.get_BoundingBox(view) ?? column.get_BoundingBox(null);
            if (box == null) continue;
            var corners = BoxCorners(box).ToList();
            var minZ = corners.Min(point => point.Z);
            var maxZ = corners.Max(point => point.Z);
            if (center.Z < minZ - tolerance || center.Z > maxZ + tolerance) continue;
            var min = corners.Min(point => point.DotProduct(right));
            var max = corners.Max(point => point.DotProduct(right));
            if (centerProjection < min - tolerance || centerProjection > max + tolerance) continue;
            candidates.Add((Math.Abs((min + max) * 0.5 - centerProjection), (max - min) * 0.5));
        }

        var best = candidates.OrderBy(item => item.Distance).FirstOrDefault();
        return best.HalfWidth > 1e-6 ? best.HalfWidth : FallbackHalfLineFeet;
    }

    private static int PlaceBeamBreakLines(Document doc, ViewSection view, FamilySymbol symbol,
        List<string> warnings)
    {
        var crop = view.CropBox;
        if (!view.CropBoxActive || crop == null) return 0;
        var inverse = crop.Transform.Inverse;
        var inset = 100.0 / MmPerFoot;
        var tolerance = 20.0 / MmPerFoot;
        var placed = 0;
        var used = new List<(double X, double Y)>();

        var beams = new FilteredElementCollector(doc, view.Id)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType()
            .ToElements();

        foreach (var beam in beams)
        {
            var box = beam.get_BoundingBox(view) ?? beam.get_BoundingBox(null);
            if (box == null) continue;
            var physicalPoints = GetPhysicalGeometryPoints(beam, view);
            var localCorners = (physicalPoints.Count > 0 ? physicalPoints : BoxCorners(box).ToList())
                .Select(inverse.OfPoint)
                .ToList();
            var minX = localCorners.Min(point => point.X);
            var maxX = localCorners.Max(point => point.X);
            var minY = localCorners.Min(point => point.Y);
            var maxY = localCorners.Max(point => point.Y);
            if (maxY <= crop.Min.Y || minY >= crop.Max.Y) continue;

            var visibleMinX = Math.Max(minX, crop.Min.X);
            var visibleMaxX = Math.Min(maxX, crop.Max.X);
            if (visibleMaxX - visibleMinX < 300.0 / MmPerFoot) continue;
            var centerY = (minY + maxY) * 0.5;

            if (minX <= crop.Min.X + tolerance)
                placed += PlaceAtLocalX(crop, doc, view, symbol, visibleMinX + inset,
                    minY, maxY, centerY, used, flipFacing: true);
            if (maxX >= crop.Max.X - tolerance)
                placed += PlaceAtLocalX(crop, doc, view, symbol, visibleMaxX - inset,
                    minY, maxY, centerY, used, flipFacing: false);
        }

        if (placed > 0) warnings.Add($"Break Line: đã cắt thêm {placed} mép dầm trong view '{view.Name}'.");
        return placed;
    }

    private static List<XYZ> GetPhysicalGeometryPoints(Element element, View view)
    {
        var points = new List<XYZ>();
        try
        {
            var geometry = element.get_Geometry(new Options
            {
                View = view,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            });
            if (geometry != null) CollectPhysicalPoints(geometry, Transform.Identity, points);
        }
        catch { }
        return points;
    }

    private static void CollectPhysicalPoints(GeometryElement geometry, Transform transform, List<XYZ> points)
    {
        foreach (var geometryObject in geometry)
        {
            if (geometryObject is Solid { Volume: > 1e-9 } solid)
            {
                foreach (Edge edge in solid.Edges)
                {
                    var curve = edge.AsCurve();
                    points.Add(transform.OfPoint(curve.GetEndPoint(0)));
                    points.Add(transform.OfPoint(curve.GetEndPoint(1)));
                }
            }
            else if (geometryObject is GeometryInstance instance)
            {
                var symbolGeometry = instance.GetSymbolGeometry();
                if (symbolGeometry != null)
                    CollectPhysicalPoints(symbolGeometry, transform.Multiply(instance.Transform), points);
            }
        }
    }

    private static int PlaceAtLocalX(BoundingBoxXYZ crop, Document doc, ViewSection view, FamilySymbol symbol,
        double x, double minY, double maxY, double centerY, List<(double X, double Y)> used, bool flipFacing)
    {
        var duplicateTolerance = 25.0 / MmPerFoot;
        if (used.Any(item => Math.Abs(item.X - x) < duplicateTolerance &&
                             Math.Abs(item.Y - centerY) < duplicateTolerance)) return 0;
        used.Add((x, centerY));
        var p0 = crop.Transform.OfPoint(new XYZ(x, minY, 0));
        var p1 = crop.Transform.OfPoint(new XYZ(x, maxY, 0));
        var midpoint = (p0 + p1) * 0.5;
        CreateBreakLine(doc, view, symbol, p0, p1, midpoint, allowMirrorFallback: false, flipFacing);
        return 1;
    }

    private static void CreateBreakLine(Document doc, ViewSection view, FamilySymbol symbol,
        XYZ p0, XYZ p1, XYZ midpoint, bool allowMirrorFallback, bool flipFacing = true)
    {
        var instance = doc.Create.NewFamilyInstance(Line.CreateBound(p0, p1), symbol, view);
        if (flipFacing && instance.CanFlipFacing)
        {
            instance.flipFacing();
        }
        else if (flipFacing && allowMirrorFallback)
        {
            var mirrorPlane = Plane.CreateByNormalAndOrigin(Normalize(view.UpDirection), midpoint);
            ElementTransformUtils.MirrorElement(doc, instance.Id, mirrorPlane);
        }
    }

    private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ box)
    {
        for (var x = 0; x <= 1; x++)
        for (var y = 0; y <= 1; y++)
        for (var z = 0; z <= 1; z++)
        {
            var local = new XYZ(x == 0 ? box.Min.X : box.Max.X,
                y == 0 ? box.Min.Y : box.Max.Y,
                z == 0 ? box.Min.Z : box.Max.Z);
            yield return box.Transform.OfPoint(local);
        }
    }

    private static XYZ Normalize(XYZ vector)
    {
        var length = vector.GetLength();
        return length < 1e-9 ? XYZ.BasisX : vector / length;
    }
}
