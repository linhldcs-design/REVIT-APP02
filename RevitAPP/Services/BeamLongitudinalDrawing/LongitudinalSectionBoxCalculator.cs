using Autodesk.Revit.DB;
using RevitAPP.Core.Models.BeamLongitudinalDrawing;

namespace RevitAPP.Services.BeamLongitudinalDrawing;

public sealed class LongitudinalSectionBoxCalculator
{
    private const double Margin = 1.5;
    private const double HalfDepth = 2.0;
    // Khớp RevitAPP.Services.BeamDrawing.SectionPlaneCalculator.
    private const double CrossMargin = 0.5;
    // Khớp code Bản Vẽ Dầm mẫu: 0.4 ft mỗi phía, tổng Far Clip ~244 mm.
    private const double CrossHalfDepth = 0.4;

    public BoundingBoxXYZ CreateLongitudinal(BeamChainModel chain, bool reversed)
    {
        var start = ToXyz(reversed ? chain.End : chain.Start);
        var end = ToXyz(reversed ? chain.Start : chain.End);
        var axis = (end - start).Normalize();
        var up = XYZ.BasisZ;
        var viewDirection = axis.CrossProduct(up).Normalize();
        var minBottom = chain.Spans.Min(span => Math.Min(span.Start.Z, span.End.Z) - span.HeightFeet * 0.5);
        var maxTop = chain.Spans.Max(span => Math.Max(span.Start.Z, span.End.Z) + span.HeightFeet * 0.5);
        var center = (start + end) * 0.5;
        center = new XYZ(center.X, center.Y, (minBottom + maxTop) * 0.5);
        return Box(center, axis, up, viewDirection,
            chain.TotalLengthFeet * 0.5 + Margin, (maxTop - minBottom) * 0.5 + Margin, HalfDepth);
    }

    public BoundingBoxXYZ CreateCross(
        BeamChainModel chain, double chainDistanceFeet, bool reversed, FamilyInstance? beam = null)
    {
        // Reverse chỉ đổi hướng nhìn/đọc, không được đổi vị trí vật lý của station đã review.
        var distance = chainDistanceFeet;
        var cumulative = 0d;
        var span = chain.Spans[^1];
        foreach (var candidate in chain.Spans)
        {
            if (distance <= cumulative + candidate.LengthFeet + 1e-9) { span = candidate; break; }
            cumulative += candidate.LengthFeet;
        }
        var local = Math.Clamp((distance - cumulative) / span.LengthFeet, 0, 1);
        var a = ToXyz(span.Start);
        var b = ToXyz(span.End);
        var axis = (b - a).Normalize();
        if (reversed) axis = -axis;
        var up = XYZ.BasisZ;
        var right = up.CrossProduct(axis).Normalize();
        var point = a + (b - a) * local;
        var center = new XYZ(point.X, point.Y, point.Z);

        if (beam != null && TryBeamBounds(beam, center, right, up,
                out var left, out var rightSide, out var bottom, out var top))
        {
            return Box(center, right, up, axis,
                left - CrossMargin, rightSide + CrossMargin,
                bottom - CrossMargin, top + CrossMargin,
                -CrossHalfDepth, CrossHalfDepth);
        }

        return Box(center, right, up, axis,
            span.WidthFeet * 0.5 + CrossMargin,
            span.HeightFeet * 0.5 + CrossMargin,
            CrossHalfDepth);
    }

    private static bool TryBeamBounds(
        FamilyInstance beam, XYZ origin, XYZ right, XYZ up,
        out double left, out double rightSide, out double bottom, out double top)
    {
        var points = BeamGeometryPoints(beam).ToList();
        if (points.Count == 0)
        {
            var bbox = beam.get_BoundingBox(null);
            if (bbox != null) points.AddRange(BoxCorners(bbox));
        }
        if (points.Count == 0)
        {
            left = rightSide = bottom = top = 0;
            return false;
        }

        var xs = points.Select(point => (point - origin).DotProduct(right)).ToList();
        var ys = points.Select(point => (point - origin).DotProduct(up)).ToList();
        left = xs.Min();
        rightSide = xs.Max();
        bottom = ys.Min();
        top = ys.Max();
        return rightSide - left > 1e-6 && top - bottom > 1e-6;
    }

    private static IEnumerable<XYZ> BeamGeometryPoints(FamilyInstance beam)
    {
        GeometryElement? geometry;
        try
        {
            geometry = beam.get_Geometry(new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            });
        }
        catch
        {
            yield break;
        }
        if (geometry == null) yield break;
        foreach (var point in GeometryPoints(geometry)) yield return point;
    }

    private static IEnumerable<XYZ> GeometryPoints(GeometryElement geometry)
    {
        foreach (var geometryObject in geometry)
        {
            if (geometryObject is GeometryInstance instance)
            {
                foreach (var point in GeometryPoints(instance.GetInstanceGeometry()))
                    yield return point;
            }
            else if (geometryObject is Solid { Volume: > 1e-9 } solid)
            {
                foreach (Edge edge in solid.Edges)
                {
                    var curve = edge.AsCurve();
                    yield return curve.GetEndPoint(0);
                    yield return curve.GetEndPoint(1);
                }
            }
        }
    }

    private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ box)
    {
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            yield return box.Transform.OfPoint(new XYZ(x, y, z));
    }

    private static BoundingBoxXYZ Box(XYZ center, XYZ x, XYZ y, XYZ z, double hx, double hy, double hz)
    {
        var transform = Transform.Identity;
        transform.Origin = center;
        transform.BasisX = x.Normalize(); transform.BasisY = y.Normalize(); transform.BasisZ = z.Normalize();
        return new BoundingBoxXYZ { Transform = transform, Min = new XYZ(-hx, -hy, -hz), Max = new XYZ(hx, hy, hz) };
    }

    private static BoundingBoxXYZ Box(
        XYZ center, XYZ x, XYZ y, XYZ z,
        double minX, double maxX, double minY, double maxY, double minZ, double maxZ)
    {
        var transform = Transform.Identity;
        transform.Origin = center;
        transform.BasisX = x.Normalize();
        transform.BasisY = y.Normalize();
        transform.BasisZ = z.Normalize();
        return new BoundingBoxXYZ
        {
            Transform = transform,
            Min = new XYZ(minX, minY, minZ),
            Max = new XYZ(maxX, maxY, maxZ)
        };
    }

    private static XYZ ToXyz(RevitAPP.Core.Models.BeamDrawing.Point3 point) => new(point.X, point.Y, point.Z);
}
