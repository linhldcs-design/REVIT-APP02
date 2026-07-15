using Autodesk.Revit.DB;
using RevitAPP.Core.Services;

namespace RevitAPP.Services.BeamDrawing;

/// <summary>
/// Places line-based break symbols at the actual slab section around a beam. Both position and line length are
/// derived from the floor solid at the current cross-section station, so sloped and one-sided slabs remain correct.
/// </summary>
public sealed class BreakLinePlacer
{
    private const double SlabNearTopFeet = 300.0 / 304.8;
    private const double PreferredStubFeet = 40.0 / 304.8;
    private const double MinimumOverhangFeet = 5.0 / 304.8;
    private const double CropPastLineFeet = 50.0 / 304.8;

    public int Place(Document doc, View view, ViewBeamPair pair, ElementId? symbolId, List<string> warnings)
    {
        if (symbolId == null || symbolId == ElementId.InvalidElementId) return 0;
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

            var crop = view.CropBox;
            var transform = crop.Transform;
            var inverse = transform.Inverse;
            var (beamLeft, beamRight) = BeamHorizontalBounds(pair.Beam, view, inverse, crop);
            if (!TryGetSlabSides(doc, view, pair, inverse, beamLeft, beamRight,
                    out var leftSide, out var rightSide)) return 0;
            var placed = 0;
            double? placedLeft = null;
            double? placedRight = null;

            if (leftSide is { } left &&
                TryVerticalRangeAtLocalX(left.Solid, transform, inverse, left.X, left.BottomZ, left.TopZ,
                    out var leftBottom, out var leftTop))
            {
                placed += DrawLine(doc, view, symbol, transform, left.X,
                    LocalYFromWorldZ(transform, inverse, leftTop),
                    LocalYFromWorldZ(transform, inverse, leftBottom));
                placedLeft = left.X;
            }

            if (rightSide is { } right &&
                TryVerticalRangeAtLocalX(right.Solid, transform, inverse, right.X, right.BottomZ, right.TopZ,
                    out var rightBottom, out var rightTop))
            {
                placed += DrawLine(doc, view, symbol, transform, right.X,
                    LocalYFromWorldZ(transform, inverse, rightBottom),
                    LocalYFromWorldZ(transform, inverse, rightTop));
                placedRight = right.X;
            }

            if (placed > 0) FitCropToBreakLine(view, placedLeft, placedRight);
            return placed;
        }
        catch (Exception ex)
        {
            warnings.Add($"Không đặt được Break Line ở view '{view.Name}': {ex.Message}");
            return 0;
        }
    }

    private static (double Left, double Right) BeamHorizontalBounds(FamilyInstance beam, View view,
        Transform inverse, BoundingBoxXYZ crop)
    {
        var box = beam.get_BoundingBox(view);
        if (box == null) return (crop.Min.X, crop.Max.X);
        var xs = BoxCorners(box).Select(point => inverse.OfPoint(point).X).ToArray();
        return (xs.Min(), xs.Max());
    }

    private static int DrawLine(Document doc, View view, FamilySymbol symbol, Transform transform,
        double localX, double firstY, double secondY)
    {
        var p0 = transform.OfPoint(new XYZ(localX, firstY, 0));
        var p1 = transform.OfPoint(new XYZ(localX, secondY, 0));
        doc.Create.NewFamilyInstance(Line.CreateBound(p0, p1), symbol, view);
        return 1;
    }

    private static void FitCropToBreakLine(View view, double? leftLineX, double? rightLineX)
    {
        var crop = view.CropBox;
        if (leftLineX is { } left) crop.Min = new XYZ(left - CropPastLineFeet, crop.Min.Y, crop.Min.Z);
        if (rightLineX is { } right) crop.Max = new XYZ(right + CropPastLineFeet, crop.Max.Y, crop.Max.Z);
        view.CropBox = crop;
        view.CropBoxVisible = false;
    }

    private static double LocalYFromWorldZ(Transform transform, Transform inverse, double worldZ) =>
        inverse.OfPoint(new XYZ(transform.Origin.X, transform.Origin.Y, worldZ)).Y;

    private sealed record SlabSide(Solid Solid, double BottomZ, double TopZ, double X);

    private static bool TryGetSlabSides(Document doc, View view, ViewBeamPair pair, Transform inverse,
        double beamLeft, double beamRight, out SlabSide? leftSide, out SlabSide? rightSide)
    {
        leftSide = null;
        rightSide = null;
        var beamTop = pair.Geometry.TopZFeet;
        var station = StationPoint(pair, beamTop);
        var probe = 2.0 / 304.8;
        var beamBottom = pair.Geometry.BottomZFeet;
        var outline = new Outline(
            // Sàn hạ cốt được phép nằm ở bất kỳ cao độ nào trong chiều cao dầm.
            new XYZ(station.X - probe, station.Y - probe, beamBottom - probe),
            new XYZ(station.X + probe, station.Y + probe, beamTop + SlabNearTopFeet * 2));
        var floors = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(outline))
            .ToList();

        var right = new XYZ(view.RightDirection.X, view.RightDirection.Y, 0);
        if (right.GetLength() < 1e-9) return false;
        right = right.Normalize();
        var bestLeftDistance = double.MaxValue;
        var bestRightDistance = double.MaxValue;
        var geometryOptions = new Options { IncludeNonVisibleObjects = true };

        foreach (var floor in floors)
        {
            var geometry = floor.get_Geometry(geometryOptions);
            if (geometry == null) continue;
            foreach (var solid in CollectSolids(geometry))
            {
                if (!TryVerticalRange(solid, station.X, station.Y, beamTop, out var bottom, out var top)) continue;
                // Không giả định top/bottom sàn trùng top dầm. Chấp nhận sàn hạ cốt miễn còn giao dải cao dầm;
                // chọn solid có khoảng Z gần đỉnh dầm nhất nếu nhiều Floor chồng tại cùng station.
                if (top < beamBottom - probe || bottom > beamTop + SlabNearTopFeet) continue;
                var distance = DistanceToInterval(beamTop, bottom, top);
                if (!TryHorizontalRange(solid, station, (bottom + top) * 0.5, right, inverse,
                        beamLeft, beamRight, out var left, out var rightX)) continue;
                var positions = SlabBreakLineMath.Calculate(beamLeft, beamRight, left, rightX,
                    PreferredStubFeet, MinimumOverhangFeet);
                if (positions.LeftX is { } leftX && distance < bestLeftDistance)
                {
                    bestLeftDistance = distance;
                    leftSide = new SlabSide(solid, bottom, top, leftX);
                }
                if (positions.RightX is { } sideRightX && distance < bestRightDistance)
                {
                    bestRightDistance = distance;
                    rightSide = new SlabSide(solid, bottom, top, sideRightX);
                }
            }
        }

        return leftSide != null || rightSide != null;
    }

    private static double DistanceToInterval(double value, double low, double high) =>
        value < low ? low - value : value > high ? value - high : 0;

    private static XYZ StationPoint(ViewBeamPair pair, double z)
    {
        var t = pair.Station ?? 0.5;
        return new XYZ(
            pair.Geometry.Start.X + (pair.Geometry.End.X - pair.Geometry.Start.X) * t,
            pair.Geometry.Start.Y + (pair.Geometry.End.Y - pair.Geometry.Start.Y) * t,
            z);
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

    private static bool TryVerticalRange(Solid solid, double x, double y, double beamTop,
        out double bottom, out double top)
    {
        bottom = top = 0;
        var line = Line.CreateBound(new XYZ(x, y, beamTop - 20), new XYZ(x, y, beamTop + 20));
        using var options = new SolidCurveIntersectionOptions();
        var intersection = solid.IntersectWithCurve(line, options);
        var bestDistance = double.MaxValue;
        for (var i = 0; i < intersection.SegmentCount; i++)
        {
            var segment = intersection.GetCurveSegment(i);
            var low = Math.Min(segment.GetEndPoint(0).Z, segment.GetEndPoint(1).Z);
            var high = Math.Max(segment.GetEndPoint(0).Z, segment.GetEndPoint(1).Z);
            var distance = Math.Abs(low - beamTop);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bottom = low;
            top = high;
        }
        return bestDistance < double.MaxValue;
    }

    private static bool TryHorizontalRange(Solid solid, XYZ station, double z, XYZ right, Transform inverse,
        double beamLeft, double beamRight, out double left, out double rightX)
    {
        left = rightX = 0;
        var center = new XYZ(station.X, station.Y, z);
        var line = Line.CreateBound(center - right * 1000, center + right * 1000);
        using var options = new SolidCurveIntersectionOptions();
        var intersection = solid.IntersectWithCurve(line, options);
        var bestOverlap = 0.0;
        for (var i = 0; i < intersection.SegmentCount; i++)
        {
            var segment = intersection.GetCurveSegment(i);
            var x0 = inverse.OfPoint(segment.GetEndPoint(0)).X;
            var x1 = inverse.OfPoint(segment.GetEndPoint(1)).X;
            var low = Math.Min(x0, x1);
            var high = Math.Max(x0, x1);
            var overlap = Math.Max(0, Math.Min(high, beamRight) - Math.Max(low, beamLeft));
            if (overlap <= bestOverlap) continue;
            bestOverlap = overlap;
            left = low;
            rightX = high;
        }
        return bestOverlap > 1.0 / 304.8;
    }

    private static bool TryVerticalRangeAtLocalX(Solid solid, Transform transform, Transform inverse,
        double localX, double fallbackBottom, double fallbackTop, out double bottom, out double top)
    {
        var localY = LocalYFromWorldZ(transform, inverse, (fallbackBottom + fallbackTop) * 0.5);
        var world = transform.OfPoint(new XYZ(localX, localY, 0));
        if (TryVerticalRange(solid, world.X, world.Y, fallbackBottom, out bottom, out top)) return true;
        bottom = fallbackBottom;
        top = fallbackTop;
        return true;
    }

    private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ box)
    {
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            yield return new XYZ(x, y, z);
    }
}
